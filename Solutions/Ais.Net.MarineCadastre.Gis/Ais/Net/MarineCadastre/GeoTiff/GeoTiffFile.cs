// <copyright file="GeoTiffFile.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

using Microsoft.Win32.SafeHandles;

namespace Ais.Net.MarineCadastre.GeoTiff;

/// <summary>
/// One resolution level of a GeoTIFF: the full image or one of its overviews, in tiles or strips.
/// </summary>
internal sealed class RasterLevel
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int BlockWidth { get; init; }

    public required int BlockHeight { get; init; }

    public required bool Tiled { get; init; }

    public required int Compression { get; init; }

    public required int Predictor { get; init; }

    public required int BitsPerSample { get; init; }

    public required int SampleFormat { get; init; }

    public required long[] BlockOffsets { get; init; }

    public required long[] BlockByteCounts { get; init; }

    public int BlocksAcross => (this.Width + this.BlockWidth - 1) / this.BlockWidth;

    public int BlocksDown => (this.Height + this.BlockHeight - 1) / this.BlockHeight;
}

/// <summary>
/// Reads the parts of a GeoTIFF a transit-count raster needs: the image directories (classic and
/// BigTIFF), tiepoint-and-scale georeferencing with a recognised coordinate system, GDAL's NoData and
/// statistics, and individual tiles or strips decoded from LZW, Deflate or no compression.
/// </summary>
internal sealed class GeoTiffFile : IDisposable
{
    private const int TagNewSubfileType = 254;
    private const int TagImageWidth = 256;
    private const int TagImageLength = 257;
    private const int TagBitsPerSample = 258;
    private const int TagCompression = 259;
    private const int TagStripOffsets = 273;
    private const int TagSamplesPerPixel = 277;
    private const int TagRowsPerStrip = 278;
    private const int TagStripByteCounts = 279;
    private const int TagPlanarConfiguration = 284;
    private const int TagPredictor = 317;
    private const int TagTileWidth = 322;
    private const int TagTileLength = 323;
    private const int TagTileOffsets = 324;
    private const int TagTileByteCounts = 325;
    private const int TagSampleFormat = 339;
    private const int TagModelPixelScale = 33550;
    private const int TagModelTiepoint = 33922;
    private const int TagModelTransformation = 34264;
    private const int TagGeoKeyDirectory = 34735;
    private const int TagGeoDoubleParams = 34736;
    private const int TagGeoAsciiParams = 34737;
    private const int TagGdalMetadata = 42112;
    private const int TagGdalNoData = 42113;

    private const int CompressionNone = 1;
    private const int CompressionLzw = 5;
    private const int CompressionDeflate = 8;
    private const int CompressionDeflateLegacy = 32946;

    private readonly SafeFileHandle handle;

    private GeoTiffFile(SafeFileHandle handle, bool littleEndian, IReadOnlyList<RasterLevel> levels, RasterGeoreference georeference, double? noData, string? gdalMetadata)
    {
        this.handle = handle;
        this.LittleEndian = littleEndian;
        this.Levels = levels;
        this.Georeference = georeference;
        this.NoData = noData;
        this.GdalMetadata = gdalMetadata;
    }

    public bool LittleEndian { get; }

    /// <summary>
    /// Gets the resolution levels, full resolution first and then overviews from largest to smallest.
    /// </summary>
    public IReadOnlyList<RasterLevel> Levels { get; }

    public RasterGeoreference Georeference { get; }

    public double? NoData { get; }

    public string? GdalMetadata { get; }

    /// <summary>
    /// Opens a GeoTIFF and reads its directories.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <returns>The open file.</returns>
    /// <exception cref="InvalidDataException">Thrown when the file is not a TIFF, has no georeferencing or has an unusable layout.</exception>
    /// <exception cref="NotSupportedException">Thrown when the coordinate system or sample type is not one this reader handles.</exception>
    public static GeoTiffFile Open(string path)
    {
        SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        try
        {
            return Parse(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Decodes one tile or strip into cell values.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <param name="blockIndex">The block, row-major.</param>
    /// <param name="cells">Receives <c>BlockWidth × BlockHeight</c> values; rows beyond the image are zero.</param>
    public void DecodeBlock(RasterLevel level, int blockIndex, Span<uint> cells)
    {
        long byteCount = level.BlockByteCounts[blockIndex];
        byte[] compressed = ArrayPool<byte>.Shared.Rent(checked((int)byteCount));
        try
        {
            this.ReadExactly(level.BlockOffsets[blockIndex], compressed.AsSpan(0, (int)byteCount));
            this.DecodeBlock(level, compressed.AsSpan(0, (int)byteCount), cells);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(compressed);
        }
    }

    /// <summary>
    /// Reads the compressed bytes of one block, for callers that want to compare blocks before decoding.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <param name="blockIndex">The block, row-major.</param>
    /// <param name="buffer">Receives the bytes; at least <c>BlockByteCounts[blockIndex]</c> long.</param>
    /// <returns>The number of bytes read.</returns>
    public int ReadBlockBytes(RasterLevel level, int blockIndex, Span<byte> buffer)
    {
        int byteCount = checked((int)level.BlockByteCounts[blockIndex]);
        this.ReadExactly(level.BlockOffsets[blockIndex], buffer[..byteCount]);
        return byteCount;
    }

    /// <summary>
    /// Decodes compressed block bytes into cell values. A span shorter than the block decodes only its
    /// leading cells, which is all a point lookup needs, and LZW and Deflate stop there.
    /// </summary>
    /// <param name="level">The level.</param>
    /// <param name="compressed">The block's bytes as stored.</param>
    /// <param name="cells">Receives up to <c>BlockWidth × BlockHeight</c> values, from the block's first cell.</param>
    public void DecodeBlock(RasterLevel level, ReadOnlySpan<byte> compressed, Span<uint> cells)
    {
        int bytesPerSample = level.BitsPerSample / 8;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cells.Length, level.BlockWidth * level.BlockHeight, nameof(cells));
        int rawLength = cells.Length * bytesPerSample;
        byte[] raw = ArrayPool<byte>.Shared.Rent(rawLength);
        try
        {
            Span<byte> rawSpan = raw.AsSpan(0, rawLength);
            int decoded;
            switch (level.Compression)
            {
                case CompressionNone:
                    decoded = Math.Min(compressed.Length, rawLength);
                    compressed[..decoded].CopyTo(rawSpan);
                    break;

                case CompressionLzw:
                    decoded = LzwDecoder.Decode(compressed, rawSpan);
                    break;

                case CompressionDeflate:
                case CompressionDeflateLegacy:
                {
                    using ZLibStream inflater = new(new ReadOnlyMemoryStream(compressed.ToArray()), CompressionMode.Decompress);
                    decoded = 0;
                    int read;
                    while (decoded < rawLength && (read = inflater.Read(rawSpan[decoded..])) > 0)
                    {
                        decoded += read;
                    }

                    break;
                }

                default:
                    throw new NotSupportedException($"TIFF compression {level.Compression} is not supported; only none, LZW and Deflate are.");
            }

            rawSpan[decoded..].Clear();

            for (int i = 0; i < cells.Length; i++)
            {
                ReadOnlySpan<byte> sample = rawSpan.Slice(i * bytesPerSample, bytesPerSample);
                cells[i] = level.BitsPerSample switch
                {
                    8 => level.SampleFormat == 2 ? unchecked((uint)(sbyte)sample[0]) : sample[0],
                    16 => level.SampleFormat == 2
                        ? unchecked((uint)(this.LittleEndian ? BinaryPrimitives.ReadInt16LittleEndian(sample) : BinaryPrimitives.ReadInt16BigEndian(sample)))
                        : this.LittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(sample) : BinaryPrimitives.ReadUInt16BigEndian(sample),
                    _ => this.LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(sample) : BinaryPrimitives.ReadUInt32BigEndian(sample),
                };
            }

            if (level.Predictor == 2)
            {
                uint mask = level.BitsPerSample == 32 ? uint.MaxValue : (1u << level.BitsPerSample) - 1;
                for (int rowStart = 0; rowStart < cells.Length; rowStart += level.BlockWidth)
                {
                    Span<uint> line = cells.Slice(rowStart, Math.Min(level.BlockWidth, cells.Length - rowStart));
                    for (int column = 1; column < line.Length; column++)
                    {
                        line[column] = (line[column] + line[column - 1]) & mask;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(raw);
        }
    }

    public void Dispose() => this.handle.Dispose();

    private static GeoTiffFile Parse(SafeFileHandle handle)
    {
        Span<byte> header = stackalloc byte[16];
        ReadExactly(handle, 0, header);
        bool littleEndian = header[0] == (byte)'I' && header[1] == (byte)'I';
        if (!littleEndian && !(header[0] == (byte)'M' && header[1] == (byte)'M'))
        {
            throw new InvalidDataException("The file is not a TIFF: it does not start with a byte-order mark.");
        }

        int version = ReadUInt16(header[2..], littleEndian);
        bool bigTiff = version switch
        {
            42 => false,
            43 => true,
            _ => throw new InvalidDataException($"The file is not a TIFF: unexpected version {version}."),
        };

        long nextDirectory = bigTiff ? ReadInt64(header[8..], littleEndian) : ReadUInt32(header[4..], littleEndian);

        List<(RasterLevel Level, bool Reduced)> levels = [];
        RasterGeoreference? georeference = null;
        double? noData = null;
        string? gdalMetadata = null;

        int directories = 0;
        while (nextDirectory != 0 && directories++ < 64)
        {
            Dictionary<int, TiffEntry> entries = ReadDirectory(handle, nextDirectory, littleEndian, bigTiff, out nextDirectory);

            long subfileType = entries.TryGetValue(TagNewSubfileType, out TiffEntry? subfile) ? GetIntegers(handle, subfile, littleEndian)[0] : 0;
            if ((subfileType & 4) != 0)
            {
                continue; // A transparency mask, not image data.
            }

            RasterLevel level = ReadLevel(handle, entries, littleEndian);
            levels.Add((level, (subfileType & 1) != 0));

            if (georeference is null && entries.ContainsKey(TagGeoKeyDirectory))
            {
                georeference = ReadGeoreference(handle, entries, littleEndian);
                if (entries.TryGetValue(TagGdalNoData, out TiffEntry? noDataEntry)
                    && double.TryParse(GetAscii(handle, noDataEntry), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedNoData))
                {
                    noData = parsedNoData;
                }

                if (entries.TryGetValue(TagGdalMetadata, out TiffEntry? metadataEntry))
                {
                    gdalMetadata = GetAscii(handle, metadataEntry);
                }
            }
        }

        if (levels.Count == 0)
        {
            throw new InvalidDataException("The TIFF has no image directory.");
        }

        if (georeference is null)
        {
            throw new InvalidDataException("The TIFF has no GeoTIFF keys, so its cells cannot be placed on the Earth.");
        }

        List<RasterLevel> ordered = [.. levels.OrderBy(l => l.Reduced).ThenByDescending(l => l.Level.Width).Select(l => l.Level)];
        return new GeoTiffFile(handle, littleEndian, ordered, georeference, noData, gdalMetadata);
    }

    private static Dictionary<int, TiffEntry> ReadDirectory(SafeFileHandle handle, long offset, bool littleEndian, bool bigTiff, out long nextDirectory)
    {
        int countSize = bigTiff ? 8 : 2;
        int entrySize = bigTiff ? 20 : 12;
        int offsetSize = bigTiff ? 8 : 4;

        Span<byte> countBytes = stackalloc byte[8];
        ReadExactly(handle, offset, countBytes[..countSize]);
        long count = bigTiff ? ReadInt64(countBytes, littleEndian) : ReadUInt16(countBytes, littleEndian);
        if (count is < 0 or > 4096)
        {
            throw new InvalidDataException("A TIFF directory has an implausible entry count.");
        }

        byte[] block = new byte[(count * entrySize) + offsetSize];
        ReadExactly(handle, offset + countSize, block);

        Dictionary<int, TiffEntry> entries = new((int)count);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = block.AsSpan(i * entrySize, entrySize);
            int tag = ReadUInt16(entry, littleEndian);
            int type = ReadUInt16(entry[2..], littleEndian);
            long valueCount = bigTiff ? ReadInt64(entry[4..], littleEndian) : ReadUInt32(entry[4..], littleEndian);
            byte[] inline = entry[(bigTiff ? 12 : 8)..].ToArray();
            entries[tag] = new TiffEntry(type, valueCount, inline) { LittleEndianOffset = littleEndian };
        }

        ReadOnlySpan<byte> next = block.AsSpan((int)(count * entrySize), offsetSize);
        nextDirectory = bigTiff ? ReadInt64(next, littleEndian) : ReadUInt32(next, littleEndian);
        return entries;
    }

    private static RasterLevel ReadLevel(SafeFileHandle handle, Dictionary<int, TiffEntry> entries, bool littleEndian)
    {
        long Integer(int tag, long fallback) => entries.TryGetValue(tag, out TiffEntry? entry) ? GetIntegers(handle, entry, littleEndian)[0] : fallback;

        long samplesPerPixel = Integer(TagSamplesPerPixel, 1);
        long planar = Integer(TagPlanarConfiguration, 1);
        if (samplesPerPixel != 1 && planar != 2)
        {
            throw new NotSupportedException("Only single-band rasters are supported.");
        }

        int bitsPerSample = (int)Integer(TagBitsPerSample, 1);
        int sampleFormat = (int)Integer(TagSampleFormat, 1);
        if (bitsPerSample is not (8 or 16 or 32) || sampleFormat is not (1 or 2))
        {
            throw new NotSupportedException($"Only 8, 16 and 32-bit integer samples are supported, not {bitsPerSample}-bit sample format {sampleFormat}.");
        }

        int width = checked((int)Integer(TagImageWidth, 0));
        int height = checked((int)Integer(TagImageLength, 0));
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("A TIFF directory has no image size.");
        }

        bool tiled = entries.ContainsKey(TagTileOffsets);
        int blockWidth;
        int blockHeight;
        long[] offsets;
        long[] byteCounts;
        if (tiled)
        {
            blockWidth = (int)Integer(TagTileWidth, 0);
            blockHeight = (int)Integer(TagTileLength, 0);
            offsets = GetIntegers(handle, entries[TagTileOffsets], littleEndian);
            byteCounts = GetIntegers(handle, entries[TagTileByteCounts], littleEndian);
        }
        else
        {
            if (!entries.ContainsKey(TagStripOffsets))
            {
                throw new InvalidDataException("A TIFF directory has neither tiles nor strips.");
            }

            blockWidth = width;
            blockHeight = (int)Math.Min(Integer(TagRowsPerStrip, height), height);
            offsets = GetIntegers(handle, entries[TagStripOffsets], littleEndian);
            byteCounts = GetIntegers(handle, entries[TagStripByteCounts], littleEndian);
        }

        if (blockWidth <= 0 || blockHeight <= 0 || offsets.Length != byteCounts.Length)
        {
            throw new InvalidDataException("A TIFF directory has an inconsistent block layout.");
        }

        RasterLevel level = new()
        {
            Width = width,
            Height = height,
            BlockWidth = blockWidth,
            BlockHeight = blockHeight,
            Tiled = tiled,
            Compression = (int)Integer(TagCompression, CompressionNone),
            Predictor = (int)Integer(TagPredictor, 1),
            BitsPerSample = bitsPerSample,
            SampleFormat = sampleFormat,
            BlockOffsets = offsets,
            BlockByteCounts = byteCounts,
        };

        if (offsets.Length < (long)level.BlocksAcross * level.BlocksDown)
        {
            throw new InvalidDataException("A TIFF directory has fewer blocks than its size requires.");
        }

        return level;
    }

    private static RasterGeoreference ReadGeoreference(SafeFileHandle handle, Dictionary<int, TiffEntry> entries, bool littleEndian)
    {
        long[] keys = GetIntegers(handle, entries[TagGeoKeyDirectory], littleEndian);
        string ascii = entries.TryGetValue(TagGeoAsciiParams, out TiffEntry? asciiEntry) ? GetAscii(handle, asciiEntry) : string.Empty;

        int modelType = 0;
        int rasterType = 1;
        int projectedCs = 0;
        int geographicCs = 0;
        StringBuilder citations = new();
        for (int i = 4; i + 3 < keys.Length && i < 4 + (keys[3] * 4); i += 4)
        {
            int keyId = (int)keys[i];
            int location = (int)keys[i + 1];
            int count = (int)keys[i + 2];
            int value = (int)keys[i + 3];
            switch (keyId)
            {
                case 1024:
                    modelType = value;
                    break;
                case 1025:
                    rasterType = value;
                    break;
                case 2048:
                    geographicCs = value;
                    break;
                case 3072:
                    projectedCs = value;
                    break;
                case 1026 or 2049 or 3073 when location == TagGeoAsciiParams && value + count <= ascii.Length:
                    citations.Append(ascii, value, count).Append(' ');
                    break;
            }
        }

        string citation = citations.ToString().Replace('|', ' ').Trim();
        RasterProjection projection;
        if (modelType == 2)
        {
            projection = RasterProjection.Geographic;
        }
        else if (modelType == 1 && (projectedCs is 3857 or 3785 or 900913 || (projectedCs == 32767 && LooksLikeWebMercator(citation))))
        {
            projection = RasterProjection.WebMercator;
        }
        else
        {
            string described = projectedCs != 0 ? $"projected CRS {projectedCs}" : geographicCs != 0 ? $"geographic CRS {geographicCs}" : $"model type {modelType}";
            throw new NotSupportedException($"The raster uses {described} ({citation}); only geographic WGS 84 and Web Mercator are supported.");
        }

        double originX;
        double originY;
        double cellWidth;
        double cellHeight;
        if (entries.TryGetValue(TagModelTiepoint, out TiffEntry? tiepointEntry) && entries.TryGetValue(TagModelPixelScale, out TiffEntry? scaleEntry))
        {
            double[] tiepoint = GetDoubles(handle, tiepointEntry, littleEndian);
            double[] scale = GetDoubles(handle, scaleEntry, littleEndian);
            if (tiepoint.Length < 6 || scale.Length < 2 || scale[0] <= 0 || scale[1] <= 0)
            {
                throw new InvalidDataException("The GeoTIFF tiepoint or pixel scale is malformed.");
            }

            cellWidth = scale[0];
            cellHeight = scale[1];
            originX = tiepoint[3] - (tiepoint[0] * cellWidth);
            originY = tiepoint[4] + (tiepoint[1] * cellHeight);
        }
        else if (entries.TryGetValue(TagModelTransformation, out TiffEntry? transformEntry))
        {
            double[] m = GetDoubles(handle, transformEntry, littleEndian);
            if (m.Length < 16 || m[1] != 0 || m[4] != 0 || m[0] <= 0 || m[5] >= 0)
            {
                throw new NotSupportedException("Only north-up, unrotated rasters are supported.");
            }

            cellWidth = m[0];
            cellHeight = -m[5];
            originX = m[3];
            originY = m[7];
        }
        else
        {
            throw new InvalidDataException("The GeoTIFF has no tiepoint, pixel scale or transformation.");
        }

        if (rasterType == 2)
        {
            // PixelIsPoint: the tiepoint names the centre of the first cell, not its corner.
            originX -= cellWidth / 2;
            originY += cellHeight / 2;
        }

        return new RasterGeoreference(projection, originX, originY, cellWidth, cellHeight);
    }

    private static bool LooksLikeWebMercator(string citation) =>
        citation.Contains("Pseudo-Mercator", StringComparison.OrdinalIgnoreCase)
        || citation.Contains("Web_Mercator", StringComparison.OrdinalIgnoreCase)
        || citation.Contains("Web Mercator", StringComparison.OrdinalIgnoreCase)
        || citation.Contains("Mercator_Auxiliary_Sphere", StringComparison.OrdinalIgnoreCase)
        || citation.Contains("3857", StringComparison.Ordinal);

    private static long[] GetIntegers(SafeFileHandle handle, TiffEntry entry, bool littleEndian)
    {
        int size = entry.Type switch
        {
            1 or 2 or 6 or 7 => 1,
            3 or 8 => 2,
            4 or 9 or 13 => 4,
            16 or 17 or 18 => 8,
            _ => throw new InvalidDataException($"TIFF type {entry.Type} is not an integer type."),
        };

        byte[] data = GetBytes(handle, entry, size);
        long[] values = new long[entry.Count];
        for (int i = 0; i < values.Length; i++)
        {
            ReadOnlySpan<byte> item = data.AsSpan(i * size, size);
            values[i] = size switch
            {
                1 => entry.Type == 6 ? (sbyte)item[0] : item[0],
                2 => entry.Type == 8 ? (littleEndian ? BinaryPrimitives.ReadInt16LittleEndian(item) : BinaryPrimitives.ReadInt16BigEndian(item)) : ReadUInt16(item, littleEndian),
                4 => entry.Type == 9 ? (littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(item) : BinaryPrimitives.ReadInt32BigEndian(item)) : ReadUInt32(item, littleEndian),
                _ => ReadInt64(item, littleEndian),
            };
        }

        return values;
    }

    private static double[] GetDoubles(SafeFileHandle handle, TiffEntry entry, bool littleEndian)
    {
        int size = entry.Type switch
        {
            11 => 4,
            12 => 8,
            _ => throw new InvalidDataException($"TIFF type {entry.Type} is not a floating-point type."),
        };

        byte[] data = GetBytes(handle, entry, size);
        double[] values = new double[entry.Count];
        for (int i = 0; i < values.Length; i++)
        {
            ReadOnlySpan<byte> item = data.AsSpan(i * size, size);
            values[i] = size == 4
                ? (littleEndian ? BinaryPrimitives.ReadSingleLittleEndian(item) : BinaryPrimitives.ReadSingleBigEndian(item))
                : littleEndian ? BinaryPrimitives.ReadDoubleLittleEndian(item) : BinaryPrimitives.ReadDoubleBigEndian(item);
        }

        return values;
    }

    private static string GetAscii(SafeFileHandle handle, TiffEntry entry)
    {
        byte[] data = GetBytes(handle, entry, 1);
        return Encoding.ASCII.GetString(data).TrimEnd('\0');
    }

    private static byte[] GetBytes(SafeFileHandle handle, TiffEntry entry, int itemSize)
    {
        long length = entry.Count * itemSize;
        if (length <= entry.Inline.Length)
        {
            return entry.Inline[..(int)length];
        }

        if (length > 256 * 1024 * 1024)
        {
            throw new InvalidDataException("A TIFF tag is implausibly large.");
        }

        byte[] data = new byte[length];
        long offset = entry.Inline.Length == 8 ? BinaryPrimitives.ReadInt64LittleEndian(entry.Inline) : BinaryPrimitives.ReadUInt32LittleEndian(entry.Inline);
        if (!entry.LittleEndianOffset)
        {
            offset = entry.Inline.Length == 8 ? BinaryPrimitives.ReadInt64BigEndian(entry.Inline) : BinaryPrimitives.ReadUInt32BigEndian(entry.Inline);
        }

        ReadExactly(handle, offset, data);
        return data;
    }

    private static int ReadUInt16(ReadOnlySpan<byte> bytes, bool littleEndian) =>
        littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static long ReadUInt32(ReadOnlySpan<byte> bytes, bool littleEndian) =>
        littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);

    private static long ReadInt64(ReadOnlySpan<byte> bytes, bool littleEndian) =>
        littleEndian ? BinaryPrimitives.ReadInt64LittleEndian(bytes) : BinaryPrimitives.ReadInt64BigEndian(bytes);

    private static void ReadExactly(SafeFileHandle handle, long offset, Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            int read = RandomAccess.Read(handle, buffer, offset);
            if (read <= 0)
            {
                throw new EndOfStreamException("The TIFF ends before the data its directory points to.");
            }

            offset += read;
            buffer = buffer[read..];
        }
    }

    private void ReadExactly(long offset, Span<byte> buffer) => ReadExactly(this.handle, offset, buffer);

    /// <summary>
    /// A directory entry: its type, how many values, and the inline value field, which holds the values
    /// themselves when they fit and the offset of the values otherwise.
    /// </summary>
    private sealed class TiffEntry(int type, long count, byte[] inline)
    {
        public int Type { get; } = type;

        public long Count { get; } = count;

        public byte[] Inline { get; } = inline;

        public bool LittleEndianOffset { get; init; } = true;
    }

    private sealed class ReadOnlyMemoryStream(byte[] data) : MemoryStream(data, writable: false);
}
