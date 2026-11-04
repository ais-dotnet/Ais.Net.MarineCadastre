using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using Ais.Net.MarineCadastre.TransitCounts;
using Ais.Net.MarineCadastre.TestSupport;
using Ais.Net.Models.Abstractions;

using Shouldly;

namespace Ais.Net.MarineCadastre.Tests.TransitCounts;

[TestClass]
public class TransitCountRasterTests
{
    private const double EarthRadius = 6378137.0;
    private const int Width = 40;
    private const int Height = 24;

    // A 40 x 24 grid in 16-cell tiles: three tiles across (the last 8 wide), two down (the last 8 high).
    // Tile (1,1) holds nothing, so one tile of six is empty.
    private static readonly (int Column, int Row, uint Value)[] Data =
    [
        (0, 0, 5),
        (17, 3, 42),
        (18, 3, 43),
        (39, 23, 7),
        (2, 20, 1),
    ];

    private static uint[] Cells()
    {
        uint[] cells = new uint[Width * Height];
        foreach ((int column, int row, uint value) in Data)
        {
            cells[(row * Width) + column] = value;
        }

        return cells;
    }

    private static Position CellCentre(GeoTiffBuilder builder, double column, double row, int levelFactor = 1)
    {
        double size = builder.CellSize * levelFactor;
        double x = builder.OriginX + ((column + 0.5) * size);
        double y = builder.OriginY - ((row + 0.5) * size);
        return builder.Geographic ? new Position(y, x) : new Position(Math.Atan(Math.Sinh(y / EarthRadius)) * 180 / Math.PI, x / EarthRadius * 180 / Math.PI);
    }

    [TestMethod]
    public void OpensAndDescribesTheRaster()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        raster.Path.ShouldBe(builder.Path);
        raster.Dimensions().ShouldBe((Width, Height));
        raster.LevelCount.ShouldBe(1);
        raster.IsProjected.ShouldBeTrue();
        raster.CellSize().ShouldBe((100.0, 100.0));
        raster.NoData.ShouldBe(0u);
        raster.Statistics.ShouldBeNull();

        Position northWest = CellCentre(builder, -0.5, -0.5);
        Position southEast = CellCentre(builder, Width - 0.5, Height - 0.5);
        raster.Extent.West.ShouldBe(northWest.Longitude, 1e-9);
        raster.Extent.North.ShouldBe(northWest.Latitude, 1e-9);
        raster.Extent.East.ShouldBe(southEast.Longitude, 1e-9);
        raster.Extent.South.ShouldBe(southEast.Latitude, 1e-9);
    }

    [TestMethod]
    public void ReadReturnsTheCellsWithDataInTileOrder()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        List<TransitCell> cells = [.. raster.Read()];

        cells.Select(c => (c.Column, c.Row, c.Transits)).ShouldBe([(0, 0, 5u), (17, 3, 42u), (18, 3, 43u), (2, 20, 1u), (39, 23, 7u)]);
        cells[1].Centre.Latitude.ShouldBe(CellCentre(builder, 17, 3).Latitude, 1e-9);
        cells[1].Centre.Longitude.ShouldBe(CellCentre(builder, 17, 3).Longitude, 1e-9);
    }

    [TestMethod]
    public void CountAtReadsTheCellUnderAPosition()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        raster.CountAt(CellCentre(builder, 17, 3)).ShouldBe(42u);
        raster.CountAt(CellCentre(builder, 18, 3)).ShouldBe(43u);
        raster.CountAt(CellCentre(builder, 39, 23)).ShouldBe(7u);
        raster.CountAt(CellCentre(builder, 20, 20)).ShouldBe(0u);
        raster.CountAt(CellCentre(builder, -1, 3)).ShouldBe(0u);
        raster.CountAt(CellCentre(builder, 3, Height + 2)).ShouldBe(0u);
        raster.CountAt(new Position(0, 0)).ShouldBe(0u);
    }

    [TestMethod]
    public void BoundsFilterKeepsCellsWhoseCentreIsInsideTheBox()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());
        Position a = CellCentre(builder, 16.6, 2.6);
        Position b = CellCentre(builder, 17.6, 3.6);
        BoundingBox box = new(a.Longitude, b.Latitude, b.Longitude, a.Latitude);

        raster.Read(new TransitCountReaderOptions { Bounds = box }).Select(c => c.Transits).ShouldBe([42u]);
        raster.ReadTiles(new TransitCountReaderOptions { Bounds = box }).Select(t => (t.Column, t.Row)).ShouldBe([(1, 0)]);
    }

    [TestMethod]
    public void BoundsOutsideTheRasterReturnNothing()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        raster.Read(new TransitCountReaderOptions { Bounds = new BoundingBox(0, 0, 1, 1) }).ShouldBeEmpty();
    }

    [TestMethod]
    public void MinimumTransitsFiltersCells()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        raster.Read(new TransitCountReaderOptions { MinimumTransits = 42 }).Select(c => c.Transits).ShouldBe([42u, 43u]);
    }

    [TestMethod]
    public void TilesSkipEmptyOnesAndClipAtTheEdges()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        List<TransitTile> tiles = [.. raster.ReadTiles()];

        tiles.Select(t => (t.Column, t.Row, t.Width, t.Height)).ShouldBe([(0, 0, 16, 16), (1, 0, 16, 16), (0, 1, 16, 8), (2, 1, 8, 8)]);
        TransitTile edge = tiles[3];
        edge.Level.ShouldBe(0);
        edge.FirstCellColumn.ShouldBe(32);
        edge.FirstCellRow.ShouldBe(16);
        edge.Counts.Length.ShouldBe(64);
        edge[7, 7].ShouldBe(7u);
        edge.Bounds.East.ShouldBe(raster.Extent.East, 1e-9);
        edge.Bounds.South.ShouldBe(raster.Extent.South, 1e-9);
        tiles[1][1, 3].ShouldBe(42u);
        tiles[1].Counts.ToArray().Sum(c => c).ShouldBe(85u);
    }

    [TestMethod]
    [DataRow(TiffCompression.None, true, false, 32, 1)]
    [DataRow(TiffCompression.Lzw, true, false, 32, 1)]
    [DataRow(TiffCompression.Deflate, true, false, 32, 1)]
    [DataRow(TiffCompression.Lzw, false, false, 32, 1)]
    [DataRow(TiffCompression.Lzw, true, true, 32, 1)]
    [DataRow(TiffCompression.Deflate, false, true, 32, 1)]
    [DataRow(TiffCompression.Lzw, true, false, 16, 1)]
    [DataRow(TiffCompression.Lzw, true, false, 8, 1)]
    [DataRow(TiffCompression.Lzw, true, false, 32, 2)]
    [DataRow(TiffCompression.Deflate, true, true, 16, 2)]
    public void ReadsEveryLayoutVariant(TiffCompression compression, bool bigTiff, bool bigEndian, int bitsPerSample, int predictor)
    {
        uint[] cells = Cells();
        if (bitsPerSample == 8)
        {
            cells = [.. cells.Select(c => c % 256)];
        }

        using GeoTiffBuilder builder = new(Width, Height, cells) { Compression = compression, BigTiff = bigTiff, BigEndian = bigEndian, BitsPerSample = bitsPerSample, Predictor = predictor };
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        raster.Read().Select(c => (c.Column, c.Row, c.Transits)).ShouldBe(
            Data.Where(d => cells[(d.Row * Width) + d.Column] != 0).OrderBy(d => d.Row / 16).ThenBy(d => d.Column / 16).ThenBy(d => d.Row).ThenBy(d => d.Column).Select(d => (d.Column, d.Row, cells[(d.Row * Width) + d.Column])));
    }

    [TestMethod]
    [DataRow(4)]
    [DataRow(24)]
    public void ReadsStrippedFiles(int rowsPerStrip)
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells()) { TileSize = 0, RowsPerStrip = rowsPerStrip };
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        raster.Read().Select(c => (c.Column, c.Row, c.Transits)).ShouldBe([(0, 0, 5u), (17, 3, 42u), (18, 3, 43u), (2, 20, 1u), (39, 23, 7u)]);
        raster.CountAt(CellCentre(builder, 39, 23)).ShouldBe(7u);
        raster.ReadTiles().Select(t => t.Height).ShouldAllBe(h => h == Math.Min(rowsPerStrip, Height));
    }

    [TestMethod]
    public void OverviewsAreFurtherLevels()
    {
        uint[] overview = new uint[20 * 12];
        overview[(1 * 20) + 8] = 42;
        using GeoTiffBuilder builder = new GeoTiffBuilder(Width, Height, Cells()).WithOverview(20, 12, overview);
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        raster.LevelCount.ShouldBe(2);
        raster.Dimensions(1).ShouldBe((20, 12));
        raster.CellSize(1).ShouldBe((200.0, 200.0));
        raster.CountAt(CellCentre(builder, 8, 1, levelFactor: 2), level: 1).ShouldBe(42u);
        raster.CountAt(CellCentre(builder, 17, 3), level: 1).ShouldBe(42u);
        raster.Read(new TransitCountReaderOptions { Level = 1 }).Select(c => (c.Column, c.Row, c.Transits)).ShouldBe([(8, 1, 42u)]);
        raster.ReadTiles(new TransitCountReaderOptions { Level = 1 }).Single().Level.ShouldBe(1);
        Should.Throw<ArgumentOutOfRangeException>(() => raster.CountAt(new Position(0, 0), level: 2));
        Should.Throw<ArgumentOutOfRangeException>(() => raster.Read(new TransitCountReaderOptions { Level = -1 }).ToList());
    }

    [TestMethod]
    public void GeographicRastersUseDegreesDirectly()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells()) { Geographic = true, OriginX = -122.5, OriginY = 47.7, CellSize = 0.01 };
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        raster.IsProjected.ShouldBeFalse();
        raster.Extent.ShouldBe(new BoundingBox(-122.5, 47.7 - (Height * 0.01), -122.5 + (Width * 0.01), 47.7));
        raster.CountAt(new Position(47.7 - 0.035, -122.5 + 0.175)).ShouldBe(42u);
        raster.Read().Skip(1).First().Centre.ShouldBe(new Position(47.7 - 0.035, -122.5 + 0.175));
    }

    [TestMethod]
    public void UserDefinedWebMercatorIsRecognisedFromItsCitation()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells()) { UserDefinedWebMercator = true };
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        raster.IsProjected.ShouldBeTrue();
        raster.CountAt(CellCentre(builder, 17, 3)).ShouldBe(42u);
    }

    [TestMethod]
    public void PixelIsPointRastersAreShiftedHalfACell()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells()) { PixelIsPoint = true };
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        raster.CountAt(CellCentre(builder, 17, 3)).ShouldBe(42u);
        raster.Extent.ShouldBe(MarineCadastreTransitCountReader.Open(new GeoTiffBuilder(Width, Height, Cells()).Write()).Extent);
    }

    [TestMethod]
    public void NonZeroNoDataIsHonoured()
    {
        uint[] cells = [.. Cells().Select(c => c == 0 ? uint.MaxValue : c)];
        using GeoTiffBuilder builder = new(Width, Height, cells) { NoData = uint.MaxValue };
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        raster.NoData.ShouldBe(uint.MaxValue);
        raster.Read().Count().ShouldBe(Data.Length);
        raster.Read(new TransitCountReaderOptions { MinimumTransits = 0 }).Count().ShouldBe(Data.Length);
        raster.CountAt(CellCentre(builder, 20, 20)).ShouldBe(0u);
        raster.ReadTiles().SelectMany(t => t.Counts.ToArray()).ShouldNotContain(uint.MaxValue);
    }

    [TestMethod]
    public void WithoutNoDataZeroCellsAreStillNotTransits()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells()) { NoData = null };
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        raster.NoData.ShouldBeNull();
        raster.Read(new TransitCountReaderOptions { MinimumTransits = 0 }).Count().ShouldBe(Data.Length);
    }

    [TestMethod]
    public void StatisticsComeFromGdalMetadata()
    {
        const string metadata = """
            <GDALMetadata>
              <Item name="STATISTICS_COUNT" sample="0">371149894.000000</Item>
              <Item name="STATISTICS_MAXIMUM" sample="0">129910</Item>
              <Item name="STATISTICS_MEAN" sample="0">10.152070055555</Item>
              <Item name="STATISTICS_MINIMUM" sample="0">1</Item>
              <Item name="STATISTICS_STDDEV" sample="0">173.90808128107</Item>
            </GDALMetadata>
            """;
        using GeoTiffBuilder builder = new(Width, Height, Cells()) { GdalMetadata = metadata };
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        raster.Statistics.ShouldBe(new TransitCountStatistics(371149894, 1, 129910, 10.152070055555, 173.90808128107));
    }

    [TestMethod]
    public void UnsupportedProjectionsAndMissingGeoKeysAreReported()
    {
        using GeoTiffBuilder albers = new(Width, Height, Cells()) { UnsupportedProjection = true };
        Should.Throw<NotSupportedException>(() => MarineCadastreTransitCountReader.Open(albers.Write())).Message.ShouldContain("5070");

        using GeoTiffBuilder plain = new(Width, Height, Cells()) { OmitGeoKeys = true };
        Should.Throw<InvalidDataException>(() => MarineCadastreTransitCountReader.Open(plain.Write()));
    }

    [TestMethod]
    public void FilesThatAreNotTiffsAreReported()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tif");
        File.WriteAllText(path, "GP, not a TIFF at all");
        try
        {
            Should.Throw<InvalidDataException>(() => MarineCadastreTransitCountReader.Open(path));
        }
        finally
        {
            File.Delete(path);
        }

        Should.Throw<FileNotFoundException>(() => MarineCadastreTransitCountReader.Open(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tif")));
        Should.Throw<ArgumentException>(() => MarineCadastreTransitCountReader.Open(" "));
    }

    [TestMethod]
    public async Task ObserveEmitsTheSameCellsAsReadThenCompletes()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        IList<TransitCell> observed = await raster.Observe().ToList().ToTask();
        IList<TransitTile> tiles = await raster.ObserveTiles().ToList().ToTask();

        observed.ShouldBe(raster.Read());
        tiles.Select(t => (t.Column, t.Row)).ShouldBe(raster.ReadTiles().Select(t => (t.Column, t.Row)));
    }

    [TestMethod]
    public async Task StaticObserveOpensAndClosesTheFilePerSubscription()
    {
        using GeoTiffBuilder builder = new(Width, Height, Cells());
        string path = builder.Write();

        IObservable<TransitCell> cells = MarineCadastreTransitCountReader.Observe(path, new TransitCountReaderOptions { MinimumTransits = 40 });

        (await cells.Count().ToTask()).ShouldBe(2);
        (await cells.Count().ToTask()).ShouldBe(2);
        (await MarineCadastreTransitCountReader.ObserveTiles(path).Count().ToTask()).ShouldBe(4);

        // Completion reaches the awaiter a moment before the subscription's disposal closes the file.
        bool deleted = false;
        for (int attempt = 0; attempt < 50 && !deleted; attempt++)
        {
            try
            {
                File.Delete(path);
                deleted = true;
            }
            catch (IOException)
            {
                await Task.Delay(20);
            }
        }

        deleted.ShouldBeTrue("the file should be closed once every subscription has completed");
    }

    [TestMethod]
    public async Task ObserveReportsAMissingFileAsAnError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tif");

        await Should.ThrowAsync<FileNotFoundException>(() => MarineCadastreTransitCountReader.Observe(path).ToList().ToTask());
    }

    [TestMethod]
    public void LargeSparseRastersRoundTripThroughLzwTiles()
    {
        const int size = 1200;
        uint[] cells = new uint[size * size];
        Random random = new(42);
        long expectedSum = 0;
        int expectedCount = 0;
        for (int i = 0; i < 30_000; i++)
        {
            int index = random.Next(cells.Length);
            if (cells[index] == 0)
            {
                expectedCount++;
            }
            else
            {
                expectedSum -= cells[index];
            }

            cells[index] = (uint)random.Next(1, 200_000);
            expectedSum += cells[index];
        }

        using GeoTiffBuilder builder = new(size, size, cells) { TileSize = 512 };
        using TransitCountRaster raster = MarineCadastreTransitCountReader.Open(builder.Write());

        long sum = 0;
        int count = 0;
        foreach (TransitCell cell in raster.Read())
        {
            cells[(cell.Row * size) + cell.Column].ShouldBe(cell.Transits);
            sum += cell.Transits;
            count++;
        }

        count.ShouldBe(expectedCount);
        sum.ShouldBe(expectedSum);
    }
}
