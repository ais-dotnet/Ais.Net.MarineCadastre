// <copyright file="ShipTypeSource.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre;

/// <summary>
/// Which CSV column supplies the <see cref="Ais.Net.ShipType"/> of a static-data message.
/// </summary>
/// <remarks>
/// MarineCadastre files carry two type columns. <c>vessel_type</c> is their derived classification and
/// is rarely empty but includes codes outside the AIS range; <c>cargo</c> is closer to the raw AIS
/// message 5 field but is empty for roughly a third of rows. Either way a code outside 0 to 99 maps to
/// <see cref="Ais.Net.ShipType.NotAvailable"/>.
/// </remarks>
public enum ShipTypeSource
{
    /// <summary>Use <c>vessel_type</c>, falling back to <c>cargo</c> when it is empty.</summary>
    VesselTypeThenCargo,

    /// <summary>Use <c>cargo</c>, falling back to <c>vessel_type</c> when it is empty.</summary>
    CargoThenVesselType,
}
