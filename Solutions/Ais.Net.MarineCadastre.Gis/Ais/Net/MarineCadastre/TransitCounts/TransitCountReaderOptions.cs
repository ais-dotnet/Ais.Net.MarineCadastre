// <copyright file="TransitCountReaderOptions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre.TransitCounts;

/// <summary>
/// Settings for a read of cells or tiles from a <see cref="TransitCountRaster"/>.
/// </summary>
public sealed record TransitCountReaderOptions
{
    /// <summary>
    /// Gets the options that the raster uses if you do not give options: each cell with data, at full
    /// resolution.
    /// </summary>
    public static TransitCountReaderOptions Default { get; } = new();

    /// <summary>
    /// Gets the area of interest. The raster reads only the tiles that touch the box. It returns only
    /// the cells whose centre is inside the box. It returns a tile if any part of the tile is inside
    /// the box.
    /// </summary>
    public BoundingBox? Bounds { get; init; }

    /// <summary>
    /// Gets the resolution level to read. Level 0 is the full-resolution grid, and it is the only level
    /// with exact counts. Each higher level is an overview with cells of two times the size. The tool
    /// that built the file resampled the overviews.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Gets the smallest count that a cell must have for the raster to return it. The default is 1,
    /// which also excludes NoData cells. This filter does not apply to tiles.
    /// </summary>
    public uint MinimumTransits { get; init; } = 1;
}
