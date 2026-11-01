// <copyright file="MarineCadastreTransitCountReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Reactive.Linq;

using Ais.Net.MarineCadastre.GeoTiff;

namespace Ais.Net.MarineCadastre.TransitCounts;

/// <summary>
/// Opens the yearly AIS Vessel Transit Counts rasters published by MarineCadastre.gov: GeoTIFFs whose
/// 100 m cells count vessel transits.
/// </summary>
/// <remarks>
/// The reader handles the files as published, which are BigTIFFs in Web Mercator with LZW tiles and
/// Deflate overviews, and the nearby variations: classic TIFF, strips, no compression, 8 or 16-bit
/// cells, geographic coordinates. It does not need GDAL.
/// </remarks>
public static class MarineCadastreTransitCountReader
{
    /// <summary>
    /// Opens a raster for repeated lookups and reads. Dispose it to close the file.
    /// </summary>
    /// <param name="path">Path of the <c>.tif</c> file.</param>
    /// <returns>The open raster.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the file is not a GeoTIFF this reader can place on the Earth.</exception>
    /// <exception cref="NotSupportedException">Thrown when the file uses a coordinate system, compression or sample type outside the supported set.</exception>
    public static TransitCountRaster Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The GeoTIFF file was not found.", path);
        }

        return new TransitCountRaster(GeoTiffFile.Open(path), path);
    }

    /// <summary>
    /// Streams the cells that hold data from a file, opening it for the life of each subscription.
    /// See <see cref="TransitCountRaster.Observe"/>.
    /// </summary>
    /// <param name="path">Path of the <c>.tif</c> file.</param>
    /// <param name="options">Filters; <see cref="TransitCountReaderOptions.Default"/> when omitted.</param>
    /// <returns>The cells, tile by tile from north-west to south-east.</returns>
    public static IObservable<TransitCell> Observe(string path, TransitCountReaderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Observable.Using(() => Open(path), raster => raster.Observe(options));
    }

    /// <summary>
    /// Streams the tiles that hold data from a file, opening it for the life of each subscription.
    /// See <see cref="TransitCountRaster.ObserveTiles"/>.
    /// </summary>
    /// <param name="path">Path of the <c>.tif</c> file.</param>
    /// <param name="options">Filters; <see cref="TransitCountReaderOptions.Default"/> when omitted.</param>
    /// <returns>The tiles, from north-west to south-east.</returns>
    public static IObservable<TransitTile> ObserveTiles(string path, TransitCountReaderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Observable.Using(() => Open(path), raster => raster.ObserveTiles(options));
    }
}
