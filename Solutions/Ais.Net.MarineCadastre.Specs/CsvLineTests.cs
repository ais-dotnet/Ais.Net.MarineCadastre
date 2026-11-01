using System.Text;

using Shouldly;

namespace Ais.Net.MarineCadastre.Specs;

[TestClass]
public class CsvLineTests
{
    [TestMethod]
    [DataRow("a,b,c", "a|b|c")]
    [DataRow("a,,c", "a||c")]
    [DataRow("a,b,", "a|b|")]
    [DataRow(",", "|")]
    [DataRow("", "")]
    [DataRow("\"a,b\",c", "a,b|c")]
    [DataRow("\"say \"\"hi\"\"\",c", "say \"hi\"|c")]
    [DataRow("a,\"\",c", "a||c")]
    [DataRow("a,\"unterminated,c", "a|unterminated,c")]
    public void SplitsFieldsAndUnescapesQuotes(string input, string expected)
    {
        byte[] line = Encoding.UTF8.GetBytes(input);
        Span<CsvField> fields = stackalloc CsvField[8];

        int count = CsvLine.Split(line, fields);

        List<string> actual = [];
        for (int i = 0; i < count; i++)
        {
            actual.Add(Encoding.UTF8.GetString(fields[i].In(line)));
        }

        string.Join('|', actual).ShouldBe(expected);
    }

    [TestMethod]
    public void SurplusFieldsAreCountedButDropped()
    {
        byte[] line = Encoding.UTF8.GetBytes("a,b,c,d");
        Span<CsvField> fields = stackalloc CsvField[2];

        CsvLine.Split(line, fields).ShouldBe(4);
        Encoding.UTF8.GetString(fields[1].In(line)).ShouldBe("b");
    }
}
