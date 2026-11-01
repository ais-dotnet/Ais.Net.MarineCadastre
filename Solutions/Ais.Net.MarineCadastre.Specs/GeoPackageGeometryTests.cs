using Ais.Net.Models.Abstractions;

using Shouldly;

namespace Ais.Net.MarineCadastre.Specs;

[TestClass]
public class GeoPackageGeometryTests
{
    private static readonly Position[] PartA = [new(25.79027, -80.18396), new(25.79028, -80.18398)];
    private static readonly Position[] PartB = [new(47.62895, -122.39129), new(47.63, -122.39), new(47.631, -122.389)];

    [TestMethod]
    public void DecodesARealTrackBlob()
    {
        // OBJECTID 1 of AISVesselTracks2026-01.gpkg: header, no envelope, little-endian MultiLineString of one two-point part.
        byte[] blob =
        [
            0x47, 0x50, 0x00, 0x01, 0xE6, 0x10, 0x00, 0x00,
            0x01, 0x05, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
            0x01, 0x02, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
            .. BitConverter.GetBytes(-80.18395999999996), .. BitConverter.GetBytes(25.790270000000078),
            .. BitConverter.GetBytes(-80.18397999999996), .. BitConverter.GetBytes(25.790280000000052),
        ];

        Position[] points = GeoPackageGeometry.DecodeLine(blob, out BoundingBox envelope);

        points.ShouldBe([new(25.790270000000078, -80.18395999999996), new(25.790280000000052, -80.18397999999996)]);
        envelope.ShouldBe(new BoundingBox(-80.18397999999996, 25.790270000000078, -80.18395999999996, 25.790280000000052));
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(false, 1)]
    [DataRow(false, 2)]
    [DataRow(false, 3)]
    [DataRow(false, 4)]
    [DataRow(true, 0)]
    [DataRow(true, 1)]
    [DataRow(true, 4)]
    public void DecodesEitherByteOrderAndSkipsAnyEnvelope(bool bigEndian, int envelopeIndicator)
    {
        byte[] blob = GeoPackageGeometry.Encode([PartA], bigEndian: bigEndian, envelopeIndicator: envelopeIndicator);

        GeoPackageGeometry.DecodeLine(blob, out _).ShouldBe(PartA);
    }

    [TestMethod]
    public void ConcatenatesThePartsOfAMultiLineString()
    {
        byte[] blob = GeoPackageGeometry.Encode([PartA, PartB]);

        GeoPackageGeometry.DecodeLine(blob, out BoundingBox envelope).ShouldBe([.. PartA, .. PartB]);
        envelope.ShouldBe(new BoundingBox(-122.39129, 25.79027, -80.18396, 47.631));
    }

    [TestMethod]
    public void AcceptsAPlainLineString()
    {
        byte[] blob = GeoPackageGeometry.Encode([PartB], forceMulti: false);

        GeoPackageGeometry.DecodeLine(blob, out _).ShouldBe(PartB);
    }

    [TestMethod]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(true, true, false)]
    [DataRow(true, false, true)]
    [DataRow(false, true, true)]
    [DataRow(true, true, true)]
    public void DropsZAndMOrdinatesInIsoAndExtendedEncodings(bool withZ, bool withM, bool extendedFlags)
    {
        byte[] blob = GeoPackageGeometry.Encode([PartA, PartB], withZ: withZ, withM: withM, extendedFlags: extendedFlags);

        GeoPackageGeometry.DecodeLine(blob, out _).ShouldBe([.. PartA, .. PartB]);
    }

    [TestMethod]
    public void EmptyFlagYieldsNoPoints()
    {
        byte[] blob = GeoPackageGeometry.Encode([PartA], empty: true);

        GeoPackageGeometry.DecodeLine(blob, out BoundingBox envelope).ShouldBeEmpty();
        envelope.ShouldBe(default);
    }

    [TestMethod]
    public void MultiLineStringWithNoPartsYieldsNoPoints()
    {
        GeoPackageGeometry.DecodeLine(GeoPackageGeometry.Encode([]), out _).ShouldBeEmpty();
    }

    [TestMethod]
    public void RejectsBlobsThatAreNotGeoPackageGeometries()
    {
        Should.Throw<InvalidDataException>(() => GeoPackageGeometry.DecodeLine("not a geometry"u8, out _));
        Should.Throw<InvalidDataException>(() => GeoPackageGeometry.DecodeLine([0x47, 0x50], out _));
    }

    [TestMethod]
    public void RejectsGeometriesOtherThanLines()
    {
        // A little-endian WKB Point behind a valid header.
        byte[] point = [0x47, 0x50, 0x00, 0x01, 0xE6, 0x10, 0x00, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, .. new byte[16]];

        Should.Throw<InvalidDataException>(() => GeoPackageGeometry.DecodeLine(point, out _)).Message.ShouldContain("type 1");
    }

    [TestMethod]
    public void RejectsTruncatedGeometries()
    {
        byte[] blob = GeoPackageGeometry.Encode([PartB]);

        Should.Throw<InvalidDataException>(() => GeoPackageGeometry.DecodeLine(blob.AsSpan(0, blob.Length - 1), out _));
    }

    [TestMethod]
    public void RejectsInvalidEnvelopeIndicators()
    {
        byte[] blob = GeoPackageGeometry.Encode([PartA]);
        blob[3] = (byte)(blob[3] | (5 << 1));

        Should.Throw<InvalidDataException>(() => GeoPackageGeometry.DecodeLine(blob, out _));
    }
}
