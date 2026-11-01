using Ais.Net.MarineCadastre.GeoPackage;
using Ais.Net.MarineCadastre.GeoTiff;
using Ais.Net.MarineCadastre.TestSupport;
using Ais.Net.MarineCadastre.Tracks;
using Ais.Net.Models.Abstractions;

using Shouldly;

namespace Ais.Net.MarineCadastre.Specs;

/// <summary>
/// Malformed registries, missing or absurd column values, string reuse, and the geometry encodings the
/// main track specs do not reach.
/// </summary>
[TestClass]
public class TrackReaderEdgeCaseTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 9, 52, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 1, 1, 0, 15, 51, TimeSpan.Zero);
    private static readonly Position[] Miami = [new(25.79027, -80.18396), new(25.79028, -80.18398)];

    [TestMethod]
    public void AGeoPackageWithNoFeaturesTableIsReported()
    {
        using GeoPackageBuilder package = new();
        package.Execute("delete from gpkg_contents");

        Should.Throw<InvalidDataException>(() => MarineCadastreTrackReader.Read(package.Path).ToList())
            .Message.ShouldContain("gpkg_contents");
    }

    [TestMethod]
    public void ARegisteredTableThatDoesNotExistIsReported()
    {
        using GeoPackageBuilder package = new();
        package.Execute("insert into gpkg_geometry_columns values ('Ghost', 'Shape', 'MULTILINESTRING', 4326, 0, 0)");

        Should.Throw<InvalidDataException>(() => MarineCadastreTrackReader.Read(package.Path, new VesselTrackReaderOptions { TableName = "Ghost" }).ToList())
            .Message.ShouldContain("Ghost");
    }

    [TestMethod]
    public void OptionalColumnsMayBeAbsent()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami) { VesselName = "ONE", Imo = "IMO1234567", Cargo = 70, Draft = 5, VesselTypeName = "Cargo", DurationMinutes = 99 });
        foreach (string column in new[] { "imo", "cargo", "draft", "vessel_type_name", "duration_minutes", "transceiver" })
        {
            package.Execute($"""alter table "{package.TableName}" drop column {column}""");
        }

        VesselTrack track = MarineCadastreTrackReader.Read(package.Path).Single();

        track.VesselName.ShouldBe("ONE");
        track.ImoNumber.ShouldBeNull();
        track.Cargo.ShouldBeNull();
        track.Draught.ShouldBeNull();
        track.ShipTypeName.ShouldBe(string.Empty);
        track.Duration.ShouldBe(End - Start);
        track.IsClassB.ShouldBeFalse();
    }

    [TestMethod]
    public void ANullMmsiIsReportedWithTheRow()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Execute($"""update "{package.TableName}" set mmsi = null""");

        Should.Throw<InvalidDataException>(() => MarineCadastreTrackReader.Read(package.Path).ToList())
            .Message.ShouldBe("Row 1 has no MMSI.");
    }

    [TestMethod]
    public void AnOutOfRangeMmsiIsReportedWithTheRow()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Execute($"""update "{package.TableName}" set mmsi = 5000000000""");

        Should.Throw<InvalidDataException>(() => MarineCadastreTrackReader.Read(package.Path).ToList())
            .Message.ShouldContain("out of range");
    }

    [TestMethod]
    public void NullAndBinaryTimestampsAreReportedWithTheRow()
    {
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami));
        package.Add(new TrackRow(2, Start, End, Miami));
        package.Execute($"""update "{package.TableName}" set start_time = null where OBJECTID = 1""");
        package.Execute($"""update "{package.TableName}" set end_time = x'00' where OBJECTID = 2""");

        Should.Throw<InvalidDataException>(() => MarineCadastreTrackReader.Read(package.Path).ToList())
            .Message.ShouldBe("Row 1 has no start time.");
        Should.Throw<InvalidDataException>(() => MarineCadastreTrackReader.Read(package.Path, new VesselTrackReaderOptions { Mmsi = 2 }).ToList())
            .Message.ShouldBe("Row 2 has an unreadable end time.");
    }

    [TestMethod]
    public void TimeFiltersWorkWhenTimestampsAreStoredAsUnixSeconds()
    {
        using GeoPackageBuilder package = new();
        DateTimeOffset midnight = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        package.Add(new TrackRow(1, midnight, midnight.AddMinutes(10), Miami));
        package.Add(new TrackRow(2, midnight.AddMinutes(20), midnight.AddMinutes(30), Miami));
        package.Add(new TrackRow(3, midnight.AddMinutes(40), midnight.AddMinutes(50), Miami));
        package.Execute($"""update "{package.TableName}" set start_time = unixepoch(start_time), end_time = unixepoch(end_time)""");

        VesselTrackReaderOptions window = new() { From = midnight.AddMinutes(15), To = midnight.AddMinutes(35) };

        MarineCadastreTrackReader.Read(package.Path, window).Select(t => t.Mmsi).ShouldBe([2u]);
        MarineCadastreTrackReader.Read(package.Path, new VesselTrackReaderOptions { To = midnight.AddMinutes(5) }).Select(t => t.Mmsi).ShouldBe([1u]);
    }

    [TestMethod]
    public void TimeFiltersAreExactBelowTheSecondTheQueryPrefilterWorksAt()
    {
        using GeoPackageBuilder package = new();
        DateTimeOffset start = Start.AddMilliseconds(700);
        DateTimeOffset end = End.AddMilliseconds(700);
        package.Add(new TrackRow(1, start, end, Miami));

        // The SQL pre-filter compares whole seconds, so these pass it; the row-level check must reject them.
        MarineCadastreTrackReader.Read(package.Path, new VesselTrackReaderOptions { From = end.AddMilliseconds(200) }).ShouldBeEmpty();
        MarineCadastreTrackReader.Read(package.Path, new VesselTrackReaderOptions { To = start.AddMilliseconds(-500) }).ShouldBeEmpty();
        MarineCadastreTrackReader.Read(package.Path, new VesselTrackReaderOptions { From = end, To = start }).Count().ShouldBe(1);
    }

    [TestMethod]
    public void ConsecutiveTracksOfOneVesselShareTheirStrings()
    {
        string longName = new('A', 80);
        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami) { VesselName = "TURN TWO", CallSign = "ODYSSEY", VesselTypeName = "Pleasure Craft" });
        package.Add(new TrackRow(1, Start.AddHours(1), End.AddHours(1), Miami) { VesselName = "TURN TWO", CallSign = "ODYSSEY", VesselTypeName = "Pleasure Craft" });
        package.Add(new TrackRow(2, Start, End, Miami) { VesselName = longName, CallSign = "WDL7894", VesselTypeName = "Tug" });
        package.Add(new TrackRow(2, Start.AddHours(1), End.AddHours(1), Miami) { VesselName = longName, CallSign = "WDL7894", VesselTypeName = "Tug" });

        List<VesselTrack> tracks = [.. MarineCadastreTrackReader.Read(package.Path)];

        ReferenceEquals(tracks[0].VesselName, tracks[1].VesselName).ShouldBeTrue();
        ReferenceEquals(tracks[0].CallSign, tracks[1].CallSign).ShouldBeTrue();
        ReferenceEquals(tracks[0].ShipTypeName, tracks[1].ShipTypeName).ShouldBeTrue();
        tracks[2].VesselName.ShouldBe(longName);
        ReferenceEquals(tracks[2].VesselName, tracks[3].VesselName).ShouldBeTrue();
        tracks[2].VesselName.ShouldNotBe(tracks[1].VesselName);
    }

    [TestMethod]
    public void ABoundingBoxNeedsAtLeastOnePosition()
    {
        Should.Throw<ArgumentException>(() => BoundingBox.FromPositions([]));
        BoundingBox.FromPositions([new Position(1, 2)]).ShouldBe(new BoundingBox(2, 1, 2, 1));
    }

    [TestMethod]
    public void ExtendedGeoPackageGeometriesAreRejected()
    {
        byte[] blob = GeoPackageGeometry.Encode([Miami]);
        blob[3] |= 0x20;

        Should.Throw<InvalidDataException>(() => GeoPackageGeometry.DecodeLine(blob, out _)).Message.ShouldContain("Extended");

        using GeoPackageBuilder package = new();
        package.Add(new TrackRow(1, Start, End, Miami) { Geometry = blob });
        Should.Throw<InvalidDataException>(() => MarineCadastreTrackReader.Read(package.Path).ToList());
    }

    [TestMethod]
    public void PostGisStyleSridFlagsAreSkipped()
    {
        // A LineString with the EWKB SRID flag set and a four-byte SRID after the type word.
        byte[] plain = GeoPackageGeometry.Encode([Miami], forceMulti: false);
        List<byte> ewkb = [.. plain[..8], plain[8]];
        uint type = BitConverter.ToUInt32(plain, 9) | 0x20000000;
        ewkb.AddRange(BitConverter.GetBytes(type));
        ewkb.AddRange(BitConverter.GetBytes(4326));
        ewkb.AddRange(plain[13..]);

        GeoPackageGeometry.DecodeLine([.. ewkb], out _).ShouldBe(Miami);
    }

    [TestMethod]
    public void LzwDataMustStartWithALiteral()
    {
        // Nine-bit codes 256 (clear) then 300: 1_0000_0000 1_0010_1100, packed most significant bit first.
        byte[] encoded = [0x80, 0x4B, 0x00];

        Should.Throw<InvalidDataException>(() => LzwDecoder.Decode(encoded, new byte[8])).Message.ShouldContain("starts with");
    }
}
