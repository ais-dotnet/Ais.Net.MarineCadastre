using System.Text;

using ZstdSharp;

namespace Ais.Net.MarineCadastre.Benchmarks;

/// <summary>
/// Supplies the CSV bytes the benchmarks read, held in memory so that disk speed is not measured.
/// </summary>
/// <remarks>
/// No MarineCadastre data is stored in the repository. When <see cref="FileVariable"/> is unset, the
/// first <see cref="SampleRows"/> rows of one published daily file are downloaded from NOAA on first
/// use and cached under the user's local application data folder, then cycled to the requested length
/// (the sample's vessels repeat, so static-data de-duplication engages sooner than on a real file).
/// Only the start of the file is fetched: the download is decompressed as it arrives and stopped once
/// enough rows have been read.
/// </remarks>
public static class CsvInput
{
    /// <summary>
    /// Environment variable naming a real MarineCadastre daily CSV to read instead of the downloaded sample.
    /// </summary>
    public const string FileVariable = "AIS_NET_MARINECADASTRE_CSV";

    /// <summary>
    /// The published daily file the sample is taken from: 1 January 2026, Zstandard-compressed CSV.
    /// </summary>
    public const string SampleUrl = "https://noaaocm.blob.core.windows.net/ais/csv2/csv2026/ais-2026-01-01.csv.zst";

    /// <summary>
    /// How many data rows the downloaded sample holds.
    /// </summary>
    public const int SampleRows = 10_000;

    public static byte[] Load(int rowCount)
    {
        string? path = Environment.GetEnvironmentVariable(FileVariable);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            path = EnsureSample();
        }

        List<string> rows = [];
        string? header = null;
        foreach (string line in File.ReadLines(path))
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

    private static string EnsureSample()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            "Ais.Net.MarineCadastre",
            "samples");
        string path = Path.Combine(folder, $"ais-2026-01-01-first-{SampleRows}.csv");
        if (File.Exists(path))
        {
            return path;
        }

        Directory.CreateDirectory(folder);
        string partial = path + "." + Environment.ProcessId + ".tmp";
        try
        {
            using (HttpClient client = new() { Timeout = TimeSpan.FromMinutes(2) })
            using (HttpResponseMessage response = client.GetAsync(SampleUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                using Stream compressed = response.Content.ReadAsStream();
                using DecompressionStream csv = new(compressed);
                using StreamReader reader = new(csv, Encoding.UTF8);
                using StreamWriter writer = new(partial, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                // Header plus SampleRows data rows; disposing the response then abandons the rest of the file.
                for (int written = 0; written <= SampleRows && reader.ReadLine() is { } line; written++)
                {
                    writer.Write(line);
                    writer.Write('\n');
                }
            }

            File.Move(partial, path, overwrite: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or ZstdException)
        {
            File.Delete(partial);
            throw new InvalidOperationException(
                $"Could not download the benchmark sample from {SampleUrl}. Check the network connection, or set {FileVariable} to a MarineCadastre daily CSV on disk.",
                ex);
        }

        return path;
    }
}
