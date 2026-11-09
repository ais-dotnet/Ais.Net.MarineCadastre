using System.Globalization;
using Ais.Net.Models.Abstractions;

using Microsoft.Data.Sqlite;

namespace Ais.Net.MarineCadastre.TestSupport;

/// <summary>
/// Writes a small GeoPackage with the same shape as a MarineCadastre AIS Vessel Tracks file, in a
/// temporary location, for tests and benchmarks. Shared between the two projects as a linked file.
/// </summary>
public sealed class GeoPackageBuilder : IDisposable
{
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.f'Z'";

    private readonly SqliteConnection connection;
    private readonly bool withRtree;

    public GeoPackageBuilder(string tableName = "AIS_Tracks_2026_01", bool withRtree = true)
    {
        this.TableName = tableName;
        this.withRtree = withRtree;
        this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ais-net-tracks-{Guid.NewGuid():N}.gpkg");

        this.connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = this.Path, Pooling = false }.ToString());
        this.connection.Open();
        this.Execute(
            $"""
            pragma application_id = 1196444487;
            pragma user_version = 10400;
            create table gpkg_spatial_ref_sys (srs_name text not null, srs_id integer primary key, organization text not null, organization_coordsys_id integer not null, definition text not null, description text);
            insert into gpkg_spatial_ref_sys values ('WGS 84', 4326, 'EPSG', 4326, 'GEOGCS["WGS 84"]', null);
            create table gpkg_contents (table_name text primary key, data_type text not null, identifier text unique, description text default '', last_change datetime, min_x double, min_y double, max_x double, max_y double, srs_id integer);
            create table gpkg_geometry_columns (table_name text not null primary key, column_name text not null, geometry_type_name text not null, srs_id integer not null, z tinyint not null, m tinyint not null);
            create table "{tableName}" (
                OBJECTID integer primary key autoincrement,
                Shape multilinestring,
                mmsi mediumint,
                vessel_name text(48),
                imo text(12),
                call_sign text(8),
                vessel_type smallint,
                vessel_type_name text(48),
                navigation_status smallint,
                vessel_length smallint,
                width smallint,
                draft float,
                cargo smallint,
                transceiver text(2),
                duration_minutes smallint,
                start_time datetime,
                end_time datetime);
            insert into gpkg_contents (table_name, data_type, identifier, srs_id) values ('{tableName}', 'features', '{tableName}', 4326);
            insert into gpkg_geometry_columns values ('{tableName}', 'Shape', 'MULTILINESTRING', 4326, 0, 0);
            """);

        if (withRtree)
        {
            this.Execute($"""create virtual table "rtree_{tableName}_Shape" using rtree(id, minx, maxx, miny, maxy);""");
        }
    }

    public string Path { get; }

    public string TableName { get; }

    /// <summary>
    /// Inserts a track row and returns its OBJECTID.
    /// </summary>
    public long Add(TrackRow row)
    {
        using SqliteCommand command = this.connection.CreateCommand();
        command.CommandText =
            $"""
            insert into "{this.TableName}" (Shape, mmsi, vessel_name, imo, call_sign, vessel_type, vessel_type_name, navigation_status, vessel_length, width, draft, cargo, transceiver, duration_minutes, start_time, end_time)
            values (@shape, @mmsi, @vessel_name, @imo, @call_sign, @vessel_type, @vessel_type_name, @navigation_status, @vessel_length, @width, @draft, @cargo, @transceiver, @duration_minutes, @start_time, @end_time);
            select last_insert_rowid();
            """;

        object shape = row.NoGeometry ? DBNull.Value : row.Geometry ?? GeoPackageGeometryEncoder.Encode([row.Points]);
        command.Parameters.AddWithValue("@shape", shape);
        command.Parameters.AddWithValue("@mmsi", (long)row.Mmsi);
        command.Parameters.AddWithValue("@vessel_name", (object?)row.VesselName ?? DBNull.Value);
        command.Parameters.AddWithValue("@imo", (object?)row.Imo ?? DBNull.Value);
        command.Parameters.AddWithValue("@call_sign", (object?)row.CallSign ?? DBNull.Value);
        command.Parameters.AddWithValue("@vessel_type", (object?)row.VesselType ?? DBNull.Value);
        command.Parameters.AddWithValue("@vessel_type_name", (object?)row.VesselTypeName ?? DBNull.Value);
        command.Parameters.AddWithValue("@navigation_status", (object?)row.NavigationStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("@vessel_length", (object?)row.Length ?? DBNull.Value);
        command.Parameters.AddWithValue("@width", (object?)row.Width ?? DBNull.Value);
        command.Parameters.AddWithValue("@draft", (object?)row.Draft ?? DBNull.Value);
        command.Parameters.AddWithValue("@cargo", (object?)row.Cargo ?? DBNull.Value);
        command.Parameters.AddWithValue("@transceiver", (object?)row.Transceiver ?? DBNull.Value);
        command.Parameters.AddWithValue("@duration_minutes", (object?)(row.DurationMinutes ?? (int)Math.Round((row.End - row.Start).TotalMinutes)) ?? DBNull.Value);
        command.Parameters.AddWithValue("@start_time", row.Start.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@end_time", row.End.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture));

        long id = (long)command.ExecuteScalar()!;

        if (this.withRtree && !row.NoGeometry && row.Points.Count > 0)
        {
            BoundingBox box = BoundingBox.FromPositions([.. row.Points]);
            using SqliteCommand index = this.connection.CreateCommand();
            index.CommandText = $"""insert into "rtree_{this.TableName}_Shape" values (@id, @minx, @maxx, @miny, @maxy)""";
            index.Parameters.AddWithValue("@id", id);
            index.Parameters.AddWithValue("@minx", box.West);
            index.Parameters.AddWithValue("@maxx", box.East);
            index.Parameters.AddWithValue("@miny", box.South);
            index.Parameters.AddWithValue("@maxy", box.North);
            index.ExecuteNonQuery();
        }

        return id;
    }

    /// <summary>
    /// Adds many rows in one transaction.
    /// </summary>
    public void AddRange(IEnumerable<TrackRow> rows)
    {
        using SqliteTransaction transaction = this.connection.BeginTransaction();
        foreach (TrackRow row in rows)
        {
            this.Add(row);
        }

        transaction.Commit();
    }

    public void Execute(string sql)
    {
        using SqliteCommand command = this.connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        this.connection.Dispose();
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                File.Delete(this.Path);
                return;
            }
            catch (IOException)
            {
                // A reader that was unsubscribed may still be closing the file; give it a moment.
                Thread.Sleep(50);
            }
        }
    }
}

/// <summary>
/// One row for <see cref="GeoPackageBuilder.Add"/>. Null attributes are stored as SQL null.
/// </summary>
public sealed record TrackRow(uint Mmsi, DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<Position> Points)
{
    public string? VesselName { get; init; }

    public string? Imo { get; init; }

    public string? CallSign { get; init; }

    public int? VesselType { get; init; }

    public string? VesselTypeName { get; init; }

    public int? NavigationStatus { get; init; }

    public int? Length { get; init; }

    public int? Width { get; init; }

    public double? Draft { get; init; }

    public int? Cargo { get; init; }

    public string? Transceiver { get; init; } = "A";

    /// <summary>
    /// Gets the stored duration; computed from <see cref="Start"/> and <see cref="End"/> when null.
    /// </summary>
    public int? DurationMinutes { get; init; }

    /// <summary>
    /// Gets a geometry blob to store instead of encoding <see cref="Points"/>.
    /// </summary>
    public byte[]? Geometry { get; init; }

    /// <summary>
    /// Gets a value indicating whether the geometry column is left null.
    /// </summary>
    public bool NoGeometry { get; init; }
}
