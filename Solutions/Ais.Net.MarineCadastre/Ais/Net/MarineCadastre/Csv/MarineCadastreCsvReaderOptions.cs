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
    /// Gets the options every reader uses unless told otherwise.
    /// </summary>
    public static MarineCadastreCsvReaderOptions Default { get; } = new();

    /// <summary>
    /// Gets a value indicating whether static-data messages are emitted. When <see langword="true"/>
    /// (the default) a vessel's static columns are emitted as a message type 5 (Class A) or message
    /// types 19 and 24 part 1 (Class B) the first time the vessel is seen and again whenever those
    /// columns change. When <see langword="false"/> only position reports are emitted.
    /// </summary>
    public bool EmitStaticData { get; init; } = true;

    /// <summary>
    /// Gets the column that supplies the ship type. Defaults to
    /// <see cref="ShipTypeSource.VesselTypeThenCargo"/>.
    /// </summary>
    public ShipTypeSource ShipTypeSource { get; init; } = ShipTypeSource.VesselTypeThenCargo;

    /// <summary>
    /// Gets the AIS message type (1, 2 or 3) reported for Class A position rows. The CSV does not say
    /// which of the three a row came from, so one value is used for all. Defaults to 1. Must be 1, 2
    /// or 3.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is not 1, 2 or 3.</exception>
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
    /// Gets a callback invoked for each data row the reader could not parse and dropped. It runs on
    /// the reading thread, before the next row is read. When <see langword="null"/> (the default)
    /// such rows are dropped silently.
    /// </summary>
    /// <remarks>
    /// The <see cref="SkippedCsvRow"/> argument is valid only during the call. An exception thrown by
    /// the callback ends the read, so throwing makes the read strict. The header and blank lines are
    /// never reported, and a row whose position is missing or out of range is not skipped: it is
    /// emitted with a <see langword="null"/> position.
    /// </remarks>
    public Action<SkippedCsvRow>? OnRowSkipped { get; init; }
}
