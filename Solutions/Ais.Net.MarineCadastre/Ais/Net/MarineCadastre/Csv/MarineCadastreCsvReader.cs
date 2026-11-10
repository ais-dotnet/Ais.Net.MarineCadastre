// <copyright file="MarineCadastreCsvReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Runtime.CompilerServices;

using Ais.Net.Models;

namespace Ais.Net.MarineCadastre.Csv;

/// <summary>
/// Reads a MarineCadastre AIS vessel-traffic CSV file and returns Ais.Net.Models messages. Each row
/// gives one position report. When the static columns of a vessel first appear or change, the reader
/// also returns a static-data message. The <see cref="AisMessageBase.Timestamp"/> of each message is
/// the <c>base_date_time</c> of the row.
/// </summary>
/// <remarks>
/// The reader streams the file. It holds one block of the file and one position report at a time. It
/// also keeps an 8-byte signature for each vessel, to find static data that it already returned.
/// MarineCadastre publishes the archives at <see href="https://hub.marinecadastre.gov/pages/vesseltraffic"/>.
/// </remarks>
public static class MarineCadastreCsvReader
{
    /// <summary>
    /// Reads messages from a CSV stream.
    /// </summary>
    /// <param name="stream">The CSV content. The header line is first.</param>
    /// <param name="options">The reader settings. If you do not give options, the reader uses <see cref="MarineCadastreCsvReaderOptions.Default"/>.</param>
    /// <returns>The messages, in file order.</returns>
    public static IEnumerable<AisMessageBase> Read(Stream stream, MarineCadastreCsvReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return ReadCore(stream, options ?? MarineCadastreCsvReaderOptions.Default);
    }

    /// <summary>
    /// Reads messages from a CSV file.
    /// </summary>
    /// <param name="path">The path of the CSV file.</param>
    /// <param name="options">The reader settings. If you do not give options, the reader uses <see cref="MarineCadastreCsvReaderOptions.Default"/>.</param>
    /// <returns>The messages, in file order.</returns>
    public static IEnumerable<AisMessageBase> ReadFile(string path, MarineCadastreCsvReaderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ReadFileCore(path, options ?? MarineCadastreCsvReaderOptions.Default);
    }

    /// <summary>
    /// Reads messages from a CSV stream asynchronously.
    /// </summary>
    /// <param name="stream">The CSV content. The header line is first.</param>
    /// <param name="options">The reader settings. If you do not give options, the reader uses <see cref="MarineCadastreCsvReaderOptions.Default"/>.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The messages, in file order.</returns>
    public static IAsyncEnumerable<AisMessageBase> ReadAsync(Stream stream, MarineCadastreCsvReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return ReadAsyncCore(stream, options ?? MarineCadastreCsvReaderOptions.Default, cancellationToken);
    }

    /// <summary>
    /// Reads messages from a CSV file asynchronously.
    /// </summary>
    /// <param name="path">The path of the CSV file.</param>
    /// <param name="options">The reader settings. If you do not give options, the reader uses <see cref="MarineCadastreCsvReaderOptions.Default"/>.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The messages, in file order.</returns>
    public static IAsyncEnumerable<AisMessageBase> ReadFileAsync(string path, MarineCadastreCsvReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ReadFileAsyncCore(path, options ?? MarineCadastreCsvReaderOptions.Default, cancellationToken);
    }

    private static FileStream OpenFile(string path, bool isAsync) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, isAsync ? FileOptions.Asynchronous | FileOptions.SequentialScan : FileOptions.SequentialScan);

    private static IEnumerable<AisMessageBase> ReadFileCore(string path, MarineCadastreCsvReaderOptions options)
    {
        using FileStream stream = OpenFile(path, isAsync: false);
        foreach (AisMessageBase message in ReadCore(stream, options))
        {
            yield return message;
        }
    }

    private static async IAsyncEnumerable<AisMessageBase> ReadFileAsyncCore(string path, MarineCadastreCsvReaderOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenFile(path, isAsync: true);
        await foreach (AisMessageBase message in ReadAsyncCore(stream, options, cancellationToken).ConfigureAwait(false))
        {
            yield return message;
        }
    }

    private static IEnumerable<AisMessageBase> ReadCore(Stream stream, MarineCadastreCsvReaderOptions options)
    {
        MarineCadastreCsvParser parser = new(options);
        LineBuffer buffer = new(stream);

        while (true)
        {
            AisMessageBase? position;
            AisMessageBase? staticData;

            if (buffer.TryTakeLine(out int offset, out int length))
            {
                parser.ParseLine(buffer.Slice(offset, length), out position, out staticData);
            }
            else if (buffer.Fill())
            {
                continue;
            }
            else if (buffer.TryTakeRemainder(out offset, out length))
            {
                parser.ParseLine(buffer.Slice(offset, length), out position, out staticData);
            }
            else
            {
                yield break;
            }

            if (position is not null)
            {
                yield return position;
            }

            if (staticData is not null)
            {
                yield return staticData;
            }
        }
    }

    private static async IAsyncEnumerable<AisMessageBase> ReadAsyncCore(Stream stream, MarineCadastreCsvReaderOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        MarineCadastreCsvParser parser = new(options);
        LineBuffer buffer = new(stream);

        while (true)
        {
            // A block holds thousands of rows, so checking only when the buffer is refilled would keep
            // parsing and yielding them long after the caller cancelled. The second check, below, is
            // for a row that yields two messages: the caller may cancel on receiving the first.
            cancellationToken.ThrowIfCancellationRequested();

            AisMessageBase? position;
            AisMessageBase? staticData;

            if (buffer.TryTakeLine(out int offset, out int length))
            {
                parser.ParseLine(buffer.Slice(offset, length), out position, out staticData);
            }
            else if (await buffer.FillAsync(cancellationToken).ConfigureAwait(false))
            {
                continue;
            }
            else if (buffer.TryTakeRemainder(out offset, out length))
            {
                parser.ParseLine(buffer.Slice(offset, length), out position, out staticData);
            }
            else
            {
                yield break;
            }

            if (position is not null)
            {
                yield return position;
            }

            if (staticData is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return staticData;
            }
        }
    }
}
