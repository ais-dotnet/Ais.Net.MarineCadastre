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
    [DataRow("368164470,2026-01-01 00:00:03,-76.31518,36.84956,0.0,149.6,19,EASTERN BRANCH,,WDL7894,52,0,20,8,2.3,52,A", "368164470|2026-01-01 00:00:03|-76.31518|36.84956|0.0|149.6|19|EASTERN BRANCH||WDL7894|52|0|20|8|2.3|52|A")]
    [DataRow("368025040,2026-01-01 00:00:04,-122.50573,48.75658,0.0,,,TRUTINA,,WDJ9334,37,,13,4,,,B", "368025040|2026-01-01 00:00:04|-122.50573|48.75658|0.0|||TRUTINA||WDJ9334|37||13|4|||B")]
    [DataRow("aaaaaaaaaaaaaaa,", "aaaaaaaaaaaaaaa|")]
    [DataRow(",aaaaaaaaaaaaaa,", "|aaaaaaaaaaaaaa|")]
    [DataRow("aaaaaaaaaaaaaaaa", "aaaaaaaaaaaaaaaa")]
    [DataRow("aaaaaaaaaaaaaaa,bbbbbbbbbbbbbbb,c", "aaaaaaaaaaaaaaa|bbbbbbbbbbbbbbb|c")]
    [DataRow("a,b,c,d,e,f,g,h,i,j,k,l,m,n,o,p,q,r,s,t,u,v,w,x,y,z,0,1,2,3,4,5,6,7,", "a|b|c|d|e|f|g|h|i|j|k|l|m|n|o|p|q|r|s|t|u|v|w|x|y|z|0|1|2|3|4|5|6|7|")]
    [DataRow("368164470,2026-01-01 00:00:03,-76.31518,36.84956,0.0,149.6,19,\"BRANCH, EASTERN\",,WDL7894,52,0,20,8,2.3,52,A", "368164470|2026-01-01 00:00:03|-76.31518|36.84956|0.0|149.6|19|BRANCH, EASTERN||WDL7894|52|0|20|8|2.3|52|A")]
    public void SplitsFieldsAndUnescapesQuotes(string input, string expected)
    {
        byte[] line = Encoding.UTF8.GetBytes(input);
        Span<CsvField> fields = stackalloc CsvField[64];

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
