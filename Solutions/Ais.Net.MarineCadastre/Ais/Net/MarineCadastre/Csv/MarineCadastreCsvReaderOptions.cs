// <copyright file="MarineCadastreCsvReaderOptions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre.Csv;

/// <summary>
/// Settings for <see cref="MarineCadastreCsvReader"/>.
/// </summary>
public sealed record MarineCadastreCsvReaderOptions
{
    /// <summary>
    /// Gets the options that each reader uses if you do not give options.
    /// </summary>
    public static MarineCadastreCsvReaderOptions Default { get; } = new();

    /// <summary>
    /// Gets a value that is <see langword="true"/> if the reader returns static-data messages. The
    /// default is <see langword="true"/>. The reader then returns the static columns of a vessel as a
    /// message type 5 (Class A). For Class B, it returns them as message types 19 and 24 part 1. It does
    /// this the first time that it sees the vessel, and each time that those columns change. If
    /// <see langword="false"/>, the reader returns only position reports.
    /// </summary>
    public bool EmitStaticData { get; init; } = true;

    /// <summary>
    /// Gets the column that supplies the ship type. The default is
    /// <see cref="ShipTypeSource.VesselTypeThenCargo"/>.
    /// </summary>
    public ShipTypeSource ShipTypeSource { get; init; } = ShipTypeSource.VesselTypeThenCargo;

    /// <summary>
    /// Gets the AIS message type (1, 2 or 3) that the reader gives to Class A position rows. The CSV
    /// does not say which of the three types a row came from. Thus the reader uses one value for all
    /// the rows. The default is 1. The value must be 1, 2 or 3.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">This exception occurs if the value is not 1, 2 or 3.</exception>
    public int ClassAMessageType
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, nameof(this.ClassAMessageType));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 3, nameof(this.ClassAMessageType));
            field = value;
        }
    } = 1;

    /// <summary>
    /// Gets a callback that the reader calls for each data row that it could not parse and dropped.
    /// The callback runs on the reader thread, before the reader reads the next row. If it is
    /// <see langword="null"/> (the default), the reader drops those rows without a report.
    /// </summary>
    /// <remarks>
    /// The <see cref="SkippedCsvRow"/> argument is valid only during the call. If the callback throws
    /// an exception, the read stops with that exception. Thus a callback that throws makes the read
    /// strict. The reader never reports the header or blank lines. A row with a missing or
    /// out-of-range position is not a skipped row. The reader returns it with a <see langword="null"/>
    /// position.
    /// </remarks>
    public Action<SkippedCsvRow>? OnRowSkipped { get; init; }
}
