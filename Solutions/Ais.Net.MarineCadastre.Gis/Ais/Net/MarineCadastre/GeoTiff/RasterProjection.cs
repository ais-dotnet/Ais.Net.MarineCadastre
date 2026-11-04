// <copyright file="RasterProjection.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre.GeoTiff;

/// <summary>
/// The coordinate reference systems a transit-count raster may use.
/// </summary>
internal enum RasterProjection
{
    /// <summary>Longitude and latitude in degrees (EPSG:4326 and relatives).</summary>
    Geographic,

    /// <summary>WGS 84 / Pseudo-Mercator, EPSG:3857, in metres on the sphere.</summary>
    WebMercator,
}
