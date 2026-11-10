// <copyright file="SkippedCsvRow.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre.Csv;

/// <summary>
/// A data row that <see cref="MarineCadastreCsvReader"/> could not parse and dropped. The reader passes
/// it to <see cref="MarineCadastreCsvReaderOptions.OnRowSkipped"/>.
/// </summary>
/// <remarks>
/// This is a <see langword="ref"/> struct because <see cref="Line"/> points into the buffer of the
/// reader. The reader uses the same buffer again for the next line. Before the callback returns, copy
/// the data that you must keep.
/// </remarks>
public readonly ref struct SkippedCsvRow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SkippedCsvRow"/> struct.
    /// </summary>
    /// <param name="lineNumber">The 1-based physical line number of the row.</param>
    /// <param name="reason">The reason that the reader skipped the row.</param>
    /// <param name="line">The UTF-8 bytes of the row, without the line ending.</param>
    public SkippedCsvRow(long lineNumber, CsvRowSkipReason reason, ReadOnlySpan<byte> line)
    {
        this.LineNumber = lineNumber;
        this.Reason = reason;
        this.Line = line;
    }

    /// <summary>
    /// Gets the 1-based physical line number of the row. The header is line 1. Blank lines count.
    /// </summary>
    public long LineNumber { get; }

    /// <summary>
    /// Gets the reason that the reader skipped the row.
    /// </summary>
    public CsvRowSkipReason Reason { get; }

    /// <summary>
    /// Gets the UTF-8 bytes of the row, without the line ending. The span is valid only during the
    /// callback, because the reader uses its buffer again. The reader possibly already removed the
    /// quote characters from quoted fields, in place.
    /// </summary>
    public ReadOnlySpan<byte> Line { get; }
}
