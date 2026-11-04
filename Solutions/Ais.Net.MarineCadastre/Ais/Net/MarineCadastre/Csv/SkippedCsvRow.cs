// <copyright file="SkippedCsvRow.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre.Csv;

/// <summary>
/// Describes a data row that <see cref="MarineCadastreCsvReader"/> could not parse and dropped. It is
/// passed to <see cref="MarineCadastreCsvReaderOptions.OnRowSkipped"/>.
/// </summary>
/// <remarks>
/// This is a <see langword="ref"/> struct because <see cref="Line"/> points into the reader's buffer,
/// which the reader reuses for the next line. Copy anything you need to keep before the callback
/// returns.
/// </remarks>
public readonly ref struct SkippedCsvRow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SkippedCsvRow"/> struct.
    /// </summary>
    /// <param name="lineNumber">The 1-based physical line number of the row.</param>
    /// <param name="reason">Why the row was skipped.</param>
    /// <param name="line">The UTF-8 bytes of the row, without the line ending.</param>
    public SkippedCsvRow(long lineNumber, CsvRowSkipReason reason, ReadOnlySpan<byte> line)
    {
        this.LineNumber = lineNumber;
        this.Reason = reason;
        this.Line = line;
    }

    /// <summary>
    /// Gets the 1-based physical line number of the row. The header is line 1, and blank lines are
    /// counted.
    /// </summary>
    public long LineNumber { get; }

    /// <summary>
    /// Gets the reason the row was skipped.
    /// </summary>
    public CsvRowSkipReason Reason { get; }

    /// <summary>
    /// Gets the UTF-8 bytes of the row, without the line ending. The span is valid only during the
    /// callback, because the reader reuses its buffer. Quoted fields may already have been unescaped
    /// in place.
    /// </summary>
    public ReadOnlySpan<byte> Line { get; }
}
