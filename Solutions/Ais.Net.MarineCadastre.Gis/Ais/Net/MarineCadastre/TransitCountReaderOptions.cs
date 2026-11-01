// <copyright file="TransitCountReaderOptions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre;

/// <summary>
/// Settings for reading cells or tiles from a <see cref="TransitCountRaster"/>.
/// </summary>
public sealed record TransitCountReaderOptions
{
    /// <summary>
    /// Gets the options used unless told otherwise: every cell with data, at full resolution.
    /// </summary>
    public static TransitCountReaderOptions Default { get; } = new();

    /// <summary>
    /// Gets the area of interest. Only tiles that touch the box are read, and only cells whose centre
    /// lies inside it are returned; a tile is returned when any part of it lies inside.
    /// </summary>
    public BoundingBox? Bounds { get; init; }

    /// <summary>
    /// Gets the resolution level to read. 0 is the full-resolution grid, the only level that holds
    /// exact counts; each higher level is an overview with cells twice as large, resampled by whatever
    /// tool built the file.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Gets the smallest count a cell must have to be returned. Defaults to 1, which also excludes
    /// NoData cells. Tiles are not filtered by it.
    /// </summary>
    public uint MinimumTransits { get; init; } = 1;
}
