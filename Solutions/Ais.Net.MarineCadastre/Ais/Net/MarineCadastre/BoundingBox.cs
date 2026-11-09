// <copyright file="BoundingBox.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Models.Abstractions;

namespace Ais.Net.MarineCadastre;

/// <summary>
/// A geographic bounding box in WGS84 degrees. The box cannot cross the antimeridian:
/// <paramref name="West"/> must not be more than <paramref name="East"/>.
/// </summary>
/// <remarks>
/// <para>
/// The constructor checks the box. Each longitude must be between -180 and 180 degrees. Each latitude
/// must be between -90 and 90 degrees. No value can be NaN. <paramref name="West"/> must not be more
/// than <paramref name="East"/>, and <paramref name="South"/> must not be more than
/// <paramref name="North"/>. Thus a box with the latitude and the longitude in the wrong positions
/// causes an exception, not an empty result. The properties are get-only. Thus a
/// <see langword="with"/> expression cannot make a box that does not pass the checks.
/// </para>
/// <para>
/// <c>default(BoundingBox)</c> is a valid box. It has zero area at latitude 0 and longitude 0.
/// </para>
/// </remarks>
/// <param name="West">The longitude of the west edge, in degrees.</param>
/// <param name="South">The latitude of the south edge, in degrees.</param>
/// <param name="East">The longitude of the east edge, in degrees.</param>
/// <param name="North">The latitude of the north edge, in degrees.</param>
/// <exception cref="ArgumentOutOfRangeException">
/// This exception occurs if a longitude is outside -180 to 180 degrees or a latitude is outside -90 to
/// 90 degrees. It also occurs if a value is NaN.
/// </exception>
/// <exception cref="ArgumentException">
/// This exception occurs if <paramref name="West"/> is more than <paramref name="East"/>, or if
/// <paramref name="South"/> is more than <paramref name="North"/>.
/// </exception>
public readonly record struct BoundingBox(double West, double South, double East, double North)
{
    /// <summary>
    /// Gets the longitude of the west edge, in degrees.
    /// </summary>
    public double West { get; } = CheckLongitude(West, nameof(West));

    /// <summary>
    /// Gets the latitude of the south edge, in degrees.
    /// </summary>
    public double South { get; } = CheckLatitude(South, nameof(South));

    /// <summary>
    /// Gets the longitude of the east edge, in degrees.
    /// </summary>
    public double East { get; } = CheckEast(West, East);

    /// <summary>
    /// Gets the latitude of the north edge, in degrees.
    /// </summary>
    public double North { get; } = CheckNorth(South, North);

    /// <summary>
    /// Gets the smallest box that contains all the positions.
    /// </summary>
    /// <param name="positions">The positions. There must be at least one.</param>
    /// <returns>The box that contains the positions.</returns>
    /// <exception cref="ArgumentException">This exception occurs if <paramref name="positions"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// This exception occurs if a position has a latitude outside -90 to 90 degrees, a longitude outside
    /// -180 to 180 degrees, or a NaN coordinate.
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
    /// Tests if this box and another box have a common area. Boxes that touch at an edge also intersect.
    /// </summary>
    /// <param name="other">The other box.</param>
    /// <returns><see langword="true"/> if the boxes have a common area or touch.</returns>
    public bool Intersects(BoundingBox other) =>
        this.West <= other.East && this.East >= other.West && this.South <= other.North && this.North >= other.South;

    /// <summary>
    /// Tests if a position is inside this box. A position on an edge is inside.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <returns><see langword="true"/> if the position is inside the box or on an edge.</returns>
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
