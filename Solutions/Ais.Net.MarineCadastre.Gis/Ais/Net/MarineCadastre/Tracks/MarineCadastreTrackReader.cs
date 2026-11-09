// <copyright file="MarineCadastreTrackReader.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Reactive.Linq;

using Microsoft.Data.Sqlite;

namespace Ais.Net.MarineCadastre.Tracks;

/// <summary>
/// Reads the monthly AIS Vessel Tracks GeoPackage files from MarineCadastre.gov and returns one
/// <see cref="VesselTrack"/> for each row.
/// </summary>
/// <remarks>
/// <para>
/// A GeoPackage is an SQLite database. The reader opens the file read-only with Microsoft.Data.Sqlite
/// and streams the feature table in primary-key order. It finds the table and the geometry column in
/// the GeoPackage registry tables. Thus a file for any month works without configuration. It matches
/// the attribute columns by name. The match ignores case and underscores.
/// </para>
/// <para>
/// The reader pushes the filters in <see cref="VesselTrackReaderOptions"/> into the query where the
/// file can help. A bounding box uses the R-tree index when the file has one. The time and MMSI
/// filters become <c>where</c> clauses. The reader also applies each filter to the decoded row. Thus
/// the result is the same with or without the index.
/// </para>
/// </remarks>
public static class MarineCadastreTrackReader
{
    /// <summary>
    /// Reads tracks as a cold observable. Each subscription opens the file and reads on a thread-pool
    /// thread. It delivers the tracks in file order and then completes. If you dispose the
    /// subscription, the read stops. Failures, which include a missing file, arrive as
    /// <see cref="IObserver{T}.OnError"/>.
    /// </summary>
    /// <param name="path">The path of the <c>.gpkg</c> file.</param>
    /// <param name="options">The filters. If you do not give options, the reader uses <see cref="VesselTrackReaderOptions.Default"/>.</param>
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
    /// Reads tracks as a lazy sequence on the thread of the caller. The reader opens the file on the
    /// first <c>MoveNext</c>. It closes the file when you dispose the enumerator or when the sequence
    /// ends.
    /// </summary>
    /// <param name="path">The path of the <c>.gpkg</c> file.</param>
    /// <param name="options">The filters. If you do not give options, the reader uses <see cref="VesselTrackReaderOptions.Default"/>.</param>
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
