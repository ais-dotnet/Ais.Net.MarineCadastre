// <copyright file="CsvRowSkipReason.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre.Csv;

/// <summary>
/// Why <see cref="MarineCadastreCsvReader"/> could not parse a data row and dropped it.
/// </summary>
public enum CsvRowSkipReason
{
    /// <summary>
    /// The row has fewer fields than the required columns (<c>mmsi</c>, <c>base_date_time</c>,
    /// <c>longitude</c> and <c>latitude</c>) need.
    /// </summary>
    TooFewFields,

    /// <summary>
    /// The <c>mmsi</c> field is empty or does not start with an unsigned integer.
    /// </summary>
    InvalidMmsi,

    /// <summary>
    /// The <c>base_date_time</c> field is not a readable date and time.
    /// </summary>
    InvalidTimestamp,
}
