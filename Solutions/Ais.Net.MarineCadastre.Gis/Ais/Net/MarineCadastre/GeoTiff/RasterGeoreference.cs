// <copyright file="RasterGeoreference.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Models.Abstractions;

namespace Ais.Net.MarineCadastre.GeoTiff;

/// <summary>
/// Maps between raster cells, model coordinates and geographic positions for a north-up raster with
/// square-ish cells and no rotation, which is the only kind GeoTIFF tiepoint-and-scale georeferencing
/// describes.
/// </summary>
/// <param name="Projection">The model coordinate system.</param>
/// <param name="OriginX">Model X of the outer edge of the left column.</param>
/// <param name="OriginY">Model Y of the outer edge of the top row.</param>
/// <param name="CellWidth">Model units per column, positive.</param>
/// <param name="CellHeight">Model units per row, positive; rows go down as Y goes down.</param>
internal sealed record RasterGeoreference(RasterProjection Projection, double OriginX, double OriginY, double CellWidth, double CellHeight)
{
    private const double EarthRadius = 6378137.0;
    private const double MaxMercatorLatitude = 85.05112878;

    /// <summary>
    /// Converts a geographic position to model coordinates.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <returns>The model coordinates.</returns>
    public (double X, double Y) ToModel(Position position)
    {
        if (this.Projection == RasterProjection.Geographic)
        {
            return (position.Longitude, position.Latitude);
        }

        double latitude = Math.Clamp(position.Latitude, -MaxMercatorLatitude, MaxMercatorLatitude);
        double x = EarthRadius * position.Longitude * Math.PI / 180;
        double y = EarthRadius * Math.Log(Math.Tan((Math.PI / 4) + (latitude * Math.PI / 360)));
        return (x, y);
    }

    /// <summary>
    /// Converts model coordinates to a geographic position.
    /// </summary>
    /// <param name="x">Model X.</param>
    /// <param name="y">Model Y.</param>
    /// <returns>The position.</returns>
    public Position ToPosition(double x, double y) => new(this.LatitudeOf(y), this.LongitudeOf(x));

    /// <summary>
    /// Converts a model Y to latitude. In Web Mercator this is the expensive half of the conversion,
    /// and every cell in a raster row shares it.
    /// </summary>
    /// <param name="y">Model Y.</param>
    /// <returns>Latitude in degrees.</returns>
    public double LatitudeOf(double y) =>
        this.Projection == RasterProjection.Geographic ? y : Math.Atan(Math.Sinh(y / EarthRadius)) * 180 / Math.PI;

    /// <summary>
    /// Converts a model X to longitude, which in Web Mercator is a plain scaling.
    /// </summary>
    /// <param name="x">Model X.</param>
    /// <returns>Longitude in degrees.</returns>
    public double LongitudeOf(double x) =>
        this.Projection == RasterProjection.Geographic ? x : x / EarthRadius * 180 / Math.PI;

    /// <summary>
    /// Gets the geographic box covering a rectangle of cells at a given cell size.
    /// </summary>
    /// <param name="firstColumn">The leftmost column.</param>
    /// <param name="firstRow">The top row.</param>
    /// <param name="columns">How many columns.</param>
    /// <param name="rows">How many rows.</param>
    /// <param name="cellWidth">Model units per column at this level.</param>
    /// <param name="cellHeight">Model units per row at this level.</param>
    /// <returns>The box.</returns>
    public BoundingBox CellBounds(long firstColumn, long firstRow, long columns, long rows, double cellWidth, double cellHeight)
    {
        double west = this.OriginX + (firstColumn * cellWidth);
        double east = this.OriginX + ((firstColumn + columns) * cellWidth);
        double north = this.OriginY - (firstRow * cellHeight);
        double south = this.OriginY - ((firstRow + rows) * cellHeight);
        Position southWest = this.ToPosition(west, south);
        Position northEast = this.ToPosition(east, north);

        // Web Mercator rounding can land a world-wide raster's edges a fraction outside the WGS84
        // range, which BoundingBox rejects.
        return new BoundingBox(
            Math.Clamp(southWest.Longitude, -180, 180),
            Math.Clamp(southWest.Latitude, -90, 90),
            Math.Clamp(northEast.Longitude, -180, 180),
            Math.Clamp(northEast.Latitude, -90, 90));
    }
}
