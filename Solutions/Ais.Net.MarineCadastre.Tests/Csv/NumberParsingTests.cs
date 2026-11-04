using System.Globalization;
using System.Text;

using Ais.Net.MarineCadastre.Csv;

using Shouldly;

namespace Ais.Net.MarineCadastre.Tests.Csv;

[TestClass]
public class NumberParsingTests
{
    [TestMethod]
    [DataRow("-122.50573")]
    [DataRow("36.92122")]
    [DataRow("0.0")]
    [DataRow("0")]
    [DataRow("14")]
    [DataRow("359.9")]
    [DataRow("-0.00001")]
    [DataRow("0.1")]
    [DataRow("0.3")]
    [DataRow("102.3")]
    [DataRow("123456789012345")]
    [DataRow("0.123456789012345")]
    [DataRow("1234567890.123456")]
    [DataRow("12345678901234567")]
    [DataRow("0.0000000000000000000001")]
    [DataRow("1e5")]
    [DataRow("-1.5E-3")]
    [DataRow("+2.5")]
    [DataRow("12.")]
    [DataRow(".5")]
    [DataRow("-.5")]
    [DataRow("007")]
    [DataRow("-0.0")]
    public void FastPathAgreesWithFrameworkParser(string text)
    {
        bool parsed = MarineCadastreCsvParser.TryParseDouble(Encoding.ASCII.GetBytes(text), out double actual);

        parsed.ShouldBeTrue();
        double expected = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        BitConverter.DoubleToInt64Bits(actual).ShouldBe(BitConverter.DoubleToInt64Bits(expected), $"{text}: {actual:R} != {expected:R}");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("-")]
    [DataRow(".")]
    [DataRow("1.2.3")]
    [DataRow("abc")]
    [DataRow("12a")]
    [DataRow(" 12")]
    [DataRow("12 ")]
    public void RejectsWhatIsNotANumber(string text)
    {
        MarineCadastreCsvParser.TryParseDouble(Encoding.ASCII.GetBytes(text), out _).ShouldBeFalse();
    }

    [TestMethod]
    public void TypicalRowsAgreeWithFrameworkParser()
    {
        // Every numeric cell of a handful of rows shaped like MarineCadastre data, including the rounding-sensitive five-decimal coordinates.
        string[] rows =
        [
            "999000003,2026-01-01 00:00:03,-76.31518,36.84956,0.0,149.6,19,EXAMPLE VESSEL,,ZZZ0003,52,0,20,8,2.3,52,A",
            "999000006,2026-01-01 00:00:08,-79.61369,25.33373,,,168,SYNTHETIC CARRIER,IMO7000002,ZZ0006,71,0,170,28,6.9,71,A",
            "999000007,2026-01-01 00:00:03,-125.92361,49.17016,7.1,95.1,95,PLACEHOLDER SHIP,,,79,0,15,10,2.0,79,A",
            "999000008,2026-01-01 00:00:07,-122.39129,47.62895,0.0,,,NOTREAL,,,37,,15,4,,,B",
        ];

        foreach (string cell in rows.SelectMany(r => r.Split(',')).Where(c => c.Length > 0 && char.IsAsciiDigit(c[^1]) && !c.Contains(' ') && !c.Contains(':')))
        {
            if (!double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out double expected))
            {
                continue;
            }

            MarineCadastreCsvParser.TryParseDouble(Encoding.ASCII.GetBytes(cell), out double actual).ShouldBeTrue(cell);
            BitConverter.DoubleToInt64Bits(actual).ShouldBe(BitConverter.DoubleToInt64Bits(expected), cell);
        }
    }
}
