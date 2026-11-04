using System.Text;

using Ais.Net.MarineCadastre.Csv;
using Ais.Net.Models;
using Ais.Net.Models.Abstractions;

using Shouldly;

namespace Ais.Net.MarineCadastre.Tests.Csv;

[TestClass]
public class MarineCadastreCsvReaderTests
{
    private const string Header = "mmsi,base_date_time,longitude,latitude,sog,cog,heading,vessel_name,imo,call_sign,vessel_type,status,length,width,draft,cargo,transceiver";
    private const string ClassARow = "999000001,2026-01-01 00:00:01,-75.95622,36.92122,9.9,150.5,150,TEST VESSEL AA,IMO7000001,TEST,90,3,99,18,6.5,90,A";
    private const string ClassBRow = "999000002,2026-01-01 00:00:04,-122.50573,48.75658,0.0,,,SAMPLER,,ZZZ0002,37,,13,4,,,B";

    [TestMethod]
    public void ClassARowBecomesType1PositionReport()
    {
        AisMessageType1Through3 message = Read(Header, ClassARow).OfType<AisMessageType1Through3>().Single();

        message.MessageType.ShouldBe(1);
        message.Mmsi.ShouldBe(999000001u);
        message.Timestamp.ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero));
        message.TimeStampSecond.ShouldBe(1u);
        message.Position.ShouldBe(new Position(36.92122, -75.95622));
        message.SpeedOverGround.ShouldBe(9.9f);
        message.CourseOverGround.ShouldBe(150.5f);
        message.TrueHeadingDegrees.ShouldBe(150u);
        message.NavigationStatus.ShouldBe(NavigationStatus.RestrictedManoeuverability);
        message.RateOfTurn.ShouldBe(-128);
        message.ManoeuvreIndicator.ShouldBe(ManoeuvreIndicator.NotAvailable);
    }

    [TestMethod]
    public void ClassARowFirstSightAlsoEmitsType5StaticData()
    {
        List<AisMessageBase> messages = Read(Header, ClassARow);

        messages.Count.ShouldBe(2);
        messages[0].ShouldBeOfType<AisMessageType1Through3>();
        AisMessageType5 statics = messages[1].ShouldBeOfType<AisMessageType5>();

        statics.Mmsi.ShouldBe(999000001u);
        statics.VesselName.ShouldBe("TEST VESSEL AA");
        statics.CallSign.ShouldBe("TEST");
        statics.ImoNumber.ShouldBe(7000001u);
        statics.ShipType.ShouldBe((ShipType)90);
        statics.DimensionToBow.ShouldBe(99u);
        statics.DimensionToPort.ShouldBe(18u);
        statics.DimensionToStarboard.ShouldBe(0u);
        statics.DimensionToStern.ShouldBe(0u);
        statics.Draught10thMetres.ShouldBe(65u);
        statics.Destination.ShouldBe(string.Empty);
        statics.Timestamp.ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero));
    }

    [TestMethod]
    public void ClassBRowBecomesType18WithNotAvailableSentinels()
    {
        List<AisMessageBase> messages = Read(Header, ClassBRow, new MarineCadastreCsvReaderOptions { EmitStaticData = false });

        AisMessageType18 message = messages.Single().ShouldBeOfType<AisMessageType18>();
        message.Mmsi.ShouldBe(999000002u);
        message.SpeedOverGround.ShouldBe(0.0f);
        message.CourseOverGround.ShouldBeNull();
        message.TrueHeadingDegrees.ShouldBe(511u);
        message.Position.ShouldBe(new Position(48.75658, -122.50573));
        message.Timestamp.ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 4, TimeSpan.Zero));
    }

    [TestMethod]
    public void ClassBRowFirstSightEmitsType19ThenType24Part1()
    {
        List<AisMessageBase> messages = Read(Header, ClassBRow);

        messages.Count.ShouldBe(2);
        AisMessageType19 extended = messages[0].ShouldBeOfType<AisMessageType19>();
        extended.ShipName.ShouldBe("SAMPLER");
        extended.ShipType.ShouldBe((ShipType)37);
        extended.DimensionToBow.ShouldBe(13u);
        extended.DimensionToPort.ShouldBe(4u);
        extended.Position.ShouldBe(new Position(48.75658, -122.50573));
        extended.TrueHeadingDegrees.ShouldBe(511u);

        AisMessageType24Part1 part1 = messages[1].ShouldBeOfType<AisMessageType24Part1>();
        part1.CallSign.ShouldBe("ZZZ0002");
        part1.ShipType.ShouldBe((ShipType)37);
        part1.PartNumber.ShouldBe(1u);
    }

    [TestMethod]
    public void EmptyStatusAndHeadingBecomeNotAvailable()
    {
        string row = "999000004,2026-01-01 00:00:01,-90.46691,40.00764,4.7,79.2,,FIXTURE BOAT,,ZZZ0004,31,,347,33,3.0,57,A";

        AisMessageType1Through3 message = Read(Header, row).OfType<AisMessageType1Through3>().Single();

        message.NavigationStatus.ShouldBe(NavigationStatus.NotDefined);
        message.TrueHeadingDegrees.ShouldBe(511u);
    }

    [TestMethod]
    public void UnchangedStaticDataIsNotRepeated()
    {
        string second = ClassARow.Replace("2026-01-01 00:00:01", "2026-01-01 00:00:11").Replace("-75.95622", "-75.95700");

        List<AisMessageBase> messages = Read(Header, ClassARow, second);

        messages.Count.ShouldBe(3);
        messages.OfType<AisMessageType5>().Count().ShouldBe(1);
        messages.OfType<AisMessageType1Through3>().Count().ShouldBe(2);
    }

    [TestMethod]
    public void ChangedStaticDataIsEmittedAgain()
    {
        string renamed = ClassARow.Replace("TEST VESSEL AA", "TEST VESSEL AA II");

        List<AisMessageBase> messages = Read(Header, ClassARow, renamed);

        messages.OfType<AisMessageType5>().Select(m => m.VesselName).ShouldBe(["TEST VESSEL AA", "TEST VESSEL AA II"]);
    }

    [TestMethod]
    public void StaticDataIsTrackedPerVessel()
    {
        List<AisMessageBase> messages = Read(Header, ClassARow, ClassBRow, ClassARow, ClassBRow);

        messages.OfType<AisMessageType5>().Count().ShouldBe(1);
        messages.OfType<AisMessageType19>().Count().ShouldBe(1);
        messages.OfType<AisMessageType18>().Count().ShouldBe(1);
        messages.OfType<AisMessageType1Through3>().Count().ShouldBe(2);
    }

    [TestMethod]
    public void RowWithNoStaticColumnsEmitsNoStaticMessage()
    {
        string bare = "999000005,2026-01-01 00:00:07,-93.87796,29.98156,5.6,97.0,100,,,,,0,,,,,A";

        List<AisMessageBase> messages = Read(Header, bare);

        messages.Single().ShouldBeOfType<AisMessageType1Through3>();
    }

    [TestMethod]
    public void QuotedNameWithCommaAndDoubledQuotesIsUnescaped()
    {
        string row = "999000009,2026-01-01 00:00:02,-95.6722,29.06032,0.0,247.1,19,\"ABE \"\"CHIEF\"\" OTTER, JR\",,ZZZ0009,31,12,20,8,,57,A";

        AisMessageType5 statics = Read(Header, row).OfType<AisMessageType5>().Single();

        statics.VesselName.ShouldBe("ABE \"CHIEF\" OTTER, JR");
        statics.CallSign.ShouldBe("ZZZ0009");
    }

    [TestMethod]
    public void ShipTypeOutsideAisRangeBecomesNotAvailable()
    {
        string row = ClassARow.Replace(",90,3,99,18,6.5,90,A", ",152,3,99,18,6.5,,A");

        AisMessageType5 statics = Read(Header, row).OfType<AisMessageType5>().Single();

        statics.ShipType.ShouldBe(ShipType.NotAvailable);
    }

    [TestMethod]
    public void ShipTypeSourceCanPreferCargo()
    {
        string row = ClassARow.Replace(",90,3,99,18,6.5,90,A", ",31,3,99,18,6.5,57,A");

        Read(Header, row).OfType<AisMessageType5>().Single().ShipType.ShouldBe((ShipType)31);
        Read(Header, row, new MarineCadastreCsvReaderOptions { ShipTypeSource = ShipTypeSource.CargoThenVesselType })
            .OfType<AisMessageType5>().Single().ShipType.ShouldBe((ShipType)57);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void ClassAMessageTypeOptionIsHonoured(int messageType)
    {
        Read(Header, ClassARow, new MarineCadastreCsvReaderOptions { ClassAMessageType = messageType })
            .OfType<AisMessageType1Through3>().Single().MessageType.ShouldBe(messageType);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(4)]
    [DataRow(9)]
    [DataRow(-1)]
    public void ClassAMessageTypeOutsideOneToThreeIsRejected(int messageType)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new MarineCadastreCsvReaderOptions { ClassAMessageType = messageType })
            .ParamName.ShouldBe(nameof(MarineCadastreCsvReaderOptions.ClassAMessageType));
        Should.Throw<ArgumentOutOfRangeException>(() => MarineCadastreCsvReaderOptions.Default with { ClassAMessageType = messageType })
            .ParamName.ShouldBe(nameof(MarineCadastreCsvReaderOptions.ClassAMessageType));
    }

    [TestMethod]
    public void HeaderOrderAndCasingDoNotMatter()
    {
        string header = "BaseDateTime,LAT,LON,MMSI,VesselName,TransceiverClass,SOG";
        string row = "2026-01-01 00:00:01,36.92122,-75.95622,999000001,TEST VESSEL AA,A,9.9";

        List<AisMessageBase> messages = Read(header, row);

        AisMessageType1Through3 position = messages[0].ShouldBeOfType<AisMessageType1Through3>();
        position.Mmsi.ShouldBe(999000001u);
        position.Position.ShouldBe(new Position(36.92122, -75.95622));
        position.SpeedOverGround.ShouldBe(9.9f);
        messages[1].ShouldBeOfType<AisMessageType5>().VesselName.ShouldBe("TEST VESSEL AA");
    }

    [TestMethod]
    public void MissingRequiredColumnThrows()
    {
        Should.Throw<FormatException>(() => Read("mmsi,latitude,longitude", "1,2,3"))
            .Message.ShouldContain("base_date_time");
    }

    [TestMethod]
    public void MalformedRowsAreSkipped()
    {
        const string Garbage = "999000001,garbage,-75.9,36.9,,,,,,,,,,,,,A";
        List<(long LineNumber, CsvRowSkipReason Reason, string Line)> skipped = [];
        MarineCadastreCsvReaderOptions options = new()
        {
            OnRowSkipped = row => skipped.Add((row.LineNumber, row.Reason, Encoding.UTF8.GetString(row.Line))),
        };

        List<AisMessageBase> messages = Read(Header, ["not,a,row", ClassARow, Garbage], options);

        messages.OfType<AisMessageType1Through3>().Single().Mmsi.ShouldBe(999000001u);
        skipped.ShouldBe(
        [
            (2L, CsvRowSkipReason.TooFewFields, "not,a,row"),
            (4L, CsvRowSkipReason.InvalidTimestamp, Garbage),
        ]);
    }

    [TestMethod]
    public void CrLfLinesByteOrderMarkAndMissingFinalNewlineAreHandled()
    {
        byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Header + "\r\n" + ClassARow + "\r\n" + ClassBRow)];

        List<AisMessageBase> messages = [.. MarineCadastreCsvReader.Read(new MemoryStream(bytes))];

        messages.Count.ShouldBe(4);
        messages[0].Mmsi.ShouldBe(999000001u);
        messages[2].Mmsi.ShouldBe(999000002u);
    }

    [TestMethod]
    public async Task ReadAsyncYieldsTheSameMessagesAsRead()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', Header, ClassARow, ClassBRow, ClassARow) + "\n");

        List<AisMessageBase> sync = [.. MarineCadastreCsvReader.Read(new MemoryStream(bytes))];
        List<AisMessageBase> async = [];
        await foreach (AisMessageBase message in MarineCadastreCsvReader.ReadAsync(new MemoryStream(bytes)))
        {
            async.Add(message);
        }

        async.ShouldBe(sync);
    }

    [TestMethod]
    public void EveryMessageImplementsIAisTimestamp()
    {
        foreach (AisMessageBase message in Read(Header, ClassARow, ClassBRow))
        {
            message.ShouldBeAssignableTo<IAisTimestamp>().Timestamp.ShouldNotBeNull();
        }
    }

    private static List<AisMessageBase> Read(string header, params string[] rows) => Read(header, rows, null);

    private static List<AisMessageBase> Read(string header, string row, MarineCadastreCsvReaderOptions options) => Read(header, [row], options);

    private static List<AisMessageBase> Read(string header, string[] rows, MarineCadastreCsvReaderOptions? options)
    {
        string text = header + "\n" + string.Join('\n', rows) + "\n";
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(text));
        return [.. MarineCadastreCsvReader.Read(stream, options)];
    }
}
