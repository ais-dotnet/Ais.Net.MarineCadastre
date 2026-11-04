using System.Text;

using Ais.Net.MarineCadastre.Csv;
using Ais.Net.Models;

using Shouldly;

namespace Ais.Net.MarineCadastre.Tests.Csv;

/// <summary>
/// The file overloads, the ends of files, blank and malformed lines: the paths the main reader specs
/// do not reach.
/// </summary>
[TestClass]
public class CsvReaderEdgeCaseTests
{
    private const string Header = "mmsi,base_date_time,longitude,latitude,sog,cog,heading,vessel_name,imo,call_sign,vessel_type,status,length,width,draft,cargo,transceiver";
    private const string ClassARow = "999000001,2026-01-01 00:00:01,-75.95622,36.92122,9.9,150.5,150,TEST VESSEL AA,IMO7000001,TEST,90,3,99,18,6.5,90,A";
    private const string ClassBRow = "999000002,2026-01-01 00:00:04,-122.50573,48.75658,0.0,,,SAMPLER,,ZZZ0002,37,,13,4,,,B";

    [TestMethod]
    public async Task ReadFileAndReadFileAsyncReadTheSameMessages()
    {
        string path = WriteTemp($"{Header}\n{ClassARow}\n{ClassBRow}");
        try
        {
            List<AisMessageBase> sync = [.. MarineCadastreCsvReader.ReadFile(path)];
            List<AisMessageBase> async = [];
            await foreach (AisMessageBase message in MarineCadastreCsvReader.ReadFileAsync(path))
            {
                async.Add(message);
            }

            sync.Select(m => (m.GetType().Name, m.Mmsi)).ShouldBe(
            [
                (nameof(AisMessageType1Through3), 999000001u),
                (nameof(AisMessageType5), 999000001u),
                (nameof(AisMessageType19), 999000002u),
                (nameof(AisMessageType24Part1), 999000002u),
            ]);
            async.Select(m => (m.GetType().Name, m.Mmsi)).ShouldBe(sync.Select(m => (m.GetType().Name, m.Mmsi)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void FilePathsAreValidatedEagerly()
    {
        Should.Throw<ArgumentException>(() => MarineCadastreCsvReader.ReadFile(string.Empty));
        Should.Throw<ArgumentException>(() => MarineCadastreCsvReader.ReadFileAsync(string.Empty));
        Should.Throw<FileNotFoundException>(() => MarineCadastreCsvReader.ReadFile(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csv")).ToList());
    }

    [TestMethod]
    public async Task FinalLineWithoutNewlineIsReadByBothPaths()
    {
        byte[] csv = Encoding.UTF8.GetBytes($"{Header}\n{ClassBRow}\n{ClassARow}");

        MarineCadastreCsvReader.Read(new MemoryStream(csv)).Select(m => m.Mmsi).Distinct().ShouldBe([999000002u, 999000001u]);

        List<uint> mmsis = [];
        await foreach (AisMessageBase message in MarineCadastreCsvReader.ReadAsync(new MemoryStream(csv)))
        {
            mmsis.Add(message.Mmsi);
        }

        mmsis.Distinct().ShouldBe([999000002u, 999000001u]);
    }

    [TestMethod]
    public void FinalLineEndingInABareCarriageReturnIsTrimmed()
    {
        byte[] csv = Encoding.UTF8.GetBytes($"{Header}\r\n{ClassARow}\r");

        AisMessageType1Through3 message = MarineCadastreCsvReader.Read(new MemoryStream(csv)).OfType<AisMessageType1Through3>().Single();

        message.Mmsi.ShouldBe(999000001u);
    }

    [TestMethod]
    public void BlankLinesAreIgnored()
    {
        byte[] csv = Encoding.UTF8.GetBytes($"{Header}\n\n{ClassARow}\n\n\n{ClassBRow}\n\n");

        MarineCadastreCsvReader.Read(new MemoryStream(csv), new MarineCadastreCsvReaderOptions { EmitStaticData = false }).Count().ShouldBe(2);
    }

    [TestMethod]
    public void IntegerColumnsWrittenWithADecimalPointAreAccepted()
    {
        string row = ClassARow.Replace(",150,LIBERTY", ",150.0,LIBERTY").Replace(",90,3,99,18,", ",90.0,3.0,99.0,18.0,");
        byte[] csv = Encoding.UTF8.GetBytes($"{Header}\n{row}\n");

        List<AisMessageBase> messages = [.. MarineCadastreCsvReader.Read(new MemoryStream(csv))];

        AisMessageType1Through3 position = messages.OfType<AisMessageType1Through3>().Single();
        position.TrueHeadingDegrees.ShouldBe(150u);
        position.NavigationStatus.ShouldBe(NavigationStatus.RestrictedManoeuverability);
        AisMessageType5 statics = messages.OfType<AisMessageType5>().Single();
        statics.DimensionToBow.ShouldBe(99u);
        statics.ShipType.ShouldBe((ShipType)90);
    }

    [TestMethod]
    [DataRow("2026-13-01 00:00:01")]
    [DataRow("2026-02-30 00:00:01")]
    [DataRow("2026-01-01 24:00:00")]
    [DataRow("2026-01-01 00:60:00")]
    [DataRow("2026-01-0x 00:00:01")]
    [DataRow("yesterday")]
    public void RowsWithUnreadableTimestampsAreSkipped(string timestamp)
    {
        string row = ClassARow.Replace("2026-01-01 00:00:01", timestamp);
        byte[] csv = Encoding.UTF8.GetBytes($"{Header}\n{row}\n{ClassBRow}\n");
        List<(long LineNumber, CsvRowSkipReason Reason, string Line)> skipped = [];

        MarineCadastreCsvReader.Read(new MemoryStream(csv), Recording(skipped)).Select(m => m.Mmsi).Distinct().ShouldBe([999000002u]);

        skipped.ShouldBe([(2L, CsvRowSkipReason.InvalidTimestamp, row)]);
    }

    [TestMethod]
    public void SkippedRowsAreReportedWithTheirLineNumberAndReason()
    {
        string badMmsi = ClassARow.Replace("999000001,", "abc,");
        string emptyMmsi = ClassARow.Replace("999000001,", ",");
        string badTimestamp = ClassARow.Replace("2026-01-01 00:00:01", "yesterday");
        byte[] csv =
        [
            .. Encoding.UTF8.GetPreamble(),
            .. Encoding.UTF8.GetBytes(string.Join("\r\n", Header, string.Empty, "1,2,3", badMmsi, emptyMmsi, badTimestamp, ClassARow) + "\r\n"),
        ];
        List<(long LineNumber, CsvRowSkipReason Reason, string Line)> skipped = [];

        List<AisMessageBase> messages = [.. MarineCadastreCsvReader.Read(new MemoryStream(csv), Recording(skipped))];

        messages.OfType<AisMessageType1Through3>().Single().Mmsi.ShouldBe(999000001u);
        skipped.ShouldBe(
        [
            (3L, CsvRowSkipReason.TooFewFields, "1,2,3"),
            (4L, CsvRowSkipReason.InvalidMmsi, badMmsi),
            (5L, CsvRowSkipReason.InvalidMmsi, emptyMmsi),
            (6L, CsvRowSkipReason.InvalidTimestamp, badTimestamp),
        ]);
        skipped.ShouldAllBe(s => !s.Line.Contains('\r'));
    }

    [TestMethod]
    public void RowsWithPositionsOutOfRangeAreEmittedNotSkipped()
    {
        string row = ClassARow.Replace("36.92122", "95");
        byte[] csv = Encoding.UTF8.GetBytes($"{Header}\n{row}\n");
        List<(long LineNumber, CsvRowSkipReason Reason, string Line)> skipped = [];

        AisMessageType1Through3 message = MarineCadastreCsvReader.Read(new MemoryStream(csv), Recording(skipped))
            .OfType<AisMessageType1Through3>().Single();

        message.Mmsi.ShouldBe(999000001u);
        message.Position.ShouldBeNull();
        skipped.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task SkippedRowsAreReportedOnTheAsyncPaths()
    {
        string badMmsi = ClassARow.Replace("999000001,", "abc,");
        string content = $"{Header}\n{badMmsi}\n{ClassBRow}\n1,2\n";
        (long LineNumber, CsvRowSkipReason Reason, string Line)[] expected =
        [
            (2L, CsvRowSkipReason.InvalidMmsi, badMmsi),
            (4L, CsvRowSkipReason.TooFewFields, "1,2"),
        ];

        List<(long LineNumber, CsvRowSkipReason Reason, string Line)> fromStream = [];
        await foreach (AisMessageBase message in MarineCadastreCsvReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(content)), Recording(fromStream)))
        {
            message.Mmsi.ShouldBe(999000002u);
        }

        fromStream.ShouldBe(expected);

        string path = WriteTemp(content);
        try
        {
            List<(long LineNumber, CsvRowSkipReason Reason, string Line)> fromFile = [];
            await foreach (AisMessageBase message in MarineCadastreCsvReader.ReadFileAsync(path, Recording(fromFile)))
            {
                message.Mmsi.ShouldBe(999000002u);
            }

            fromFile.ShouldBe(expected);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task AnExceptionThrownByTheCallbackEndsTheRead()
    {
        string badMmsi = ClassARow.Replace("999000001,", "abc,");
        byte[] csv = Encoding.UTF8.GetBytes($"{Header}\n{ClassBRow}\n{badMmsi}\n{ClassARow}\n");
        MarineCadastreCsvReaderOptions strict = new()
        {
            EmitStaticData = false,
            OnRowSkipped = row => throw new InvalidDataException($"Line {row.LineNumber}: {row.Reason}"),
        };

        List<uint> sync = [];
        Should.Throw<InvalidDataException>(() =>
        {
            foreach (AisMessageBase message in MarineCadastreCsvReader.Read(new MemoryStream(csv), strict))
            {
                sync.Add(message.Mmsi);
            }
        }).Message.ShouldBe("Line 3: InvalidMmsi");
        sync.ShouldBe([999000002u]);

        List<uint> async = [];
        (await Should.ThrowAsync<InvalidDataException>(async () =>
        {
            await foreach (AisMessageBase message in MarineCadastreCsvReader.ReadAsync(new MemoryStream(csv), strict))
            {
                async.Add(message.Mmsi);
            }
        })).Message.ShouldBe("Line 3: InvalidMmsi");
        async.ShouldBe([999000002u]);
    }

    [TestMethod]
    public void BlankLinesAndTheHeaderAreNeverReported()
    {
        List<(long LineNumber, CsvRowSkipReason Reason, string Line)> skipped = [];

        MarineCadastreCsvReader.Read(new MemoryStream(Encoding.UTF8.GetBytes($"{Header}\n\n{ClassARow}\r\n\r\n\n{ClassBRow}\n\n")), Recording(skipped)).Count().ShouldBe(4);
        MarineCadastreCsvReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(Header)), Recording(skipped)).ShouldBeEmpty();
        MarineCadastreCsvReader.Read(new MemoryStream(Encoding.UTF8.GetBytes($"{Header}\n\n\n")), Recording(skipped)).ShouldBeEmpty();

        skipped.ShouldBeEmpty();
    }

    [TestMethod]
    public void QuotedRecordEndingInASeparatorHasATrailingEmptyField()
    {
        byte[] line = Encoding.UTF8.GetBytes("\"a\",b,");
        Span<CsvField> fields = stackalloc CsvField[8];

        int count = CsvLine.Split(line, fields);

        count.ShouldBe(3);
        fields[2].Length.ShouldBe(0);
    }

    [TestMethod]
    public async Task FillAsyncKeepsReturningFalseAfterTheEndOfTheStream()
    {
        LineBuffer buffer = new(new MemoryStream([]));

        (await buffer.FillAsync(CancellationToken.None)).ShouldBeFalse();
        (await buffer.FillAsync(CancellationToken.None)).ShouldBeFalse();
    }

    private static MarineCadastreCsvReaderOptions Recording(List<(long LineNumber, CsvRowSkipReason Reason, string Line)> skipped) =>
        new() { OnRowSkipped = row => skipped.Add((row.LineNumber, row.Reason, Encoding.UTF8.GetString(row.Line))) };

    private static string WriteTemp(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ais-net-csv-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        return path;
    }
}
