using System.Reactive.Linq;

using Ais.Net.MarineCadastre.TestSupport;
using Ais.Net.Models.Abstractions;

using BenchmarkDotNet.Attributes;

namespace Ais.Net.MarineCadastre.Benchmarks;

/// <summary>
/// Throughput and allocation of reading vessel tracks from a GeoPackage: the pull reader, the
/// observable, and a bounding-box query that exercises the R-tree. By default the input is a generated
/// file with the shape of a real month (about 183 vertices per track, a quarter of them inside the
/// queried box). Set <see cref="FileVariable"/> to a real monthly file to measure that instead; a real
/// file holds over a million tracks, so run with <c>--job short</c>.
/// </summary>
[MemoryDiagnoser]
public class VesselTrackBenchmarks
{
    public const string FileVariable = "AIS_NET_MARINECADASTRE_GPKG";

    // Puget Sound, which about a quarter of the generated tracks touch.
    private static readonly VesselTrackReaderOptions InBox = new() { Bounds = new BoundingBox(-122.6, 47.4, -122.2, 47.8) };

    private GeoPackageBuilder? generated;
    private string path = string.Empty;

    [Params(10_000)]
    public int TrackCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        string? real = Environment.GetEnvironmentVariable(FileVariable);
        if (!string.IsNullOrEmpty(real) && File.Exists(real))
        {
            this.path = real;
            return;
        }

        this.generated = new GeoPackageBuilder();
        this.generated.AddRange(GenerateTracks(this.TrackCount));
        this.path = this.generated.Path;
    }

    [GlobalCleanup]
    public void Cleanup() => this.generated?.Dispose();

    [Benchmark(Baseline = true)]
    public long ReadAllTracks()
    {
        long points = 0;
        foreach (VesselTrack track in MarineCadastreTrackReader.Read(this.path))
        {
            points += track.Points.Count;
        }

        return points;
    }

    [Benchmark]
    public long ObserveAllTracks() =>
        MarineCadastreTrackReader.Observe(this.path).Aggregate(0L, (points, track) => points + track.Points.Count).Wait();

    [Benchmark]
    public long ReadTracksInBoundingBox()
    {
        long points = 0;
        foreach (VesselTrack track in MarineCadastreTrackReader.Read(this.path, InBox))
        {
            points += track.Points.Count;
        }

        return points;
    }

    private static IEnumerable<TrackRow> GenerateTracks(int count)
    {
        Random random = new(20260101);
        DateTimeOffset start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Four home areas: Puget Sound, Miami, Boston, Houston.
        (double Latitude, double Longitude)[] areas = [(47.6, -122.4), (25.8, -80.2), (42.35, -71.05), (29.7, -95.0)];

        for (int i = 0; i < count; i++)
        {
            (double latitude, double longitude) = areas[i % areas.Length];
            int vertices = 2 + (int)(random.NextDouble() * random.NextDouble() * 1000);
            Position[] points = new Position[vertices];
            for (int v = 0; v < vertices; v++)
            {
                latitude += (random.NextDouble() - 0.5) * 0.002;
                longitude += (random.NextDouble() - 0.5) * 0.002;
                points[v] = new Position(latitude, longitude);
            }

            DateTimeOffset trackStart = start.AddMinutes(random.Next(0, 31 * 24 * 60));
            bool classB = i % 3 == 0;
            yield return new TrackRow((uint)(300_000_000 + (i % 35_000)), trackStart, trackStart.AddMinutes(vertices), points)
            {
                VesselName = $"VESSEL {i % 35_000}",
                CallSign = $"WD{i % 9000:D4}",
                Imo = classB ? null : $"IMO{9000000 + i}",
                VesselType = classB ? 37 : 70,
                VesselTypeName = classB ? "Pleasure Craft" : "Cargo",
                NavigationStatus = classB ? null : 0,
                Length = classB ? 12 : 180,
                Width = classB ? 4 : 30,
                Draft = classB ? null : 9.5,
                Cargo = classB ? null : 70,
                Transceiver = classB ? "B" : "A",
            };
        }
    }
}
