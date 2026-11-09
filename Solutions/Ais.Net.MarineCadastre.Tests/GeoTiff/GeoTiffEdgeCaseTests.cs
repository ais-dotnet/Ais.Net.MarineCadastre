using Ais.Net.MarineCadastre.TestSupport;
using Ais.Net.MarineCadastre.TransitCounts;
using Ais.Net.Models.Abstractions;

using Shouldly;

namespace Ais.Net.MarineCadastre.Tests.GeoTiff;

/// <summary>
/// GeoTIFF layouts and faults the main raster tests do not reach: matrix georeferencing, sparse
/// blocks, unsupported encodings, truncated and malformed files, and metadata that is not statistics.
/// </summary>
[TestClass]
public class GeoTiffEdgeCaseTests
{
    private const double EarthRadius = 6378137.0;
    private const int Width = 40;
    private const int Height = 24;

    private static uint[] Cells()
    {
        uint[] cells = new uint[Width * Height];
        cells[(3 * Width) + 17] = 42;
        cells[(23 * Width) + 39] = 7;
        return cells;
    }

    private static Position CellCentre(GeoTiffBuilder builder, double column, double row)
    {
        double x = builder.OriginX + ((column + 0.5) * builder.CellSize);
        double y = builder.OriginY - ((row + 0.5) * builder.CellSize);
        return new Position(Math.Atan(Math.Sinh(y / EarthRadius)) * 180 / Math.PI, x / EarthRadius * 180 / Math.PI);
    }

    [TestMethod]
    public void ModelTransformationGeoreferencingIsRead()
    {
        using GeoTiffBuilder tiepoint = new(Width, Height, Cells());
        using GeoTiffBuilder matrix = new(Width, Height, Cells()) { UseModelTransformation = true };
        using TransitCountRaster expected = MarineCadastreTransitCountReader.Open(tiepoint.Write());
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(matrix.Write());

        raster.Extent.ShouldBe(expected.Extent);
        raster.CellSize().ShouldBe((100.0, 100.0));
        raster.CountAt(CellCentre(matrix, 17, 3)).ShouldBe(42u);
    }

    [TestMethod]
    public void RotatedTransformationsAreRejected()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells()) { UseModelTransformation = true, RotatedTransformation = true };

        Should.Throw<NotSupportedException>(() => MarineCadastreTransitCountReader.Open(builder.Write())).Message.ShouldContain("north-up");
    }

    [TestMethod]
    public void ATiepointWithoutAScaleIsRejected()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells()) { OmitPixelScale = true };

        Should.Throw<InvalidDataException>(() => MarineCadastreTransitCountReader.Open(builder.Write())).Message.ShouldContain("tiepoint");
    }

    [TestMethod]
    public void WorldWideWebMercatorExtentsClampToTheAntimeridian()
    {
        const double HalfWorld = 20037508.342789244;
        using GeoTiffBuilder builder = new(Width, Height, Cells()) { OriginX = -HalfWorld, CellSize = 2 * HalfWorld / Width };

        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        raster.Extent.West.ShouldBe(-180, 1e-9);
        raster.Extent.East.ShouldBe(180, 1e-9);
        List<TransitTile> tiles = [.. raster.ReadTiles()];
        tiles.ShouldNotBeEmpty();
        tiles.ShouldAllBe(t => t.Bounds.West >= -180 && t.Bounds.East <= 180);
    }

    [TestMethod]
    public void NonFiniteGeoreferencingIsRejected()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells()) { OriginX = double.NaN };

        Should.Throw<InvalidDataException>(() => MarineCadastreTransitCountReader.Open(builder.Write())).Message.ShouldContain("NaN");
    }

    [TestMethod]
    public void SparseBlocksReadAsNoData()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells()) { SparseEmptyBlocks = true };
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        raster.Read().Select(c => (c.Column, c.Row, c.Transits)).ShouldBe([(17, 3, 42u), (39, 23, 7u)]);
        raster.ReadTiles().Select(t => (t.Column, t.Row)).ShouldBe([(1, 0), (2, 1)]);
        raster.CountAt(CellCentre(builder, 2, 2)).ShouldBe(0u);
        raster.CountAt(CellCentre(builder, 17, 3)).ShouldBe(42u);
    }

    [TestMethod]
    public void UnsupportedCompressionIsReportedWhenATileIsRead()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells()) { DeclaredCompression = 7 };
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        Should.Throw<NotSupportedException>(() => raster.Read().ToList()).Message.ShouldContain("compression 7");
    }

    [TestMethod]
    public void UnsupportedPredictorsAreRejected()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells()) { Predictor = 3 };

        Should.Throw<NotSupportedException>(() => MarineCadastreTransitCountReader.Open(builder.Write())).Message.ShouldContain("predictor 3");
    }

    [TestMethod]
    public void MultiBandRastersAreRejected()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells()) { SamplesPerPixel = 3 };

        Should.Throw<NotSupportedException>(() => MarineCadastreTransitCountReader.Open(builder.Write())).Message.ShouldContain("single-band");
    }

    [TestMethod]
    public void UnsupportedSampleTypesAreRejected()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells()) { BitsPerSample = 64 };

        Should.Throw<NotSupportedException>(() => MarineCadastreTransitCountReader.Open(builder.Write())).Message.ShouldContain("64-bit");
    }

    [TestMethod]
    public void ATruncatedFileIsReported()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        string path = builder.Write();
        using (FileStream stream = new(path, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(16);
        }

        Should.Throw<EndOfStreamException>(() => MarineCadastreTransitCountReader.Open(path));
    }

    [TestMethod]
    public void UnknownTiffVersionsAreRejected()
    {
        string path = WriteBytes([0x49, 0x49, 44, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        try
        {
            Should.Throw<InvalidDataException>(() => MarineCadastreTransitCountReader.Open(path)).Message.ShouldContain("version 44");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AFileWithNoDirectoryIsRejected()
    {
        string path = WriteBytes([0x4D, 0x4D, 0, 42, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        try
        {
            Should.Throw<InvalidDataException>(() => MarineCadastreTransitCountReader.Open(path)).Message.ShouldContain("no image directory");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ADirectoryWithAnAbsurdEntryCountIsRejected()
    {
        // Classic TIFF, first directory at offset 8, claiming 65535 entries.
        string path = WriteBytes([0x49, 0x49, 42, 0, 8, 0, 0, 0, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0]);
        try
        {
            Should.Throw<InvalidDataException>(() => MarineCadastreTransitCountReader.Open(path)).Message.ShouldContain("entry count");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void MetadataWithoutStatisticsYieldsNone()
    {
        TransitCountStatistics.Parse(null).ShouldBeNull();
        TransitCountStatistics.Parse("<GDALMetadata><Item name=\"DataType\">Generic</Item></GDALMetadata>").ShouldBeNull();
        TransitCountStatistics.Parse("<GDALMetadata><Item name=\"STATISTICS_MINIMUM\">1</Item></GDALMetadata>").ShouldBeNull();
        TransitCountStatistics.Parse("<GDALMetadata><Item name=\"STATISTICS_MINIMUM\">1</Item>").ShouldBeNull();
    }

    [TestMethod]
    public void StatisticsWithoutACountOrMeanAreStillParsed()
    {
        TransitCountStatistics? statistics = TransitCountStatistics.Parse("<GDALMetadata><Item name=\"STATISTICS_MINIMUM\">1</Item><Item name=\"STATISTICS_MAXIMUM\">9</Item></GDALMetadata>");

        statistics.ShouldNotBeNull();
        statistics.Count.ShouldBeNull();
        statistics.Minimum.ShouldBe(1);
        statistics.Maximum.ShouldBe(9);
        statistics.Mean.ShouldBeNull();
        statistics.StandardDeviation.ShouldBeNull();
    }

    private static string WriteBytes(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ais-net-tiff-{Guid.NewGuid():N}.tif");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
