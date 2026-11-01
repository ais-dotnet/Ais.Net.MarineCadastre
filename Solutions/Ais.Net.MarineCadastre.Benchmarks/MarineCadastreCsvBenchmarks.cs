using Ais.Net.MarineCadastre.Csv;
using Ais.Net.Models;

using BenchmarkDotNet.Attributes;

namespace Ais.Net.MarineCadastre.Benchmarks;

/// <summary>
/// Throughput and allocation of reading MarineCadastre CSV rows into Ais.Net.Models messages, in
/// layers: splitting lines, splitting fields, parsing rows without building messages, and the public
/// reader with and without static-data messages. Allocation per row is the figure to watch: the floor
/// is one position record per row.
/// </summary>
[MemoryDiagnoser]
public class MarineCadastreCsvBenchmarks
{
    private static readonly MarineCadastreReaderOptions PositionsOnly = new() { EmitStaticData = false };

    private byte[] csv = [];

    [Params(100_000)]
    public int RowCount { get; set; }

    [GlobalSetup]
    public void Setup() => this.csv = CsvInput.Load(this.RowCount);

    [Benchmark(Baseline = true)]
    public int SplitLinesOnly()
    {
        LineBuffer buffer = new(new MemoryStream(this.csv));
        int lines = 0;
        while (true)
        {
            if (buffer.TryTakeLine(out _, out _))
            {
                lines++;
            }
            else if (!buffer.Fill())
            {
                if (buffer.TryTakeRemainder(out _, out _))
                {
                    lines++;
                }

                return lines;
            }
        }
    }

    [Benchmark]
    public int SplitFieldsOnly()
    {
        LineBuffer buffer = new(new MemoryStream(this.csv));
        Span<CsvField> fields = stackalloc CsvField[MarineCadastreCsvParser.MaxFields];
        int total = 0;
        while (true)
        {
            if (buffer.TryTakeLine(out int offset, out int length))
            {
                total += CsvLine.Split(buffer.Slice(offset, length), fields);
            }
            else if (!buffer.Fill())
            {
                return total;
            }
        }
    }

    [Benchmark]
    public int ReadPositionsOnly()
    {
        int count = 0;
        foreach (AisMessageBase message in MarineCadastreCsvReader.Read(new MemoryStream(this.csv), PositionsOnly))
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public int ReadWithStaticData()
    {
        int count = 0;
        foreach (AisMessageBase message in MarineCadastreCsvReader.Read(new MemoryStream(this.csv)))
        {
            count++;
        }

        return count;
    }

    [Benchmark]
    public async Task<int> ReadAsyncWithStaticData()
    {
        int count = 0;
        await foreach (AisMessageBase message in MarineCadastreCsvReader.ReadAsync(new MemoryStream(this.csv)))
        {
            count++;
        }

        return count;
    }
}
