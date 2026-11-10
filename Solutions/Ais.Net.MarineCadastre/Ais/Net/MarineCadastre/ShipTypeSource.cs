// <copyright file="ShipTypeSource.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre;

/// <summary>
/// The MarineCadastre column that supplies the <see cref="Ais.Net.ShipType"/> of a static-data message
/// or a vessel track.
/// </summary>
/// <remarks>
/// The MarineCadastre CSV files and track GeoPackages have two type columns. <c>vessel_type</c> is the
/// derived classification of MarineCadastre. It is empty in few rows, but it includes codes outside the
/// AIS range. <c>cargo</c> is closer to the raw AIS message 5 field, but it is empty in approximately a
/// third of the CSV rows. In both cases a code outside 0 to 99 maps to
/// <see cref="Ais.Net.ShipType.NotAvailable"/>.
/// </remarks>
public enum ShipTypeSource
{
    /// <summary>The reader uses <c>vessel_type</c>. If it is empty, the reader uses <c>cargo</c>.</summary>
    VesselTypeThenCargo,

    /// <summary>The reader uses <c>cargo</c>. If it is empty, the reader uses <c>vessel_type</c>.</summary>
    CargoThenVesselType,
}
