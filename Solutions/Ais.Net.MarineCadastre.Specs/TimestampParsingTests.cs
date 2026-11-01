using System.Globalization;
using System.Text;

using Shouldly;

namespace Ais.Net.MarineCadastre.Specs;

[TestClass]
public class TimestampParsingTests
{
    [TestMethod]
    [DataRow("2026-01-01T00:09:52.0Z")]
    [DataRow("2026-01-01T00:09:52Z")]
    [DataRow("2026-01-01T00:09:52")]
    [DataRow("2026-01-01 00:09:52")]
    [DataRow("2026-01-31T23:59:59.999Z")]
    [DataRow("2026-01-31T23:59:59.1234567Z")]
    [DataRow("2024-02-29T12:00:00Z")]
    [DataRow("0001-01-01T00:00:00Z")]
    [DataRow("9999-12-31T23:59:59Z")]
    public void FastPathAgreesWithTheFrameworkParser(string text)
    {
        bool parsed = TrackTable.TryParseTimestamp(Encoding.UTF8.GetBytes(text), out DateTimeOffset actual);

        parsed.ShouldBeTrue();
        DateTimeOffset expected = DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        actual.ShouldBe(expected);
        actual.Offset.ShouldBe(TimeSpan.Zero);
    }

    [TestMethod]
    public void FractionDigitsBeyondTicksAreDropped()
    {
        TrackTable.TryParseTimestamp("2026-01-31T23:59:59.12345678912Z"u8, out DateTimeOffset actual).ShouldBeTrue();

        actual.ShouldBe(new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(1234567));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("2026-01-01")]
    [DataRow("2026-13-01T00:00:00Z")]
    [DataRow("2026-02-29T00:00:00Z")]
    [DataRow("2026-01-01T24:00:00Z")]
    [DataRow("2026-01-01T00:60:00Z")]
    [DataRow("2026-01-01T00:00:60Z")]
    [DataRow("2026-01-01T00:00:00.Z")]
    [DataRow("2026-01-01T00:00:00+01:00")]
    [DataRow("2026-01-01T00:00:00Zx")]
    [DataRow("2026/01/01T00:00:00Z")]
    [DataRow("2026-01-0aT00:00:00Z")]
    [DataRow("01/01/2026 00:00:00")]
    public void ShapesOutsideTheFastPathAreDeclined(string text)
    {
        TrackTable.TryParseTimestamp(Encoding.UTF8.GetBytes(text), out _).ShouldBeFalse();
    }
}
