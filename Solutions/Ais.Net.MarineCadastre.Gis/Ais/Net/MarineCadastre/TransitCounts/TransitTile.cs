// <copyright file="TransitTile.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre.TransitCounts;

/// <summary>
/// One tile of a MarineCadastre transit-count raster: a rectangle of cells with their counts, for
/// consumers that want the grid itself rather than one cell at a time.
/// </summary>
/// <remarks>
/// The tile is checked when it is constructed: <paramref name="Width"/> and <paramref name="Height"/>
/// must be positive and <paramref name="Counts"/> must hold exactly <paramref name="Width"/> ×
/// <paramref name="Height"/> values. Those three properties are get-only, so a <see langword="with"/>
/// expression cannot break that relationship.
/// </remarks>
/// <param name="Column">The tile's column within the level's grid of tiles.</param>
/// <param name="Row">The tile's row within the level's grid of tiles.</param>
/// <param name="Level">The resolution level the tile came from; 0 is full resolution.</param>
/// <param name="FirstCellColumn">The level column of the tile's leftmost cell.</param>
/// <param name="FirstCellRow">The level row of the tile's top cell.</param>
/// <param name="Width">Cells across; smaller than the tile size on the raster's east edge.</param>
/// <param name="Height">Cells down; smaller than the tile size on the raster's south edge.</param>
/// <param name="Bounds">The geographic box the tile's cells cover.</param>
/// <param name="Counts">The counts, row-major, <paramref name="Width"/> × <paramref name="Height"/> long; NoData cells hold 0.</param>
/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="Width"/> or <paramref name="Height"/> is zero or negative.</exception>
/// <exception cref="ArgumentException">Thrown when <paramref name="Counts"/> does not hold <paramref name="Width"/> × <paramref name="Height"/> values.</exception>
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
    /// Gets the number of cells across; smaller than the tile size on the raster's east edge.
    /// </summary>
    public int Width { get; } = CheckPositive(Width, nameof(Width));

    /// <summary>
    /// Gets the number of cells down; smaller than the tile size on the raster's south edge.
    /// </summary>
    public int Height { get; } = CheckPositive(Height, nameof(Height));

    /// <summary>
    /// Gets the counts, row-major, always exactly <see cref="Width"/> × <see cref="Height"/> long;
    /// NoData cells hold 0.
    /// </summary>
    public ReadOnlyMemory<uint> Counts { get; } = CheckCounts(Counts, Width, Height);

    /// <summary>
    /// Gets the count of one cell of the tile.
    /// </summary>
    /// <param name="column">The column within the tile, from 0 to <see cref="Width"/> - 1.</param>
    /// <param name="row">The row within the tile, from 0 to <see cref="Height"/> - 1.</param>
    /// <returns>The count, 0 for NoData.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="column"/> or <paramref name="row"/> is outside the tile.</exception>
    public uint this[int column, int row]
    {
        get
        {
            if ((uint)column >= (uint)this.Width)
            {
                throw new ArgumentOutOfRangeException(nameof(column), column, "The column is outside the tile.");
            }

            if ((uint)row >= (uint)this.Height)
            {
                throw new ArgumentOutOfRangeException(nameof(row), row, "The row is outside the tile.");
            }

            return this.Counts.Span[(row * this.Width) + column];
        }
    }

    private static int CheckPositive(int value, string name)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, name);
        return value;
    }

    private static ReadOnlyMemory<uint> CheckCounts(ReadOnlyMemory<uint> counts, int width, int height) =>
        counts.Length == (long)width * height
            ? counts
            : throw new ArgumentException("Counts must hold Width × Height values.", nameof(Counts));
}
