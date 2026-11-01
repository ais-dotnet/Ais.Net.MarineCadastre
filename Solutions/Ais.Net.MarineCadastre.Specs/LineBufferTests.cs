using System.Text;

using Shouldly;

namespace Ais.Net.MarineCadastre.Specs;

[TestClass]
public class LineBufferTests
{
    [TestMethod]
    public void SplitsOnLfAndDropsCr()
    {
        ReadAll("a\nb\r\nc\n").ShouldBe(["a", "b", "c"]);
    }

    [TestMethod]
    public void FinalLineWithoutNewlineIsReturned()
    {
        ReadAll("a\nb").ShouldBe(["a", "b"]);
    }

    [TestMethod]
    public void BlankLinesAreReturnedAsEmpty()
    {
        ReadAll("a\n\nb\n").ShouldBe(["a", "", "b"]);
    }

    [TestMethod]
    public void ByteOrderMarkIsSkippedOnTheFirstLineOnly()
    {
        byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("a\nb\n")];

        ReadAll(bytes).ShouldBe(["a", "b"]);
    }

    [TestMethod]
    public void LinesLongerThanTheBufferGrowIt()
    {
        string longLine = new('x', 1000);

        ReadAll("short\n" + longLine + "\n" + "tail\n", capacity: 16).ShouldBe(["short", longLine, "tail"]);
    }

    [TestMethod]
    public void LinesStraddlingAFillBoundaryAreJoined()
    {
        // Many 7-byte lines through a 32-byte buffer: every fill splits a line somewhere.
        string[] lines = [.. Enumerable.Range(0, 200).Select(i => i.ToString("D6"))];

        ReadAll(string.Join('\n', lines) + "\n", capacity: 32).ShouldBe(lines);
    }

    [TestMethod]
    public async Task FillAsyncBehavesLikeFill()
    {
        string[] lines = [.. Enumerable.Range(0, 200).Select(i => i.ToString("D6"))];
        LineBuffer buffer = new(new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', lines))), initialCapacity: 32);

        List<string> actual = [];
        while (true)
        {
            if (buffer.TryTakeLine(out int offset, out int length))
            {
                actual.Add(Encoding.UTF8.GetString(buffer.Slice(offset, length)));
            }
            else if (!await buffer.FillAsync(CancellationToken.None))
            {
                if (buffer.TryTakeRemainder(out offset, out length))
                {
                    actual.Add(Encoding.UTF8.GetString(buffer.Slice(offset, length)));
                }

                break;
            }
        }

        actual.ShouldBe(lines);
    }

    private static List<string> ReadAll(string text, int capacity = LineBuffer.DefaultCapacity) =>
        ReadAll(Encoding.UTF8.GetBytes(text), capacity);

    private static List<string> ReadAll(byte[] bytes, int capacity = LineBuffer.DefaultCapacity)
    {
        LineBuffer buffer = new(new MemoryStream(bytes), capacity);
        List<string> lines = [];
        while (true)
        {
            if (buffer.TryTakeLine(out int offset, out int length))
            {
                lines.Add(Encoding.UTF8.GetString(buffer.Slice(offset, length)));
            }
            else if (!buffer.Fill())
            {
                if (buffer.TryTakeRemainder(out offset, out length))
                {
                    lines.Add(Encoding.UTF8.GetString(buffer.Slice(offset, length)));
                }

                return lines;
            }
        }
    }
}
