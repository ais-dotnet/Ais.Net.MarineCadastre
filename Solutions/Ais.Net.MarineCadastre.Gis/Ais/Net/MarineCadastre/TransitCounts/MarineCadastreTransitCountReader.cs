// <copyright file="MarineCadastreTransitCountReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Reactive.Linq;

using Ais.Net.MarineCadastre.GeoTiff;

namespace Ais.Net.MarineCadastre.TransitCounts;

/// <summary>
/// Opens the yearly AIS Vessel Transit Counts rasters from MarineCadastre.gov. These are GeoTIFFs whose
/// 100 m cells count vessel transits.
/// </summary>
/// <remarks>
/// The reader handles the files as MarineCadastre publishes them: BigTIFFs in Web Mercator with LZW
/// tiles and Deflate overviews. It also handles the near variations: classic TIFF, strips, no
/// compression, 8 or 16-bit cells, and geographic coordinates. It does not need GDAL.
/// </remarks>
public static class MarineCadastreTransitCountReader
{
    /// <summary>
    /// Opens a raster for repeated lookups and reads. Dispose the raster to close the file.
    /// </summary>
    /// <param name="path">The path of the <c>.tif</c> file.</param>
    /// <returns>The open raster.</returns>
    /// <exception cref="FileNotFoundException">This exception occurs if the file does not exist.</exception>
    /// <exception cref="InvalidDataException">This exception occurs if the file is not a GeoTIFF that this reader can place on the Earth.</exception>
    /// <exception cref="NotSupportedException">This exception occurs if the file uses a coordinate system, a compression or a sample type that the reader does not support.</exception>
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
    /// Streams the cells that hold data from a file. The file is open for the life of each subscription.
    /// See <see cref="TransitCountRaster.Observe"/>.
    /// </summary>
    /// <param name="path">The path of the <c>.tif</c> file.</param>
    /// <param name="options">The filters. If you do not give options, the reader uses <see cref="TransitCountReaderOptions.Default"/>.</param>
    /// <returns>The cells, tile by tile from north-west to south-east.</returns>
    public static IObservable<TransitCell> Observe(string path, TransitCountReaderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Observable.Using(() => Open(path), raster => raster.Observe(options));
    }

    /// <summary>
    /// Streams the tiles that hold data from a file. The file is open for the life of each subscription.
    /// See <see cref="TransitCountRaster.ObserveTiles"/>.
    /// </summary>
    /// <param name="path">The path of the <c>.tif</c> file.</param>
    /// <param name="options">The filters. If you do not give options, the reader uses <see cref="TransitCountReaderOptions.Default"/>.</param>
    /// <returns>The tiles, from north-west to south-east.</returns>
    public static IObservable<TransitTile> ObserveTiles(string path, TransitCountReaderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Observable.Using(() => Open(path), raster => raster.ObserveTiles(options));
    }
}
