// <copyright file="VesselTrack.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Models.Abstractions;

namespace Ais.Net.MarineCadastre.Tracks;

/// <summary>
/// One row of a MarineCadastre AIS Vessel Tracks GeoPackage: the path one vessel followed over part of
/// one day, with the static data MarineCadastre attached to it.
/// </summary>
/// <remarks>
/// <para>
/// The vertices are the vessel's reported positions in time order, but the file stores them as a plain
/// two-dimensional line, so individual vertices carry no timestamp. Only <see cref="StartTime"/> and
/// <see cref="EndTime"/> are known for the track as a whole.
/// </para>
/// <para>
/// Columns that are empty in the file are <see langword="null"/> here, except where an Ais.Net.Models
/// interface requires a value: <see cref="VesselName"/> and <see cref="CallSign"/> are then empty,
/// <see cref="ShipType"/> is <see cref="Ais.Net.ShipType.NotAvailable"/> and
/// <see cref="NavigationStatus"/> is <see cref="Ais.Net.NavigationStatus.NotDefined"/>.
/// </para>
/// <para>
/// Equality is by value for every member except <see cref="Points"/>, which compares by reference.
/// </para>
/// </remarks>
/// <param name="Id">The row's primary key (<c>OBJECTID</c>) in the file.</param>
/// <param name="Mmsi">The Maritime Mobile Service Identity of the vessel.</param>
/// <param name="StartTime">The time of the first position, as UTC.</param>
/// <param name="EndTime">The time of the last position, as UTC.</param>
/// <param name="Points">The positions along the track, in time order.</param>
public sealed record VesselTrack(
    long Id,
    uint Mmsi,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyList<Position> Points)
    : IVesselIdentity, IVesselName, ICallSign, IShipType, IVesselNavigationStatus
{
    /// <summary>
    /// Gets the length of the track in time: the file's <c>duration_minutes</c> when present, otherwise
    /// <see cref="EndTime"/> less <see cref="StartTime"/>.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the smallest box enclosing <see cref="Points"/>.
    /// </summary>
    public BoundingBox Envelope { get; init; }

    /// <inheritdoc/>
    public string VesselName { get; init; } = string.Empty;

    /// <inheritdoc/>
    public string CallSign { get; init; } = string.Empty;

    /// <summary>
    /// Gets the IMO ship identification number, or <see langword="null"/> when the file has none.
    /// </summary>
    public uint? ImoNumber { get; init; }

    /// <inheritdoc/>
    public ShipType ShipType { get; init; } = ShipType.NotAvailable;

    /// <summary>
    /// Gets MarineCadastre's description of <see cref="ShipType"/>, such as <c>Pleasure Craft</c>, or
    /// empty when the file has none.
    /// </summary>
    public string ShipTypeName { get; init; } = string.Empty;

    /// <inheritdoc/>
    public NavigationStatus NavigationStatus { get; init; } = NavigationStatus.NotDefined;

    /// <summary>
    /// Gets the vessel length in metres, or <see langword="null"/> when the file has none.
    /// </summary>
    public uint? Length { get; init; }

    /// <summary>
    /// Gets the vessel width in metres, or <see langword="null"/> when the file has none.
    /// </summary>
    public uint? Width { get; init; }

    /// <summary>
    /// Gets the vessel draught in metres, or <see langword="null"/> when the file has none.
    /// </summary>
    public double? Draught { get; init; }

    /// <summary>
    /// Gets the file's <c>cargo</c> code, or <see langword="null"/> when the file has none. It is the
    /// fallback for <see cref="ShipType"/> when <c>vessel_type</c> is empty.
    /// </summary>
    public uint? Cargo { get; init; }

    /// <summary>
    /// Gets a value indicating whether the vessel reported through a Class B transceiver.
    /// </summary>
    public bool IsClassB { get; init; }
}
