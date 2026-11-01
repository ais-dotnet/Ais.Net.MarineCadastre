// <copyright file="BoundingBox.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Models.Abstractions;

namespace Ais.Net.MarineCadastre;

/// <summary>
/// A geographic bounding box in WGS84 degrees. Boxes that cross the antimeridian are not supported:
/// <paramref name="West"/> must not exceed <paramref name="East"/>.
/// </summary>
/// <param name="West">The westernmost longitude, in degrees.</param>
/// <param name="South">The southernmost latitude, in degrees.</param>
/// <param name="East">The easternmost longitude, in degrees.</param>
/// <param name="North">The northernmost latitude, in degrees.</param>
public readonly record struct BoundingBox(double West, double South, double East, double North)
{
    /// <summary>
    /// Gets the smallest box enclosing a set of positions.
    /// </summary>
    /// <param name="positions">The positions; at least one.</param>
    /// <returns>The enclosing box.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="positions"/> is empty.</exception>
    public static BoundingBox FromPositions(ReadOnlySpan<Position> positions)
    {
        if (positions.IsEmpty)
        {
            throw new ArgumentException("At least one position is required.", nameof(positions));
        }

        double west = double.MaxValue;
        double south = double.MaxValue;
        double east = double.MinValue;
        double north = double.MinValue;
        foreach (Position position in positions)
        {
            west = Math.Min(west, position.Longitude);
            east = Math.Max(east, position.Longitude);
            south = Math.Min(south, position.Latitude);
            north = Math.Max(north, position.Latitude);
        }

        return new BoundingBox(west, south, east, north);
    }

    /// <summary>
    /// Tests whether this box and another share any area, edges included.
    /// </summary>
    /// <param name="other">The other box.</param>
    /// <returns><see langword="true"/> when the boxes overlap or touch.</returns>
    public bool Intersects(BoundingBox other) =>
        this.West <= other.East && this.East >= other.West && this.South <= other.North && this.North >= other.South;

    /// <summary>
    /// Tests whether a position lies inside this box, edges included.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <returns><see langword="true"/> when the position is inside or on the edge.</returns>
    public bool Contains(Position position) =>
        position.Longitude >= this.West && position.Longitude <= this.East && position.Latitude >= this.South && position.Latitude <= this.North;
}
