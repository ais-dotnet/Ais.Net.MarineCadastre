// <copyright file="VesselTrackReaderOptions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre.Tracks;

/// <summary>
/// Settings for <see cref="MarineCadastreTrackReader"/>. Every filter is optional; a track is returned
/// when it passes all the filters that are set.
/// </summary>
public sealed record VesselTrackReaderOptions
{
    /// <summary>
    /// Gets the options every reader uses unless told otherwise: every track in the file.
    /// </summary>
    public static VesselTrackReaderOptions Default { get; } = new();

    /// <summary>
    /// Gets the area of interest. Only tracks whose <see cref="VesselTrack.Envelope"/> intersects the
    /// box are returned. The file's R-tree index narrows the search when it has one, so this filter is
    /// much cheaper than reading everything and testing positions.
    /// </summary>
    public BoundingBox? Bounds { get; init; }

    /// <summary>
    /// Gets the earliest time of interest. Only tracks that end at or after it are returned.
    /// </summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>
    /// Gets the latest time of interest. Only tracks that start at or before it are returned.
    /// </summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>
    /// Gets the single vessel of interest, or <see langword="null"/> for all vessels.
    /// </summary>
    public uint? Mmsi { get; init; }

    /// <summary>
    /// Gets the column that supplies <see cref="VesselTrack.ShipType"/>. Defaults to
    /// <see cref="ShipTypeSource.VesselTypeThenCargo"/>. It does not affect
    /// <see cref="VesselTrack.ShipTypeName"/>, which always comes from the file's vessel type name column.
    /// </summary>
    public ShipTypeSource ShipTypeSource { get; init; } = ShipTypeSource.VesselTypeThenCargo;

    /// <summary>
    /// Gets the name of the feature table to read. When <see langword="null"/> (the default) the first
    /// features table registered in the GeoPackage's <c>gpkg_contents</c> is used, which is the right
    /// choice for the files MarineCadastre publishes, since each holds one month in one table.
    /// </summary>
    public string? TableName { get; init; }
}
