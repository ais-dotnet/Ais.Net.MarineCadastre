using System.Text;

using Ais.Net.MarineCadastre.Csv;
using Ais.Net.Models;

using Shouldly;

namespace Ais.Net.MarineCadastre.Tests.Csv;

/// <summary>
/// Field values that look nearly right: the AIS "not available" values for speed and course, and
/// identifiers or timestamps with text after the part that parses.
/// </summary>
[TestClass]
public class CsvValueValidationTests
{
    private const string Header = "mmsi,base_date_time,longitude,latitude,sog,cog,heading,vessel_name,imo,call_sign,vessel_type,status,length,width,draft,cargo,transceiver";

    [TestMethod]
    [DataRow("102.3", "360.0", null, null, DisplayName = "AIS not-available values")]
    [DataRow("102.5", "511", null, null, DisplayName = "Above the not-available values")]
    [DataRow("-0.1", "-1", null, null, DisplayName = "Negative values")]
    [DataRow("NaN", "NaN", null, null, DisplayName = "NaN")]
    [DataRow("102.2", "359.9", 102.2f, 359.9f, DisplayName = "Largest real values")]
    [DataRow("0", "0", 0f, 0f, DisplayName = "Zero")]
    public void SpeedAndCourseOutsideTheAisRangeBecomeNull(string sog, string cog, float? expectedSog, float? expectedCog)
    {
        string row = $"999000001,2026-01-01 00:00:01,-75.95622,36.92122,{sog},{cog},150,,,,,,,,,,A";

        AisMessageType1Through3 position = Read(row).OfType<AisMessageType1Through3>().Single();

        position.SpeedOverGround.ShouldBe(expectedSog);
        position.CourseOverGround.ShouldBe(expectedCog);
    }

    [TestMethod]
    public void SpeedAndCourseNotAvailableValuesBecomeNullForClassB()
    {
        string row = "999000002,2026-01-01 00:00:01,-122.5,48.7,102.3,360,,,,,,,,,,,B";

        AisMessageType18 position = Read(row).OfType<AisMessageType18>().Single();

        position.SpeedOverGround.ShouldBeNull();
        position.CourseOverGround.ShouldBeNull();
    }

    [TestMethod]
    [DataRow("123abc")]
    [DataRow("123.0")]
    [DataRow("-123")]
    [DataRow("99999999999")]
    public void AnMmsiWithTextAfterTheDigitsIsSkipped(string mmsi)
    {
        string row = $"{mmsi},2026-01-01 00:00:01,-75.9,36.9,,,,,,,,,,,,,A";
        List<CsvRowSkipReason> skipped = [];

        Read(row, skipped).ShouldBeEmpty();

        skipped.ShouldBe([CsvRowSkipReason.InvalidMmsi]);
    }

    [TestMethod]
    public void AnMmsiPaddedWithSpacesIsRead()
    {
        string row = " 999000001 ,2026-01-01 00:00:01,-75.9,36.9,,,,,,,,,,,,,A";

        Read(row).Single().Mmsi.ShouldBe(999000001u);
    }

    [TestMethod]
    [DataRow("2026-01-01 00:00:01+05:00")]
    [DataRow("2026-01-01 00:00:01 UTC")]
    [DataRow("2026-01-01 00:00:01.")]
    [DataRow("2026-01-01 00:00:01.Z")]
    [DataRow("2026-01-01 00:00:01.5ZZ")]
    [DataRow("2026-01-01 00:00:01Z extra")]
    public void ATimestampWithUnexpectedTextAfterTheSecondsIsSkipped(string timestamp)
    {
        string row = $"999000001,{timestamp},-75.9,36.9,,,,,,,,,,,,,A";
        List<CsvRowSkipReason> skipped = [];

        Read(row, skipped).ShouldBeEmpty();

        skipped.ShouldBe([CsvRowSkipReason.InvalidTimestamp]);
    }

    [TestMethod]
    [DataRow("2026-01-01 00:00:01")]
    [DataRow("2026-01-01T00:00:01")]
    [DataRow("2026-01-01T00:00:01Z")]
    [DataRow("2026-01-01T00:00:01.5")]
    [DataRow("2026-01-01T00:00:01.987654321Z")]
    public void ATimestampWithAFractionOrAZIsReadAsUtcToTheSecond(string timestamp)
    {
        string row = $"999000001,{timestamp},-75.9,36.9,,,,,,,,,,,,,A";

        Read(row).Single().Timestamp.ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero));
    }

    private static List<AisMessageBase> Read(string row, List<CsvRowSkipReason>? skipped = null)
    {
        MarineCadastreCsvReaderOptions options = new()
        {
            EmitStaticData = false,
            OnRowSkipped = skipped is null ? null : r => skipped.Add(r.Reason),
        };

        return [.. MarineCadastreCsvReader.Read(new MemoryStream(Encoding.UTF8.GetBytes($"{Header}\n{row}\n")), options)];
    }
}
