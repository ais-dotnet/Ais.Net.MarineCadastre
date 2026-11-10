using System.Buffers.Binary;

using Ais.Net.MarineCadastre.TestSupport;
using Ais.Net.MarineCadastre.TransitCounts;
using Ais.Net.Models.Abstractions;

using Shouldly;

namespace Ais.Net.MarineCadastre.Tests.GeoTiff;

/// <summary>
/// Directory entries with impossible values, written by patching a valid little-endian BigTIFF. Each
/// one must be reported as <see cref="InvalidDataException"/> or <see cref="NotSupportedException"/>,
/// not as whatever the arithmetic or the collection access would otherwise throw.
/// </summary>
[TestClass]
public class GeoTiffCorruptionTests
{
    private const int Width = 40;
    private const int Height = 24;
    private const int TagImageWidth = 256;
    private const int TagTileWidth = 322;
    private const int TagTileLength = 323;
    private const int TagTileOffsets = 324;
    private const int TagTileByteCounts = 325;
    private const int TagGeoKeyDirectory = 34735;

    [TestMethod]
    public void ATagWithNoValueIsRejected()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        string path = builder.Write();
        PatchEntry(path, TagImageWidth, (file, entry) => WriteInt64(file, entry + 4, 0));

        Should.Throw<InvalidDataException>(() => MarineCadastreTransitCountReader.Open(path)).Message.ShouldContain("no value");
    }

    [TestMethod]
    public void ATiledDirectoryWithoutTileByteCountsIsRejected()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        string path = builder.Write();
        PatchEntry(path, TagTileByteCounts, (file, entry) => WriteUInt16(file, entry, 65000));

        Should.Throw<InvalidDataException>(() => MarineCadastreTransitCountReader.Open(path)).Message.ShouldContain("TileByteCounts");
    }

    [TestMethod]
    public void AnImageWidthBeyondInt32IsRejected()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        string path = builder.Write();
        PatchEntry(path, TagImageWidth, (file, entry) => WriteUInt32(file, entry + 12, 0x80000000));

        Should.Throw<InvalidDataException>(() => MarineCadastreTransitCountReader.Open(path)).Message.ShouldContain("implausible value");
    }

    [TestMethod]
    public void ATagCountThatWouldOverflowTheByteLengthIsRejected()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        string path = builder.Write();
        PatchEntry(path, TagTileOffsets, (file, entry) => WriteInt64(file, entry + 4, (long.MaxValue / 8) + 1));

        Should.Throw<InvalidDataException>(() => MarineCadastreTransitCountReader.Open(path)).Message.ShouldContain("implausibly large");
    }

    [TestMethod]
    public void TilesTooLargeToDecodeInMemoryAreRejected()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        string path = builder.Write();
        PatchEntry(path, TagTileWidth, (file, entry) => WriteUInt16(file, entry + 12, 65535));
        PatchEntry(path, TagTileLength, (file, entry) => WriteUInt16(file, entry + 12, 65535));

        Should.Throw<NotSupportedException>(() => MarineCadastreTransitCountReader.Open(path)).Message.ShouldContain("too large");
    }

    [TestMethod]
    public void ABlockByteCountAboveTheLimitIsRejected()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        string path = builder.Write();

        // Six 4-byte counts do not fit inline, so the value field holds the offset of the array.
        PatchEntry(path, TagTileByteCounts, (file, entry) => WriteUInt32(file, ReadInt64(file, entry + 12), 0xF0000000));

        Should.Throw<InvalidDataException>(() => MarineCadastreTransitCountReader.Open(path)).Message.ShouldContain("implausible offset or size");
    }

    [TestMethod]
    public void AGeoKeyDirectoryWithoutItsHeaderIsRejected()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        string path = builder.Write();
        PatchEntry(path, TagGeoKeyDirectory, (file, entry) => WriteInt64(file, entry + 4, 2));

        Should.Throw<InvalidDataException>(() => MarineCadastreTransitCountReader.Open(path)).Message.ShouldContain("key directory");
    }

    [TestMethod]
    public void CountAtTreatsANaNCoordinateAsOutsideTheRaster()
    {
        uint[] cells = Cells();
        cells[0] = 5;
        using GeoTiffBuilder builder = new(Width, Height, cells);
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        // A NaN coordinate used to fall through the range test and read the top-left cell.
        raster.CountAt(new Position(double.NaN, double.NaN)).ShouldBe(0u);
        raster.CountAt(new Position(double.NaN, raster.Extent.West + 0.0001)).ShouldBe(0u);
        raster.CountAt(new Position(raster.Extent.North - 0.0001, double.NaN)).ShouldBe(0u);
        raster.CountAt(new Position(raster.Extent.North - 0.0001, raster.Extent.West + 0.0001)).ShouldBe(5u);
    }

    private static uint[] Cells()
    {
        uint[] cells = new uint[Width * Height];
        cells[(3 * Width) + 17] = 42;
        return cells;
    }

    /// <summary>
    /// Finds the entry for <paramref name="tag"/> in the first directory of a little-endian BigTIFF and
    /// passes its file offset to <paramref name="patch"/>. A BigTIFF entry is 20 bytes: tag (2), type (2),
    /// count (8) and the value or its offset (8).
    /// </summary>
    private static void PatchEntry(string path, int tag, Action<FileStream, long> patch)
    {
        using FileStream file = new(path, FileMode.Open, FileAccess.ReadWrite);
        long directory = ReadInt64(file, 8);
        long count = ReadInt64(file, directory);
        for (long i = 0; i < count; i++)
        {
            long entry = directory + 8 + (i * 20);
            if (ReadUInt16(file, entry) == tag)
            {
                patch(file, entry);
                return;
            }
        }

        throw new InvalidOperationException($"The test file has no tag {tag}.");
    }

    private static int ReadUInt16(FileStream file, long offset)
    {
        Span<byte> bytes = stackalloc byte[2];
        file.Position = offset;
        file.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes);
    }

    private static long ReadInt64(FileStream file, long offset)
    {
        Span<byte> bytes = stackalloc byte[8];
        file.Position = offset;
        file.ReadExactly(bytes);
        return BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }

    private static void WriteUInt16(FileStream file, long offset, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        file.Position = offset;
        file.Write(bytes);
    }

    private static void WriteUInt32(FileStream file, long offset, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        file.Position = offset;
        file.Write(bytes);
    }

    private static void WriteInt64(FileStream file, long offset, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        file.Position = offset;
        file.Write(bytes);
    }
}
