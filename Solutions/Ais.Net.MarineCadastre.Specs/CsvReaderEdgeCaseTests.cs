using System.Text;

using Ais.Net.MarineCadastre.Csv;
using Ais.Net.Models;

using Shouldly;

namespace Ais.Net.MarineCadastre.Specs;

/// <summary>
/// The file overloads, the ends of files, blank and malformed lines: the paths the main reader specs
/// do not reach.
/// </summary>
[TestClass]
public class CsvReaderEdgeCaseTests
{
    private const string Header = "mmsi,base_date_time,longitude,latitude,sog,cog,heading,vessel_name,imo,call_sign,vessel_type,status,length,width,draft,cargo,transceiver";
    private const string ClassARow = "369053000,2026-01-01 00:00:01,-75.95622,36.92122,9.9,150.5,150,LIBERTY ISLAND,IMO9224831,WDFP,90,3,99,18,6.5,90,A";
    private const string ClassBRow = "368025040,2026-01-01 00:00:04,-122.50573,48.75658,0.0,,,TRUTINA,,WDJ9334,37,,13,4,,,B";

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
                (nameof(AisMessageType1Through3), 369053000u),
                (nameof(AisMessageType5), 369053000u),
                (nameof(AisMessageType19), 368025040u),
                (nameof(AisMessageType24Part1), 368025040u),
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

        MarineCadastreCsvReader.Read(new MemoryStream(csv)).Select(m => m.Mmsi).Distinct().ShouldBe([368025040u, 369053000u]);

        List<uint> mmsis = [];
        await foreach (AisMessageBase message in MarineCadastreCsvReader.ReadAsync(new MemoryStream(csv)))
        {
            mmsis.Add(message.Mmsi);
        }

        mmsis.Distinct().ShouldBe([368025040u, 369053000u]);
    }

    [TestMethod]
    public void FinalLineEndingInABareCarriageReturnIsTrimmed()
    {
        byte[] csv = Encoding.UTF8.GetBytes($"{Header}\r\n{ClassARow}\r");

        AisMessageType1Through3 message = MarineCadastreCsvReader.Read(new MemoryStream(csv)).OfType<AisMessageType1Through3>().Single();

        message.Mmsi.ShouldBe(369053000u);
    }

    [TestMethod]
    public void BlankLinesAreIgnored()
    {
        byte[] csv = Encoding.UTF8.GetBytes($"{Header}\n\n{ClassARow}\n\n\n{ClassBRow}\n\n");

        MarineCadastreCsvReader.Read(new MemoryStream(csv), new MarineCadastreReaderOptions { EmitStaticData = false }).Count().ShouldBe(2);
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

        MarineCadastreCsvReader.Read(new MemoryStream(csv)).Select(m => m.Mmsi).Distinct().ShouldBe([368025040u]);
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

    private static string WriteTemp(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ais-net-csv-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        return path;
    }
}
