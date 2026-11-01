using System.Text;

namespace Ais.Net.MarineCadastre.Benchmarks;

/// <summary>
/// Supplies the CSV bytes the benchmarks read, held in memory so that disk speed is not measured.
/// </summary>
public static class CsvInput
{
    /// <summary>
    /// Environment variable naming a real MarineCadastre daily CSV. When unset, the bundled 10,000-row
    /// extract is cycled to the requested length (its vessels repeat, so static-data de-duplication
    /// engages sooner than on a real file).
    /// </summary>
    public const string FileVariable = "AIS_NET_MARINECADASTRE_CSV";

    public static byte[] Load(int rowCount)
    {
        string? path = Environment.GetEnvironmentVariable(FileVariable);
        bool bundled = string.IsNullOrEmpty(path) || !File.Exists(path);
        if (bundled)
        {
            path = Path.Combine(AppContext.BaseDirectory, "samples", "marinecadastre-sample.csv");
        }

        List<string> rows = [];
        string? header = null;
        foreach (string line in File.ReadLines(path!))
        {
            if (header is null)
            {
                header = line;
                continue;
            }

            if (line.Length == 0)
            {
                continue;
            }

            rows.Add(line);
            if (rows.Count == rowCount)
            {
                break;
            }
        }

        if (header is null || rows.Count == 0)
        {
            throw new InvalidOperationException($"No rows found in '{path}'.");
        }

        StringBuilder text = new(rowCount * 110);
        text.Append(header).Append('\n');
        for (int i = 0; i < rowCount; i++)
        {
            text.Append(rows[i % rows.Count]).Append('\n');
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }
}
