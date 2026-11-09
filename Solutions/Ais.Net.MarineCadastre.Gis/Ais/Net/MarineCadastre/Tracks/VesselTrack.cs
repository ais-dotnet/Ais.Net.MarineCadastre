// <copyright file="VesselTrack.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Models.Abstractions;

namespace Ais.Net.MarineCadastre.Tracks;

/// <summary>
/// One row of a MarineCadastre AIS Vessel Tracks GeoPackage. It is the path of one vessel over part of
/// one day, with the static data of the vessel.
/// </summary>
/// <remarks>
/// <para>
/// The vertices are the reported positions of the vessel, in time order. But the file stores them as a
/// plain two-dimensional line, so each vertex has no timestamp. The track as a whole has only
/// <see cref="StartTime"/> and <see cref="EndTime"/>.
/// </para>
/// <para>
/// Columns that are empty in the file are <see langword="null"/> here. The exception is where an
/// Ais.Net.Models interface needs a value. Then <see cref="VesselName"/> and <see cref="CallSign"/> are
/// empty, <see cref="ShipType"/> is <see cref="Ais.Net.ShipType.NotAvailable"/>, and
/// <see cref="NavigationStatus"/> is <see cref="Ais.Net.NavigationStatus.NotDefined"/>.
/// </para>
/// <para>
/// Equality compares each member by value, except <see cref="Points"/>, which it compares by reference.
/// </para>
/// </remarks>
/// <param name="Id">The primary key (<c>OBJECTID</c>) of the row in the file.</param>
/// <param name="Mmsi">The Maritime Mobile Service Identity of the vessel.</param>
/// <param name="StartTime">The time of the first position, as UTC.</param>
/// <param name="EndTime">The time of the last position, as UTC.</param>
/// <param name="Points">The positions along the track, in time order.</param>
/// <exception cref="ArgumentNullException">This exception occurs if <paramref name="Points"/> is <see langword="null"/>.</exception>
public sealed record VesselTrack(
    long Id,
    uint Mmsi,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyList<Position> Points)
    : IVesselIdentity, IVesselName, ICallSign, IShipType, IVesselNavigationStatus
{
    /// <summary>
    /// Gets the positions along the track, in time order.
    /// </summary>
    /// <remarks>
    /// Tracks from <see cref="MarineCadastreTrackReader"/> hold a read-only list. You cannot cast it
    /// back to an array or to a different mutable collection.
    /// </remarks>
    /// <exception cref="ArgumentNullException">This exception occurs if you set the value to <see langword="null"/>.</exception>
    public IReadOnlyList<Position> Points { get; init => field = value ?? throw new ArgumentNullException(nameof(this.Points)); }
        = Points ?? throw new ArgumentNullException(nameof(Points));

    /// <summary>
    /// Gets the length of the track in time. It is the <c>duration_minutes</c> of the file when present.
    /// If not, it is <see cref="EndTime"/> minus <see cref="StartTime"/>.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the smallest box that contains <see cref="Points"/>.
    /// </summary>
    public BoundingBox Envelope { get; init; }

    /// <inheritdoc/>
    public string VesselName { get; init; } = string.Empty;

    /// <inheritdoc/>
    public string CallSign { get; init; } = string.Empty;

    /// <summary>
    /// Gets the IMO ship identification number, or <see langword="null"/> if the file has none.
    /// </summary>
    public uint? ImoNumber { get; init; }

    /// <inheritdoc/>
    public ShipType ShipType { get; init; } = ShipType.NotAvailable;

    /// <summary>
    /// Gets the MarineCadastre description of <see cref="ShipType"/>, for example <c>Pleasure Craft</c>.
    /// It is empty if the file has none.
    /// </summary>
    public string ShipTypeName { get; init; } = string.Empty;

    /// <inheritdoc/>
    public NavigationStatus NavigationStatus { get; init; } = NavigationStatus.NotDefined;

    /// <summary>
    /// Gets the length of the vessel in metres, or <see langword="null"/> if the file has none.
    /// </summary>
    public uint? Length { get; init; }

    /// <summary>
    /// Gets the width of the vessel in metres, or <see langword="null"/> if the file has none.
    /// </summary>
    public uint? Width { get; init; }

    /// <summary>
    /// Gets the draught of the vessel in metres, or <see langword="null"/> if the file has none.
    /// </summary>
    public double? Draught { get; init; }

    /// <summary>
    /// Gets the <c>cargo</c> code of the file, or <see langword="null"/> if the file has none.
    /// <see cref="ShipTypeSource"/> says if this code or <c>vessel_type</c> supplies <see cref="ShipType"/>.
    /// </summary>
    public uint? Cargo { get; init; }

    /// <summary>
    /// Gets a value that is <see langword="true"/> if the vessel reported through a Class B transceiver.
    /// </summary>
    public bool IsClassB { get; init; }
}
