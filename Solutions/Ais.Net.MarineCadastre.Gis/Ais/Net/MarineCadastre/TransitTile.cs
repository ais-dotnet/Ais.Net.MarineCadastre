// <copyright file="TransitTile.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre;

/// <summary>
/// One tile of a MarineCadastre transit-count raster: a rectangle of cells with their counts, for
/// consumers that want the grid itself rather than one cell at a time.
/// </summary>
/// <param name="Column">The tile's column within the level's grid of tiles.</param>
/// <param name="Row">The tile's row within the level's grid of tiles.</param>
/// <param name="Level">The resolution level the tile came from; 0 is full resolution.</param>
/// <param name="FirstCellColumn">The level column of the tile's leftmost cell.</param>
/// <param name="FirstCellRow">The level row of the tile's top cell.</param>
/// <param name="Width">Cells across; smaller than the tile size on the raster's east edge.</param>
/// <param name="Height">Cells down; smaller than the tile size on the raster's south edge.</param>
/// <param name="Bounds">The geographic box the tile's cells cover.</param>
/// <param name="Counts">The counts, row-major, <paramref name="Width"/> × <paramref name="Height"/> long; NoData cells hold 0.</param>
public sealed record TransitTile(
    int Column,
    int Row,
    int Level,
    int FirstCellColumn,
    int FirstCellRow,
    int Width,
    int Height,
    BoundingBox Bounds,
    ReadOnlyMemory<uint> Counts)
{
    /// <summary>
    /// Gets the count of one cell of the tile.
    /// </summary>
    /// <param name="column">The column within the tile.</param>
    /// <param name="row">The row within the tile.</param>
    /// <returns>The count, 0 for NoData.</returns>
    public uint this[int column, int row] => this.Counts.Span[(row * this.Width) + column];
}
