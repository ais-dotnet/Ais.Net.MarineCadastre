// <copyright file="VesselTrackReaderOptions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre.Tracks;

/// <summary>
/// Settings for <see cref="MarineCadastreTrackReader"/>. Each filter is optional. The reader returns a
/// track when it passes all the filters that are set.
/// </summary>
public sealed record VesselTrackReaderOptions
{
    /// <summary>
    /// Gets the options that each reader uses if you do not give options: each track in the file.
    /// </summary>
    public static VesselTrackReaderOptions Default { get; } = new();

    /// <summary>
    /// Gets the area of interest. The reader returns only the tracks whose
    /// <see cref="VesselTrack.Envelope"/> intersects the box. When the file has an R-tree index, the
    /// index narrows the search. Thus this filter is much cheaper than a read of all the tracks and a
    /// test of their positions.
    /// </summary>
    public BoundingBox? Bounds { get; init; }

    /// <summary>
    /// Gets the earliest time of interest. The reader returns only the tracks that end at or after this time.
    /// </summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>
    /// Gets the latest time of interest. The reader returns only the tracks that start at or before this time.
    /// </summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>
    /// Gets the one vessel of interest, or <see langword="null"/> for all the vessels.
    /// </summary>
    public uint? Mmsi { get; init; }

    /// <summary>
    /// Gets the column that supplies <see cref="VesselTrack.ShipType"/>. The default is
    /// <see cref="ShipTypeSource.VesselTypeThenCargo"/>. It does not affect
    /// <see cref="VesselTrack.ShipTypeName"/>, which always comes from the vessel type name column of the file.
    /// </summary>
    public ShipTypeSource ShipTypeSource { get; init; } = ShipTypeSource.VesselTypeThenCargo;

    /// <summary>
    /// Gets the name of the feature table to read. If it is <see langword="null"/> (the default), the
    /// reader uses the first features table in <c>gpkg_contents</c>, in name order. That is the correct
    /// table for the files that MarineCadastre publishes, because each file holds one month in one table.
    /// </summary>
    public string? TableName { get; init; }
}
