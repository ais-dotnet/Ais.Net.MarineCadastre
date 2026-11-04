// <copyright file="MarineCadastreTrackReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Reactive.Linq;

using Microsoft.Data.Sqlite;

namespace Ais.Net.MarineCadastre.Tracks;

/// <summary>
/// Reads the monthly AIS Vessel Tracks GeoPackage files published by MarineCadastre.gov and yields one
/// <see cref="VesselTrack"/> per row.
/// </summary>
/// <remarks>
/// <para>
/// A GeoPackage is an SQLite database, so the reader opens the file read-only with Microsoft.Data.Sqlite
/// and streams the feature table in primary-key order. It discovers the table and geometry column from
/// the GeoPackage registry tables, so a file for any month works without configuration, and matches
/// attribute columns by name, case-insensitively and ignoring underscores.
/// </para>
/// <para>
/// Filters in <see cref="VesselTrackReaderOptions"/> are pushed into the query where the file can help:
/// a bounding box uses the R-tree index when the file has one, and time and MMSI filters become
/// <c>where</c> clauses. Every filter is also applied to the decoded row, so the result is the same with
/// or without the index.
/// </para>
/// </remarks>
public static class MarineCadastreTrackReader
{
    /// <summary>
    /// Reads tracks as a cold observable. Each subscription opens the file, reads on a thread-pool
    /// thread, delivers tracks in file order and completes; disposing the subscription stops the read.
    /// Failures, including a missing file, arrive as <see cref="IObserver{T}.OnError"/>.
    /// </summary>
    /// <param name="path">Path of the <c>.gpkg</c> file.</param>
    /// <param name="options">Filters; <see cref="VesselTrackReaderOptions.Default"/> when omitted.</param>
    /// <returns>The tracks that pass the filters, in file order.</returns>
    public static IObservable<VesselTrack> Observe(string path, VesselTrackReaderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        VesselTrackReaderOptions settings = options ?? VesselTrackReaderOptions.Default;

        return Observable.Create<VesselTrack>((observer, cancellationToken) => Task.Run(
            () =>
            {
                foreach (VesselTrack track in ReadCore(path, settings, cancellationToken))
                {
                    observer.OnNext(track);
                }
            },
            cancellationToken));
    }

    /// <summary>
    /// Reads tracks as a lazy sequence on the caller's thread. The file is opened on the first
    /// <c>MoveNext</c> and closed when the enumerator is disposed or runs out.
    /// </summary>
    /// <param name="path">Path of the <c>.gpkg</c> file.</param>
    /// <param name="options">Filters; <see cref="VesselTrackReaderOptions.Default"/> when omitted.</param>
    /// <returns>The tracks that pass the filters, in file order.</returns>
    public static IEnumerable<VesselTrack> Read(string path, VesselTrackReaderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ReadCore(path, options ?? VesselTrackReaderOptions.Default, CancellationToken.None);
    }

    private static IEnumerable<VesselTrack> ReadCore(string path, VesselTrackReaderOptions options, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The GeoPackage file was not found.", path);
        }

        using SqliteConnection connection = VesselTrackTable.Open(path);
        VesselTrackTable table = VesselTrackTable.Discover(connection, options.TableName);
        foreach (VesselTrack track in table.Read(connection, options, cancellationToken))
        {
            yield return track;
        }
    }
}
