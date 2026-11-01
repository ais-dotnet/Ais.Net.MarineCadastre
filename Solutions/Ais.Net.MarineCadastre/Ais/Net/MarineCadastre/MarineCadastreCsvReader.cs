// <copyright file="MarineCadastreCsvReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Runtime.CompilerServices;

using Ais.Net.Models;

namespace Ais.Net.MarineCadastre;

/// <summary>
/// Reads a MarineCadastre AIS vessel-traffic CSV file and yields Ais.Net.Models messages, one position
/// report per row plus a static-data message whenever a vessel's static columns first appear or change.
/// Every message has its <see cref="AisMessageBase.Timestamp"/> set from the row's <c>base_date_time</c>.
/// </summary>
/// <remarks>
/// The reader streams: it holds one block of the file and one position report at a time, plus a
/// per-vessel 8-byte signature for static-data de-duplication. The archives are published at
/// <see href="https://hub.marinecadastre.gov/pages/vesseltraffic"/>.
/// </remarks>
public static class MarineCadastreCsvReader
{
    /// <summary>
    /// Reads messages from a CSV stream.
    /// </summary>
    /// <param name="stream">The CSV content, header line first.</param>
    /// <param name="options">Reader settings; <see cref="MarineCadastreReaderOptions.Default"/> when omitted.</param>
    /// <returns>The messages, in file order.</returns>
    public static IEnumerable<AisMessageBase> Read(Stream stream, MarineCadastreReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return ReadCore(stream, options ?? MarineCadastreReaderOptions.Default);
    }

    /// <summary>
    /// Reads messages from a CSV file.
    /// </summary>
    /// <param name="path">Path of the CSV file.</param>
    /// <param name="options">Reader settings; <see cref="MarineCadastreReaderOptions.Default"/> when omitted.</param>
    /// <returns>The messages, in file order.</returns>
    public static IEnumerable<AisMessageBase> ReadFile(string path, MarineCadastreReaderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ReadFileCore(path, options ?? MarineCadastreReaderOptions.Default);
    }

    /// <summary>
    /// Reads messages from a CSV stream asynchronously.
    /// </summary>
    /// <param name="stream">The CSV content, header line first.</param>
    /// <param name="options">Reader settings; <see cref="MarineCadastreReaderOptions.Default"/> when omitted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The messages, in file order.</returns>
    public static IAsyncEnumerable<AisMessageBase> ReadAsync(Stream stream, MarineCadastreReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return ReadAsyncCore(stream, options ?? MarineCadastreReaderOptions.Default, cancellationToken);
    }

    /// <summary>
    /// Reads messages from a CSV file asynchronously.
    /// </summary>
    /// <param name="path">Path of the CSV file.</param>
    /// <param name="options">Reader settings; <see cref="MarineCadastreReaderOptions.Default"/> when omitted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The messages, in file order.</returns>
    public static IAsyncEnumerable<AisMessageBase> ReadFileAsync(string path, MarineCadastreReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ReadFileAsyncCore(path, options ?? MarineCadastreReaderOptions.Default, cancellationToken);
    }

    private static FileStream OpenFile(string path, bool isAsync) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, isAsync ? FileOptions.Asynchronous | FileOptions.SequentialScan : FileOptions.SequentialScan);

    private static IEnumerable<AisMessageBase> ReadFileCore(string path, MarineCadastreReaderOptions options)
    {
        using FileStream stream = OpenFile(path, isAsync: false);
        foreach (AisMessageBase message in ReadCore(stream, options))
        {
            yield return message;
        }
    }

    private static async IAsyncEnumerable<AisMessageBase> ReadFileAsyncCore(string path, MarineCadastreReaderOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenFile(path, isAsync: true);
        await foreach (AisMessageBase message in ReadAsyncCore(stream, options, cancellationToken).ConfigureAwait(false))
        {
            yield return message;
        }
    }

    private static IEnumerable<AisMessageBase> ReadCore(Stream stream, MarineCadastreReaderOptions options)
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

    private static async IAsyncEnumerable<AisMessageBase> ReadAsyncCore(Stream stream, MarineCadastreReaderOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
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
                yield return staticData;
            }
        }
    }
}
