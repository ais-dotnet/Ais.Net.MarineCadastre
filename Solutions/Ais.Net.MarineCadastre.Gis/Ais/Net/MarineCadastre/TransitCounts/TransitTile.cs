// <copyright file="TransitTile.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre.TransitCounts;

/// <summary>
/// One tile of a MarineCadastre transit-count raster: a rectangle of cells with their counts. It is for
/// consumers that want the grid itself, not one cell at a time.
/// </summary>
/// <remarks>
/// The constructor checks the tile. <paramref name="Width"/> and <paramref name="Height"/> must be
/// positive. <paramref name="Counts"/> must hold exactly <paramref name="Width"/> ×
/// <paramref name="Height"/> values. Those three properties are get-only. Thus a
/// <see langword="with"/> expression cannot break that relation.
/// </remarks>
/// <param name="Column">The column of the tile in the grid of tiles of the level.</param>
/// <param name="Row">The row of the tile in the grid of tiles of the level.</param>
/// <param name="Level">The resolution level of the tile. 0 is full resolution.</param>
/// <param name="FirstCellColumn">The column, at the level, of the cells on the west edge of the tile.</param>
/// <param name="FirstCellRow">The row, at the level, of the cells on the north edge of the tile.</param>
/// <param name="Width">The number of cells across. It is less than the tile size on the east edge of the raster.</param>
/// <param name="Height">The number of cells down. It is less than the tile size on the south edge of the raster.</param>
/// <param name="Bounds">The geographic box that the cells of the tile cover.</param>
/// <param name="Counts">The counts, row-major, <paramref name="Width"/> × <paramref name="Height"/> long. NoData cells hold 0.</param>
/// <exception cref="ArgumentOutOfRangeException">This exception occurs if <paramref name="Width"/> or <paramref name="Height"/> is zero or negative.</exception>
/// <exception cref="ArgumentException">This exception occurs if <paramref name="Counts"/> does not hold <paramref name="Width"/> × <paramref name="Height"/> values.</exception>
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
    /// Gets the number of cells across. It is less than the tile size on the east edge of the raster.
    /// </summary>
    public int Width { get; } = CheckPositive(Width, nameof(Width));

    /// <summary>
    /// Gets the number of cells down. It is less than the tile size on the south edge of the raster.
    /// </summary>
    public int Height { get; } = CheckPositive(Height, nameof(Height));

    /// <summary>
    /// Gets the counts, row-major. They are always exactly <see cref="Width"/> × <see cref="Height"/>
    /// long. NoData cells hold 0.
    /// </summary>
    public ReadOnlyMemory<uint> Counts { get; } = CheckCounts(Counts, Width, Height);

    /// <summary>
    /// Gets the count of one cell of the tile.
    /// </summary>
    /// <param name="column">The column in the tile, from 0 to <see cref="Width"/> - 1.</param>
    /// <param name="row">The row in the tile, from 0 to <see cref="Height"/> - 1.</param>
    /// <returns>The count. It is 0 for NoData.</returns>
    /// <exception cref="ArgumentOutOfRangeException">This exception occurs if <paramref name="column"/> or <paramref name="row"/> is outside the tile.</exception>
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
