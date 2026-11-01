using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using Ais.Net.MarineCadastre.TestSupport;
using Ais.Net.Models.Abstractions;

using Shouldly;

namespace Ais.Net.MarineCadastre.Specs;

[TestClass]
public class MarineCadastreTrackReaderTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 9, 52, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 1, 1, 0, 15, 51, TimeSpan.Zero);

    // Miami, Puget Sound and Boston: well apart so bounding boxes can pick one out.
    private static readonly Position[] Miami = [new(25.79027, -80.18396), new(25.79028, -80.18398)];
    private static readonly Position[] Seattle = [new(47.62895, -122.39129), new(47.63000, -122.39000), new(47.63100, -122.38900)];
    private static readonly Position[] Boston = [new(42.35, -71.05), new(42.36, -71.04)];

    private static readonly BoundingBox PugetSound = new(-122.6, 47.4, -122.2, 47.8);

    [TestMethod]
    public void ReadsAttributesAndPoints()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(255915644, Start, End, Miami)
        {
            VesselName = "SEABOARD GARDENIA",
            Imo = "IMO9673630",
            CallSign = "CQ2102",
            VesselType = 71,
            VesselTypeName = "Cargo - Hazard A (Major)",
            NavigationStatus = 0,
            Length = 170,
            Width = 28,
            Draft = 6.9,
            Cargo = 71,
            Transceiver = "A",
            DurationMinutes = 5,
        });

        VesselTrack track = MarineCadastreTrackReader.Read(package.Path).Single();

        track.Id.ShouldBe(1);
        track.Mmsi.ShouldBe(255915644u);
        track.StartTime.ShouldBe(Start);
        track.EndTime.ShouldBe(End);
        track.Duration.ShouldBe(TimeSpan.FromMinutes(5));
        track.Points.ShouldBe(Miami);
        track.Envelope.ShouldBe(new BoundingBox(-80.18398, 25.79027, -80.18396, 25.79028));
        track.VesselName.ShouldBe("SEABOARD GARDENIA");
        track.ImoNumber.ShouldBe(9673630u);
        track.CallSign.ShouldBe("CQ2102");
        track.ShipType.ShouldBe((ShipType)71);
        track.ShipTypeName.ShouldBe("Cargo - Hazard A (Major)");
        track.NavigationStatus.ShouldBe(NavigationStatus.UnderwayUsingEngine);
        track.Length.ShouldBe(170u);
        track.Width.ShouldBe(28u);
        track.Draught.ShouldBe(6.9);
        track.Cargo.ShouldBe(71u);
        track.IsClassB.ShouldBeFalse();
    }

    [TestMethod]
    public void TrackImplementsTheModelsStaticDataInterfaces()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(123456789, Start, End, Miami) { VesselName = "TURN TWO", CallSign = "ODYSSEY", VesselType = 37, Transceiver = "B" });

        VesselTrack track = MarineCadastreTrackReader.Read(package.Path).Single();

        ((IVesselIdentity)track).Mmsi.ShouldBe(123456789u);
        ((IVesselName)track).VesselName.ShouldBe("TURN TWO");
        ((ICallSign)track).CallSign.ShouldBe("ODYSSEY");
        ((IShipType)track).ShipType.ShouldBe((ShipType)37);
        ((IVesselNavigationStatus)track).NavigationStatus.ShouldBe(NavigationStatus.NotDefined);
        track.IsClassB.ShouldBeTrue();
    }

    [TestMethod]
    public void EmptyColumnsBecomeNullOrNotAvailable()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(368110110, Start, End, Miami) { Transceiver = null });

        VesselTrack track = MarineCadastreTrackReader.Read(package.Path).Single();

        track.VesselName.ShouldBe(string.Empty);
        track.CallSign.ShouldBe(string.Empty);
        track.ImoNumber.ShouldBeNull();
        track.ShipType.ShouldBe(ShipType.NotAvailable);
        track.ShipTypeName.ShouldBe(string.Empty);
        track.NavigationStatus.ShouldBe(NavigationStatus.NotDefined);
        track.Length.ShouldBeNull();
        track.Width.ShouldBeNull();
        track.Draught.ShouldBeNull();
        track.Cargo.ShouldBeNull();
        track.IsClassB.ShouldBeFalse();
    }

    [TestMethod]
    public void DurationFallsBackToTheTimestampsWhenTheColumnIsEmpty()
    {
        using GeoPackageBuilder package = new();
        package.Execute($"""update "{package.TableName}" set duration_minutes = null""");
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Execute($"""update "{package.TableName}" set duration_minutes = null""");

        MarineCadastreTrackReader.Read(package.Path).Single().Duration.ShouldBe(End - Start);
    }

    [TestMethod]
    [DataRow(null, 70, 70)]
    [DataRow(37, 70, 37)]
    [DataRow(152, null, 0)]
    [DataRow(null, null, 0)]
    public void ShipTypeComesFromVesselTypeThenCargoAndMustBeInRange(int? vesselType, int? cargo, int expected)
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami) { VesselType = vesselType, Cargo = cargo });

        MarineCadastreTrackReader.Read(package.Path).Single().ShipType.ShouldBe((ShipType)expected);
    }

    [TestMethod]
    [DataRow("IMO9673630", 9673630u)]
    [DataRow("9673630", 9673630u)]
    [DataRow("imo0000000", null)]
    [DataRow("", null)]
    [DataRow("IMO", null)]
    [DataRow("junk", null)]
    public void ImoNumberIsParsedFromThePrefixedText(string imo, uint? expected)
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami) { Imo = imo });

        MarineCadastreTrackReader.Read(package.Path).Single().ImoNumber.ShouldBe(expected);
    }

    [TestMethod]
    public void TracksComeBackInFileOrder()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(3, Start, End, Miami));
        package.Add(new TrackRow(1, Start, End, Seattle));
        package.Add(new TrackRow(2, Start, End, Boston));

        MarineCadastreTrackReader.Read(package.Path).Select(t => t.Mmsi).ShouldBe([3u, 1u, 2u]);
    }

    [TestMethod]
    public void RowsWithoutGeometryAreSkipped()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami) { NoGeometry = true });
        package.Add(new TrackRow(2, Start, End, Miami));
        package.Add(new TrackRow(3, Start, End, Miami) { Geometry = GeoPackageGeometry.Encode([Miami], empty: true) });

        MarineCadastreTrackReader.Read(package.Path).Select(t => t.Mmsi).ShouldBe([2u]);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void BoundsFilterReturnsTracksWhoseEnvelopeIntersectsWithOrWithoutTheIndex(bool withRtree)
    {
        using GeoPackageBuilder package = new(withRtree: withRtree);
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Add(new TrackRow(2, Start, End, Seattle));
        package.Add(new TrackRow(3, Start, End, Boston));
        package.Add(new TrackRow(4, Start, End, Seattle));

        List<VesselTrack> tracks = [.. MarineCadastreTrackReader.Read(package.Path, new VesselTrackReaderOptions { Bounds = PugetSound })];

        tracks.Select(t => t.Mmsi).ShouldBe([2u, 4u]);
    }

    [TestMethod]
    public void BoundsFilterCountsATrackThatMerelyTouchesTheBox()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, [new(47.8, -122.6), new(48.0, -123.0)]));

        MarineCadastreTrackReader.Read(package.Path, new VesselTrackReaderOptions { Bounds = PugetSound }).Count().ShouldBe(1);
    }

    [TestMethod]
    public void TimeFilterKeepsTracksThatOverlapTheWindow()
    {
        using GeoPackageBuilder package = new();
        DateTimeOffset midnight = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        package.Add(new TrackRow(1, midnight, midnight.AddMinutes(10), Miami));
        package.Add(new TrackRow(2, midnight.AddMinutes(20), midnight.AddMinutes(40), Miami));
        package.Add(new TrackRow(3, midnight.AddMinutes(50), midnight.AddMinutes(60), Miami));
        package.Add(new TrackRow(4, midnight.AddMinutes(70), midnight.AddMinutes(80), Miami));

        VesselTrackReaderOptions window = new() { From = midnight.AddMinutes(30), To = midnight.AddMinutes(50) };

        MarineCadastreTrackReader.Read(package.Path, window).Select(t => t.Mmsi).ShouldBe([2u, 3u]);
        MarineCadastreTrackReader.Read(package.Path, new VesselTrackReaderOptions { From = midnight.AddMinutes(45) }).Select(t => t.Mmsi).ShouldBe([3u, 4u]);
        MarineCadastreTrackReader.Read(package.Path, new VesselTrackReaderOptions { To = midnight.AddMinutes(10) }).Select(t => t.Mmsi).ShouldBe([1u]);
    }

    [TestMethod]
    public void TimeFilterHonoursTheOffsetOfTheRequestedTimes()
    {
        using GeoPackageBuilder package = new();
        DateTimeOffset midnight = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        package.Add(new TrackRow(1, midnight, midnight.AddMinutes(10), Miami));

        // 01:05 at UTC+1 is 00:05 UTC, inside the track.
        DateTimeOffset local = new(2026, 1, 1, 1, 5, 0, TimeSpan.FromHours(1));

        MarineCadastreTrackReader.Read(package.Path, new VesselTrackReaderOptions { From = local, To = local }).Count().ShouldBe(1);
    }

    [TestMethod]
    public void TimestampsStoredAsUnixSecondsOrJulianDaysAreRead()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Add(new TrackRow(2, Start, End, Miami));
        package.Add(new TrackRow(3, Start, End, Miami));
        package.Execute($"""update "{package.TableName}" set start_time = {Start.ToUnixTimeSeconds()}, end_time = {End.ToUnixTimeSeconds()} where OBJECTID = 2""");
        package.Execute($"""update "{package.TableName}" set start_time = julianday('{Start:yyyy-MM-dd HH:mm:ss}'), end_time = julianday('{End:yyyy-MM-dd HH:mm:ss}') where OBJECTID = 3""");

        List<VesselTrack> tracks = [.. MarineCadastreTrackReader.Read(package.Path)];

        tracks.Count.ShouldBe(3);
        foreach (VesselTrack track in tracks)
        {
            track.StartTime.ShouldBe(Start, TimeSpan.FromMilliseconds(1), $"start of {track.Id}");
            track.EndTime.ShouldBe(End, TimeSpan.FromMilliseconds(1), $"end of {track.Id}");
        }
    }

    [TestMethod]
    public void TimestampsInAnUnexpectedTextShapeFallBackToTheFrameworkParser()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Execute($"""update "{package.TableName}" set start_time = '2026-01-01T01:09:52+01:00'""");

        MarineCadastreTrackReader.Read(package.Path).Single().StartTime.ShouldBe(Start);
    }

    [TestMethod]
    public void UnreadableTimestampsAreReportedWithTheRow()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Add(new TrackRow(2, Start, End, Miami));
        package.Execute($"""update "{package.TableName}" set end_time = 'yesterday' where OBJECTID = 2""");

        Should.Throw<InvalidDataException>(() => MarineCadastreTrackReader.Read(package.Path).ToList())
            .Message.ShouldContain("Row 2");
    }

    [TestMethod]
    public void MmsiFilterReturnsOneVessel()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Add(new TrackRow(2, Start, End, Seattle));
        package.Add(new TrackRow(1, Start.AddHours(1), End.AddHours(1), Miami));

        MarineCadastreTrackReader.Read(package.Path, new VesselTrackReaderOptions { Mmsi = 1 }).Select(t => t.Id).ShouldBe([1L, 3L]);
    }

    [TestMethod]
    public void FiltersCombine()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Seattle));
        package.Add(new TrackRow(2, Start, End, Seattle));
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Add(new TrackRow(1, Start.AddDays(1), End.AddDays(1), Seattle));

        VesselTrackReaderOptions options = new() { Mmsi = 1, Bounds = PugetSound, To = End };

        MarineCadastreTrackReader.Read(package.Path, options).Select(t => t.Id).ShouldBe([1L]);
    }

    [TestMethod]
    public void TableIsDiscoveredFromGpkgContentsWhateverItsName()
    {
        using GeoPackageBuilder package = new("AIS_Tracks_2025_12");
        package.Add(new TrackRow(1, Start, End, Miami));

        MarineCadastreTrackReader.Read(package.Path).Single().Mmsi.ShouldBe(1u);
        MarineCadastreTrackReader.Read(package.Path, new VesselTrackReaderOptions { TableName = "AIS_Tracks_2025_12" }).Count().ShouldBe(1);
    }

    [TestMethod]
    public void UnknownTableNameIsReported()
    {
        using GeoPackageBuilder package = new();

        Should.Throw<InvalidDataException>(() => MarineCadastreTrackReader.Read(package.Path, new VesselTrackReaderOptions { TableName = "Nope" }).ToList())
            .Message.ShouldContain("Nope");
    }

    [TestMethod]
    public void MissingMmsiColumnIsReported()
    {
        using GeoPackageBuilder package = new();
        package.Execute($"""alter table "{package.TableName}" drop column mmsi""");

        Should.Throw<InvalidDataException>(() => MarineCadastreTrackReader.Read(package.Path).ToList())
            .Message.ShouldContain("mmsi");
    }

    [TestMethod]
    public void MissingFileIsReportedOnFirstRead()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.gpkg");

        IEnumerable<VesselTrack> tracks = MarineCadastreTrackReader.Read(path);

        Should.Throw<FileNotFoundException>(() => tracks.ToList());
    }

    [TestMethod]
    public async Task ObserveEmitsEveryTrackThenCompletes()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Add(new TrackRow(2, Start, End, Seattle));
        package.Add(new TrackRow(3, Start, End, Boston));

        IList<VesselTrack> tracks = await MarineCadastreTrackReader.Observe(package.Path).ToList().ToTask();

        tracks.Select(t => t.Mmsi).ShouldBe([1u, 2u, 3u]);
        tracks[1].Points.ShouldBe(Seattle);
    }

    [TestMethod]
    public async Task ObserveAppliesTheSameFilters()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Add(new TrackRow(2, Start, End, Seattle));

        IList<VesselTrack> tracks = await MarineCadastreTrackReader.Observe(package.Path, new VesselTrackReaderOptions { Bounds = PugetSound }).ToList().ToTask();

        tracks.Select(t => t.Mmsi).ShouldBe([2u]);
    }

    [TestMethod]
    public async Task ObserveIsColdSoEachSubscriptionReadsTheFile()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami));

        IObservable<VesselTrack> tracks = MarineCadastreTrackReader.Observe(package.Path);

        (await tracks.Count().ToTask()).ShouldBe(1);
        (await tracks.Count().ToTask()).ShouldBe(1);
    }

    [TestMethod]
    public void ObserveStopsReadingWhenTheSubscriptionIsDisposed()
    {
        using GeoPackageBuilder package = new();
        for (uint mmsi = 1; mmsi <= 20; mmsi++)
        {
            package.Add(new TrackRow(mmsi, Start, End, Miami));
        }

        using ManualResetEventSlim firstDelivered = new();
        using ManualResetEventSlim releaseFirst = new();
        int delivered = 0;
        bool completed = false;

        IDisposable subscription = MarineCadastreTrackReader.Observe(package.Path).Subscribe(
            _ =>
            {
                Interlocked.Increment(ref delivered);
                firstDelivered.Set();
                releaseFirst.Wait();
            },
            _ => { },
            () => completed = true);

        firstDelivered.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
        subscription.Dispose();
        releaseFirst.Set();
        Thread.Sleep(200);

        delivered.ShouldBe(1);
        completed.ShouldBeFalse();
    }

    [TestMethod]
    public async Task ObserveReportsAMissingFileAsAnError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.gpkg");

        await Should.ThrowAsync<FileNotFoundException>(() => MarineCadastreTrackReader.Observe(path).ToList().ToTask());
    }

    [TestMethod]
    public async Task ObserveReportsABadFileAsAnError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.gpkg");
        await File.WriteAllTextAsync(path, "this is not a GeoPackage");
        try
        {
            await Should.ThrowAsync<Exception>(() => MarineCadastreTrackReader.Observe(path).ToList().ToTask());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void BlankPathIsRejectedEagerly()
    {
        Should.Throw<ArgumentException>(() => MarineCadastreTrackReader.Read(" "));
        Should.Throw<ArgumentException>(() => MarineCadastreTrackReader.Observe(string.Empty));
    }
}
