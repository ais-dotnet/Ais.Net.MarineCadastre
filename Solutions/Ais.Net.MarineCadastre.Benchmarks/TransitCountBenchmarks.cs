using System.Reactive.Linq;
using Ais.Net.MarineCadastre.TestSupport;
using Ais.Net.MarineCadastre.TransitCounts;
using Ais.Net.Models.Abstractions;
using BenchmarkDotNet.Attributes;

namespace Ais.Net.MarineCadastre.Benchmarks;

/// <summary>
/// Throughput and allocation of reading a transit-count GeoTIFF: every cell with data as a pull
/// sequence and as an observable, the same as tiles, and point lookups. The input is a generated
/// 2048 × 2048 raster in 512-cell LZW tiles with the real file's density (about 2% of cells hold data,
/// clustered so that a quarter of the tiles are empty). Set <see cref="FileVariable"/> to a real file to
/// measure that instead; the 2025 file has 7,643 non-empty tiles, so run with <c>--job short</c>.
/// </summary>
[MemoryDiagnoser]
public class TransitCountBenchmarks
{
    public const string FileVariable = "AIS_NET_MARINECADASTRE_TIF";

    private const int Size = 2048;

    private GeoTiffBuilder? generated;
    private TransitCountRaster? raster;
    private Position[] lookups = [];

    [GlobalSetup]
    public void Setup()
    {
        string? real = Environment.GetEnvironmentVariable(FileVariable);
        string path;
        if (!string.IsNullOrEmpty(real) && File.Exists(real))
        {
            path = real;
        }
        else
        {
            this.generated = new GeoTiffBuilder(Size, Size, GenerateCells()) { TileSize = 512 };
            path = this.generated.Write();
        }

        this.raster = MarineCadastreTransitCountReader.Open(path);

        // A thousand positions spread over the raster, so lookups mostly change tile.
        Random random = new(1);
        BoundingBox extent = this.raster.Extent;
        this.lookups = [.. Enumerable.Range(0, 1000).Select(_ => new Position(
            extent.South + (random.NextDouble() * (extent.North - extent.South)),
            extent.West + (random.NextDouble() * (extent.East - extent.West))))];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.raster?.Dispose();
        this.generated?.Dispose();
    }

    [Benchmark(Baseline = true)]
    public long ReadAllCells()
    {
        long transits = 0;
        foreach (TransitCell cell in this.raster!.Read())
        {
            transits += cell.Transits;
        }

        return transits;
    }

    [Benchmark]
    public long ObserveAllCells() =>
        this.raster!.Observe().Aggregate(0L, (transits, cell) => transits + cell.Transits).Wait();

    [Benchmark]
    public long ReadAllTiles()
    {
        long cells = 0;
        foreach (TransitTile tile in this.raster!.ReadTiles())
        {
            cells += tile.Counts.Length;
        }

        return cells;
    }

    [Benchmark]
    public long ThousandPointLookups()
    {
        long transits = 0;
        foreach (Position position in this.lookups)
        {
            transits += this.raster!.CountAt(position);
        }

        return transits;
    }

    private static uint[] GenerateCells()
    {
        uint[] cells = new uint[Size * Size];
        Random random = new(20250101);

        // Twelve shipping lanes drawn as noisy lines across the lower-right three quarters of the grid.
        for (int lane = 0; lane < 12; lane++)
        {
            double x = random.Next(Size / 4, Size);
            double y = random.Next(Size / 4, Size);
            double dx = random.NextDouble() - 0.5;
            double dy = random.NextDouble() - 0.5;
            for (int step = 0; step < 20_000; step++)
            {
                x += dx * 2;
                y += dy * 2;
                int column = (int)x + random.Next(-3, 4);
                int row = (int)y + random.Next(-3, 4);
                if (column < Size / 4 || row < Size / 4 || column >= Size || row >= Size)
                {
                    break;
                }

                cells[(row * Size) + column] += (uint)random.Next(1, 50);
            }
        }

        return cells;
    }
}
