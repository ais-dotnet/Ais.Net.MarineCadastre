using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Ais.Net.MarineCadastre.TestSupport;

public enum TiffCompression
{
    None = 1,
    Lzw = 5,
    Deflate = 8,
}

/// <summary>
/// Writes a small GeoTIFF with the shape of a MarineCadastre transit-count raster, in a temporary
/// file, for specs and benchmarks. Shared between the two projects as a linked file.
/// </summary>
public sealed class GeoTiffBuilder : IDisposable
{
    private readonly List<(int Width, int Height, uint[] Cells)> images = [];

    public GeoTiffBuilder(int width, int height, uint[] cells)
    {
        if (cells.Length != width * height)
        {
            throw new ArgumentException("Cell count does not match the size.", nameof(cells));
        }

        this.images.Add((width, height, cells));
        this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ais-net-transits-{Guid.NewGuid():N}.tif");
    }

    public string Path { get; }

    /// <summary>Gets the tile edge in cells; 0 writes strips of <see cref="RowsPerStrip"/> rows instead.</summary>
    public int TileSize { get; init; } = 16;

    public int RowsPerStrip { get; init; } = 4;

    public TiffCompression Compression { get; init; } = TiffCompression.Lzw;

    public bool BigTiff { get; init; } = true;

    public bool BigEndian { get; init; }

    /// <summary>Gets a value indicating whether cells are in degrees rather than Web Mercator metres.</summary>
    public bool Geographic { get; init; }

    /// <summary>Gets a value indicating whether Web Mercator is declared as a user-defined CRS with a citation, as ArcGIS does, rather than as EPSG:3857.</summary>
    public bool UserDefinedWebMercator { get; init; }

    /// <summary>Gets a value indicating whether to declare an unsupported projected CRS instead.</summary>
    public bool UnsupportedProjection { get; init; }

    public bool OmitGeoKeys { get; init; }

    public bool PixelIsPoint { get; init; }

    /// <summary>Gets the model X of the west edge; defaults to a point near Seattle in Web Mercator metres.</summary>
    public double OriginX { get; init; } = -13630000;

    public double OriginY { get; init; } = 6050000;

    public double CellSize { get; init; } = 100;

    public uint? NoData { get; init; } = 0;

    public int BitsPerSample { get; init; } = 32;

    public bool SignedSamples { get; init; }

    public int Predictor { get; init; } = 1;

    public string? GdalMetadata { get; init; }

    /// <summary>Gets a compression code to declare in the directory instead of the one actually used.</summary>
    public int? DeclaredCompression { get; init; }

    public int SamplesPerPixel { get; init; } = 1;

    /// <summary>Gets a value indicating whether georeferencing is written as a ModelTransformation matrix rather than tiepoint and scale.</summary>
    public bool UseModelTransformation { get; init; }

    /// <summary>Gets a value indicating whether the ModelTransformation carries rotation terms.</summary>
    public bool RotatedTransformation { get; init; }

    /// <summary>Gets a value indicating whether the pixel scale tag is left out, leaving a tiepoint alone.</summary>
    public bool OmitPixelScale { get; init; }

    /// <summary>Gets a value indicating whether all-zero blocks are written with no data and a zero byte count, as GDAL's SPARSE_OK does.</summary>
    public bool SparseEmptyBlocks { get; init; }

    /// <summary>
    /// Adds an overview level. Levels are written in the order added and should halve each time.
    /// </summary>
    public GeoTiffBuilder WithOverview(int width, int height, uint[] cells)
    {
        this.images.Add((width, height, cells));
        return this;
    }

    public string Write()
    {
        using FileStream stream = File.Create(this.Path);
        TiffWriter writer = new(stream, this.BigTiff, this.BigEndian);
        writer.WriteHeader();

        for (int i = 0; i < this.images.Count; i++)
        {
            (int width, int height, uint[] cells) = this.images[i];
            bool tiled = this.TileSize > 0;
            int blockWidth = tiled ? this.TileSize : width;
            int blockHeight = tiled ? this.TileSize : Math.Min(this.RowsPerStrip, height);
            int across = (width + blockWidth - 1) / blockWidth;
            int down = (height + blockHeight - 1) / blockHeight;

            List<long> offsets = [];
            List<long> counts = [];
            for (int by = 0; by < down; by++)
            {
                for (int bx = 0; bx < across; bx++)
                {
                    int rows = tiled ? blockHeight : Math.Min(blockHeight, height - (by * blockHeight));
                    byte[] raw = this.EncodeBlock(cells, width, height, bx * blockWidth, by * blockHeight, blockWidth, rows);
                    if (this.SparseEmptyBlocks && !raw.AsSpan().ContainsAnyExcept((byte)0))
                    {
                        offsets.Add(0);
                        counts.Add(0);
                        continue;
                    }

                    byte[] stored = this.Compression switch
                    {
                        TiffCompression.None => raw,
                        TiffCompression.Lzw => LzwEncode(raw),
                        TiffCompression.Deflate => Deflate(raw),
                        _ => throw new InvalidOperationException(),
                    };
                    offsets.Add(writer.WriteBytes(stored));
                    counts.Add(stored.Length);
                }
            }

            List<TiffWriter.Entry> entries =
            [
                new(254, 4, [i == 0 ? 0L : 1L]),
                new(256, 4, [width]),
                new(257, 4, [height]),
                new(258, 3, [this.BitsPerSample]),
                new(259, 3, [this.DeclaredCompression ?? (long)this.Compression]),
                new(262, 3, [1]),
                new(277, 3, [this.SamplesPerPixel]),
                new(284, 3, [1]),
                new(317, 3, [this.Predictor]),
                new(339, 3, [this.SignedSamples ? 2L : 1L]),
            ];

            if (tiled)
            {
                entries.Add(new(322, 3, [blockWidth]));
                entries.Add(new(323, 3, [blockHeight]));
                entries.Add(new(324, this.BigTiff ? 16 : 4, offsets));
                entries.Add(new(325, 4, counts));
            }
            else
            {
                entries.Add(new(278, 4, [blockHeight]));
                entries.Add(new(273, this.BigTiff ? 16 : 4, offsets));
                entries.Add(new(279, 4, counts));
            }

            if (i == 0)
            {
                double tiepointX = this.PixelIsPoint ? this.OriginX + (this.CellSize / 2) : this.OriginX;
                double tiepointY = this.PixelIsPoint ? this.OriginY - (this.CellSize / 2) : this.OriginY;
                if (this.UseModelTransformation)
                {
                    double rotation = this.RotatedTransformation ? 1 : 0;
                    entries.Add(new(34264, 12, Doubles:
                    [
                        this.CellSize, rotation, 0, tiepointX,
                        rotation, -this.CellSize, 0, tiepointY,
                        0, 0, 0, 0,
                        0, 0, 0, 1,
                    ]));
                }
                else
                {
                    if (!this.OmitPixelScale)
                    {
                        entries.Add(new(33550, 12, Doubles: [this.CellSize, this.CellSize, 0]));
                    }

                    entries.Add(new(33922, 12, Doubles: [0, 0, 0, tiepointX, tiepointY, 0]));
                }

                if (!this.OmitGeoKeys)
                {
                    string ascii = this.UserDefinedWebMercator ? "WGS_1984_Web_Mercator_Auxiliary_Sphere|" : this.UnsupportedProjection ? "NAD83 / Conus Albers|" : "GeoTIFF|";
                    List<long> keys = [1, 1, 0, 0];
                    void Key(int id, int location, int count, int value) => keys.AddRange([id, location, count, value]);
                    Key(1024, 0, 1, this.Geographic ? 2 : 1);
                    Key(1025, 0, 1, this.PixelIsPoint ? 2 : 1);
                    if (this.Geographic)
                    {
                        Key(2048, 0, 1, 4326);
                    }
                    else if (this.UnsupportedProjection)
                    {
                        Key(3072, 0, 1, 5070);
                        Key(3073, 34737, ascii.Length, 0);
                    }
                    else if (this.UserDefinedWebMercator)
                    {
                        Key(3072, 0, 1, 32767);
                        Key(3073, 34737, ascii.Length, 0);
                    }
                    else
                    {
                        Key(3072, 0, 1, 3857);
                    }

                    keys[3] = (keys.Count - 4) / 4;
                    entries.Add(new(34735, 3, keys));
                    entries.Add(new(34737, 2, Ascii: ascii));
                }

                if (this.GdalMetadata is not null)
                {
                    entries.Add(new(42112, 2, Ascii: this.GdalMetadata));
                }

                if (this.NoData is { } noData)
                {
                    entries.Add(new(42113, 2, Ascii: noData.ToString()));
                }
            }

            writer.WriteDirectory(entries);
        }

        writer.Finish();
        return this.Path;
    }

    public void Dispose()
    {
        try
        {
            File.Delete(this.Path);
        }
        catch (IOException)
        {
        }
    }

    internal static byte[] LzwEncode(ReadOnlySpan<byte> data)
    {
        MemoryStream output = new();
        ulong bitBuffer = 0;
        int bitCount = 0;
        int codeBits = 9;

        void Emit(int code)
        {
            bitBuffer = (bitBuffer << codeBits) | (uint)code;
            bitCount += codeBits;
            while (bitCount >= 8)
            {
                output.WriteByte((byte)(bitBuffer >> (bitCount - 8)));
                bitCount -= 8;
            }
        }

        Dictionary<int, int> table = [];
        int next = 258;
        Emit(256);
        int prefix = -1;
        foreach (byte b in data)
        {
            if (prefix < 0)
            {
                prefix = b;
                continue;
            }

            int key = (prefix << 8) | b;
            if (table.TryGetValue(key, out int code))
            {
                prefix = code;
                continue;
            }

            Emit(prefix);
            table[key] = next++;
            if (next >= 1 << codeBits && codeBits < 12)
            {
                codeBits++;
            }

            prefix = b;

            if (next >= 4094)
            {
                Emit(256);
                table.Clear();
                next = 258;
                codeBits = 9;
            }
        }

        if (prefix >= 0)
        {
            Emit(prefix);
        }

        Emit(257);
        if (bitCount > 0)
        {
            output.WriteByte((byte)(bitBuffer << (8 - bitCount)));
        }

        return output.ToArray();
    }

    private static byte[] Deflate(byte[] raw)
    {
        MemoryStream output = new();
        using (ZLibStream deflater = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflater.Write(raw);
        }

        return output.ToArray();
    }

    private byte[] EncodeBlock(uint[] cells, int width, int height, int x0, int y0, int blockWidth, int rows)
    {
        int bytesPerSample = this.BitsPerSample / 8;
        byte[] raw = new byte[blockWidth * rows * bytesPerSample];
        for (int row = 0; row < rows; row++)
        {
            uint previous = 0;
            for (int column = 0; column < blockWidth; column++)
            {
                int x = x0 + column;
                int y = y0 + row;
                uint value = x < width && y < height ? cells[(y * width) + x] : 0;
                uint stored = value;
                if (this.Predictor == 2)
                {
                    stored = value - previous;
                    previous = value;
                }

                Span<byte> target = raw.AsSpan(((row * blockWidth) + column) * bytesPerSample, bytesPerSample);
                switch (bytesPerSample)
                {
                    case 1:
                        target[0] = (byte)stored;
                        break;
                    case 2:
                        if (this.BigEndian)
                        {
                            BinaryPrimitives.WriteUInt16BigEndian(target, (ushort)stored);
                        }
                        else
                        {
                            BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)stored);
                        }

                        break;
                    default:
                        if (this.BigEndian)
                        {
                            BinaryPrimitives.WriteUInt32BigEndian(target, stored);
                        }
                        else
                        {
                            BinaryPrimitives.WriteUInt32LittleEndian(target, stored);
                        }

                        break;
                }
            }
        }

        return raw;
    }

    /// <summary>
    /// Appends data and directories to a TIFF or BigTIFF stream, patching each directory's pointer to the next.
    /// </summary>
    private sealed class TiffWriter(Stream stream, bool bigTiff, bool bigEndian)
    {
        private long nextPointerPosition;

        public void WriteHeader()
        {
            stream.Write(bigEndian ? "MM"u8 : "II"u8);
            this.WriteUInt16(bigTiff ? 43 : 42);
            if (bigTiff)
            {
                this.WriteUInt16(8);
                this.WriteUInt16(0);
                this.nextPointerPosition = stream.Position;
                this.WriteOffset(0);
            }
            else
            {
                this.nextPointerPosition = stream.Position;
                this.WriteOffset(0);
            }
        }

        public long WriteBytes(byte[] data)
        {
            this.Align();
            long offset = stream.Position;
            stream.Write(data);
            return offset;
        }

        public void WriteDirectory(List<Entry> entries)
        {
            entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));
            int inlineSize = bigTiff ? 8 : 4;

            // Values that do not fit inline go first, so their offsets are known when the directory is written.
            Dictionary<Entry, long> external = [];
            foreach (Entry entry in entries)
            {
                byte[] bytes = this.Serialise(entry);
                if (bytes.Length > inlineSize)
                {
                    external[entry] = this.WriteBytes(bytes);
                }
            }

            this.Align();
            long directoryOffset = stream.Position;
            this.PatchNextPointer(directoryOffset);

            if (bigTiff)
            {
                this.WriteUInt64((ulong)entries.Count);
            }
            else
            {
                this.WriteUInt16(entries.Count);
            }

            foreach (Entry entry in entries)
            {
                this.WriteUInt16(entry.Tag);
                this.WriteUInt16(entry.Type);
                long count = entry.Count;
                if (bigTiff)
                {
                    this.WriteUInt64((ulong)count);
                }
                else
                {
                    this.WriteUInt32((uint)count);
                }

                if (external.TryGetValue(entry, out long offset))
                {
                    this.WriteOffset(offset);
                }
                else
                {
                    byte[] bytes = this.Serialise(entry);
                    stream.Write(bytes);
                    stream.Write(new byte[inlineSize - bytes.Length]);
                }
            }

            this.nextPointerPosition = stream.Position;
            this.WriteOffset(0);
        }

        public void Finish() => stream.Flush();

        private void PatchNextPointer(long value)
        {
            long end = stream.Position;
            stream.Position = this.nextPointerPosition;
            this.WriteOffset(value);
            stream.Position = end;
        }

        private void Align()
        {
            while (stream.Position % 2 != 0)
            {
                stream.WriteByte(0);
            }
        }

        private byte[] Serialise(Entry entry)
        {
            if (entry.Ascii is not null)
            {
                return Encoding.ASCII.GetBytes(entry.Ascii + "\0");
            }

            MemoryStream buffer = new();
            Span<byte> scratch = stackalloc byte[8];
            if (entry.Doubles is not null)
            {
                foreach (double value in entry.Doubles)
                {
                    if (bigEndian)
                    {
                        BinaryPrimitives.WriteDoubleBigEndian(scratch, value);
                    }
                    else
                    {
                        BinaryPrimitives.WriteDoubleLittleEndian(scratch, value);
                    }

                    buffer.Write(scratch);
                }

                return buffer.ToArray();
            }

            foreach (long value in entry.Integers!)
            {
                int size = entry.Type switch { 3 => 2, 4 => 4, 16 => 8, _ => throw new InvalidOperationException() };
                if (bigEndian)
                {
                    BinaryPrimitives.WriteUInt64BigEndian(scratch, (ulong)value);
                    buffer.Write(scratch[(8 - size)..]);
                }
                else
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(scratch, (ulong)value);
                    buffer.Write(scratch[..size]);
                }
            }

            return buffer.ToArray();
        }

        private void WriteUInt16(int value)
        {
            Span<byte> bytes = stackalloc byte[2];
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
            }

            stream.Write(bytes);
        }

        private void WriteUInt32(uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            }

            stream.Write(bytes);
        }

        private void WriteUInt64(ulong value)
        {
            Span<byte> bytes = stackalloc byte[8];
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
            }

            stream.Write(bytes);
        }

        private void WriteOffset(long value)
        {
            if (bigTiff)
            {
                this.WriteUInt64((ulong)value);
            }
            else
            {
                this.WriteUInt32((uint)value);
            }
        }

        public sealed record Entry(int Tag, int Type, IReadOnlyList<long>? Integers = null, IReadOnlyList<double>? Doubles = null, string? Ascii = null)
        {
            public long Count => this.Ascii is not null ? this.Ascii.Length + 1 : this.Doubles?.Count ?? this.Integers!.Count;
        }
    }
}
