// <copyright file="CsvField.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre.Csv;

/// <summary>
/// The location of one field within a CSV record.
/// </summary>
/// <param name="Start">Offset of the first byte.</param>
/// <param name="Length">Number of bytes.</param>
internal readonly record struct CsvField(int Start, int Length)
{
    /// <summary>Gets the bytes of this field within <paramref name="line"/>.</summary>
    public ReadOnlySpan<byte> In(ReadOnlySpan<byte> line) => line.Slice(this.Start, this.Length);
}
