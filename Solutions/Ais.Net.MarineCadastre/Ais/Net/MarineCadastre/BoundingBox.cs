// <copyright file="BoundingBox.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Models.Abstractions;

namespace Ais.Net.MarineCadastre;

/// <summary>
/// A geographic bounding box in WGS84 degrees. Boxes that cross the antimeridian are not supported:
/// <paramref name="West"/> must not exceed <paramref name="East"/>.
/// </summary>
/// <remarks>
/// <para>
/// The box is checked when it is constructed. Longitudes must be between -180 and 180 degrees,
/// latitudes between -90 and 90 degrees, and none may be NaN. <paramref name="West"/> must not exceed
/// <paramref name="East"/>, and <paramref name="South"/> must not exceed <paramref name="North"/>. So a
/// box with latitude and longitude swapped throws instead of silently matching nothing. The properties
/// are get-only, so a <see langword="with"/> expression cannot bypass the checks.
/// </para>
/// <para>
/// <c>default(BoundingBox)</c> is valid: the zero-area box at latitude 0, longitude 0.
/// </para>
/// </remarks>
/// <param name="West">The westernmost longitude, in degrees.</param>
/// <param name="South">The southernmost latitude, in degrees.</param>
/// <param name="East">The easternmost longitude, in degrees.</param>
/// <param name="North">The northernmost latitude, in degrees.</param>
/// <exception cref="ArgumentOutOfRangeException">
/// Thrown when a longitude is outside -180 to 180 degrees, a latitude is outside -90 to 90 degrees, or a value is NaN.
/// </exception>
/// <exception cref="ArgumentException">
/// Thrown when <paramref name="West"/> exceeds <paramref name="East"/> or <paramref name="South"/> exceeds <paramref name="North"/>.
/// </exception>
public readonly record struct BoundingBox(double West, double South, double East, double North)
{
    /// <summary>
    /// Gets the westernmost longitude, in degrees.
    /// </summary>
    public double West { get; } = CheckLongitude(West, nameof(West));

    /// <summary>
    /// Gets the southernmost latitude, in degrees.
    /// </summary>
    public double South { get; } = CheckLatitude(South, nameof(South));

    /// <summary>
    /// Gets the easternmost longitude, in degrees.
    /// </summary>
    public double East { get; } = CheckEast(West, East);

    /// <summary>
    /// Gets the northernmost latitude, in degrees.
    /// </summary>
    public double North { get; } = CheckNorth(South, North);

    /// <summary>
    /// Gets the smallest box enclosing a set of positions.
    /// </summary>
    /// <param name="positions">The positions; at least one.</param>
    /// <returns>The enclosing box.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="positions"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when a position has a latitude outside -90 to 90 degrees, a longitude outside -180 to 180
    /// degrees, or a NaN coordinate.
    /// </exception>
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

    private static double CheckLongitude(double value, string name) =>
        Math.Abs(value) <= 180 ? value : throw new ArgumentOutOfRangeException(name, value, "Longitude must be between -180 and 180 degrees.");

    private static double CheckLatitude(double value, string name) =>
        Math.Abs(value) <= 90 ? value : throw new ArgumentOutOfRangeException(name, value, "Latitude must be between -90 and 90 degrees.");

    private static double CheckEast(double west, double east)
    {
        CheckLongitude(east, nameof(East));
        return west <= east ? east : throw new ArgumentException("West must not exceed East.", nameof(East));
    }

    private static double CheckNorth(double south, double north)
    {
        CheckLatitude(north, nameof(North));
        return south <= north ? north : throw new ArgumentException("South must not exceed North.", nameof(North));
    }
}
