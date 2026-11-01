// <copyright file="MarineCadastreReaderOptions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre.Csv;

/// <summary>
/// Settings for <see cref="MarineCadastreCsvReader"/>.
/// </summary>
public sealed record MarineCadastreReaderOptions
{
    /// <summary>
    /// Gets the options every reader uses unless told otherwise.
    /// </summary>
    public static MarineCadastreReaderOptions Default { get; } = new();

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
    /// which of the three a row came from, so one value is used for all. Defaults to 1.
    /// </summary>
    public int ClassAMessageType { get; init; } = 1;
}
