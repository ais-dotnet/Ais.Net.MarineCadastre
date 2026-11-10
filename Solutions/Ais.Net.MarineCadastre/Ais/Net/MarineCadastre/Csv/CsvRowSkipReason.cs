// <copyright file="CsvRowSkipReason.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre.Csv;

/// <summary>
/// The reason that <see cref="MarineCadastreCsvReader"/> could not parse a data row and dropped it.
/// </summary>
public enum CsvRowSkipReason
{
    /// <summary>
    /// The row does not have enough fields to include the required columns (<c>mmsi</c>,
    /// <c>base_date_time</c>, <c>longitude</c> and <c>latitude</c>).
    /// </summary>
    TooFewFields,

    /// <summary>
    /// The <c>mmsi</c> field is empty, or it is not an unsigned integer.
    /// </summary>
    InvalidMmsi,

    /// <summary>
    /// The reader cannot read the <c>base_date_time</c> field as a date and time. This includes a value
    /// with a UTC offset, because the reader reads only UTC times.
    /// </summary>
    InvalidTimestamp,
}
