// <copyright file="TrackTable.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text;

using Ais.Net.Models.Abstractions;

using Ais.Net.MarineCadastre.Tracks;

using Microsoft.Data.Sqlite;

using SQLitePCL;

namespace Ais.Net.MarineCadastre.GeoPackage;

/// <summary>
/// The feature table of a tracks GeoPackage: which table and columns to read, the query that reads
/// them, and the mapping from a result row to a <see cref="VesselTrack"/>.
/// </summary>
/// <remarks>
/// Discovery uses Microsoft.Data.Sqlite. The row loop steps a prepared statement through the
/// SQLitePCLRaw API underneath it instead, because that hands back each column as a span over SQLite's
/// own memory: no copy of the geometry blob, no string for the timestamps, and none of the per-column
/// type negotiation of the ADO.NET layer, which together cost more than SQLite's own work on a file of
/// a million rows.
/// </remarks>
internal sealed class TrackTable
{
    // Ordinals in the select list built by Prepare. Attribute columns the file lacks are selected as
    // null so the ordinals never move.
    private const int IdOrdinal = 0;
    private const int GeometryOrdinal = 1;
    private const int MmsiOrdinal = 2;
    private const int VesselNameOrdinal = 3;
    private const int ImoOrdinal = 4;
    private const int CallSignOrdinal = 5;
    private const int VesselTypeOrdinal = 6;
    private const int VesselTypeNameOrdinal = 7;
    private const int NavigationStatusOrdinal = 8;
    private const int LengthOrdinal = 9;
    private const int WidthOrdinal = 10;
    private const int DraughtOrdinal = 11;
    private const int CargoOrdinal = 12;
    private const int TransceiverOrdinal = 13;
    private const int DurationOrdinal = 14;
    private const int StartTimeOrdinal = 15;
    private const int EndTimeOrdinal = 16;

    // Attribute columns in ordinal order from MmsiOrdinal, each with the normalised names it may have
    // in the file (lower case, underscores removed), most likely first.
    private static readonly string[][] AttributeAliases =
    [
        ["mmsi"],
        ["vesselname", "name"],
        ["imo", "imonumber"],
        ["callsign"],
        ["vesseltype", "shiptype"],
        ["vesseltypename", "shiptypename"],
        ["navigationstatus", "status", "navstatus"],
        ["vessellength", "length"],
        ["width"],
        ["draft", "draught"],
        ["cargo"],
        ["transceiver", "transceiverclass"],
        ["durationminutes"],
        ["starttime"],
        ["endtime"],
    ];

    private const string SqlDateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    private readonly string tableName;
    private readonly string idColumn;
    private readonly string geometryColumn;
    private readonly string? rtreeTable;
    private readonly string?[] attributeColumns;

    private TrackTable(string tableName, string idColumn, string geometryColumn, string? rtreeTable, string?[] attributeColumns)
    {
        this.tableName = tableName;
        this.idColumn = idColumn;
        this.geometryColumn = geometryColumn;
        this.rtreeTable = rtreeTable;
        this.attributeColumns = attributeColumns;
    }

    /// <summary>
    /// Opens a GeoPackage read-only. Pooling is off so that disposing the connection releases the file.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <returns>The open connection.</returns>
    public static SqliteConnection Open(string path)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };

        SqliteConnection connection = new(builder.ToString());
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Finds the feature table, its geometry column, primary key, R-tree index and attribute columns.
    /// </summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="requestedTable">The table to read, or <see langword="null"/> to take the first features table in <c>gpkg_contents</c>.</param>
    /// <returns>The table description.</returns>
    /// <exception cref="InvalidDataException">Thrown when the file is not a usable tracks GeoPackage.</exception>
    public static TrackTable Discover(SqliteConnection connection, string? requestedTable)
    {
        string? tableName = requestedTable ?? Scalar(connection, "select table_name from gpkg_contents where data_type = 'features' order by table_name limit 1") as string;
        if (tableName is null)
        {
            throw new InvalidDataException("The GeoPackage registers no features table in gpkg_contents.");
        }

        if (Scalar(connection, "select column_name from gpkg_geometry_columns where table_name = @table", ("@table", tableName)) is not string geometryColumn)
        {
            throw new InvalidDataException($"Table '{tableName}' is not registered in gpkg_geometry_columns.");
        }

        Dictionary<string, string> columnsByNormalisedName = new(StringComparer.Ordinal);
        string? primaryKey = null;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = $"pragma table_info({QuoteIdentifier(tableName)})";
            using SqliteDataReader reader = command.ExecuteReader();
            int nameOrdinal = reader.GetOrdinal("name");
            int pkOrdinal = reader.GetOrdinal("pk");
            while (reader.Read())
            {
                string name = reader.GetString(nameOrdinal);
                columnsByNormalisedName.TryAdd(Normalise(name), name);
                if (primaryKey is null && reader.GetInt64(pkOrdinal) == 1)
                {
                    primaryKey = name;
                }
            }
        }

        if (columnsByNormalisedName.Count == 0)
        {
            throw new InvalidDataException($"Table '{tableName}' does not exist in the GeoPackage.");
        }

        string?[] attributeColumns = new string?[AttributeAliases.Length];
        for (int i = 0; i < AttributeAliases.Length; i++)
        {
            foreach (string alias in AttributeAliases[i])
            {
                if (columnsByNormalisedName.TryGetValue(alias, out string? actual))
                {
                    attributeColumns[i] = actual;
                    break;
                }
            }
        }

        foreach ((int ordinal, string role) in new[] { (MmsiOrdinal, "mmsi"), (StartTimeOrdinal, "start_time"), (EndTimeOrdinal, "end_time") })
        {
            if (attributeColumns[ordinal - MmsiOrdinal] is null)
            {
                throw new InvalidDataException($"Table '{tableName}' has no {role} column.");
            }
        }

        string rtreeName = $"rtree_{tableName}_{geometryColumn}";
        bool hasRtree = Scalar(connection, "select 1 from sqlite_master where type = 'table' and name = @name", ("@name", rtreeName)) is not null;

        return new TrackTable(tableName, primaryKey ?? "rowid", geometryColumn, hasRtree ? rtreeName : null, attributeColumns);
    }

    /// <summary>
    /// Reads the tracks that pass the filters, in primary-key order.
    /// </summary>
    /// <param name="connection">The open connection the table was discovered on.</param>
    /// <param name="options">The filters.</param>
    /// <param name="cancellationToken">Checked before every row.</param>
    /// <returns>The tracks.</returns>
    public IEnumerable<VesselTrack> Read(SqliteConnection connection, VesselTrackReaderOptions options, CancellationToken cancellationToken)
    {
        sqlite3 database = connection.Handle ?? throw new InvalidOperationException("The connection is not open.");
        using sqlite3_stmt statement = this.Prepare(database, options);
        RecentStrings strings = new();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int result = raw.sqlite3_step(statement);
            if (result == raw.SQLITE_DONE)
            {
                yield break;
            }

            if (result != raw.SQLITE_ROW)
            {
                SqliteException.ThrowExceptionForRC(result, database);
            }

            VesselTrack? track = this.ReadTrack(statement, options, strings);
            if (track is not null)
            {
                yield return track;
            }
        }
    }

    /// <summary>
    /// Parses the timestamp text the files use, <c>yyyy-MM-ddTHH:mm:ss.fZ</c>, and its near relatives:
    /// a space instead of <c>T</c>, any number of fraction digits or none, with or without the <c>Z</c>.
    /// </summary>
    /// <param name="text">The UTF-8 text.</param>
    /// <param name="value">The UTC time when the text is in that shape.</param>
    /// <returns><see langword="true"/> when parsed.</returns>
    internal static bool TryParseTimestamp(ReadOnlySpan<byte> text, out DateTimeOffset value)
    {
        value = default;
        if (text.Length < 19 || text[4] != '-' || text[7] != '-' || (text[10] != 'T' && text[10] != ' ') || text[13] != ':' || text[16] != ':')
        {
            return false;
        }

        if (!TryDigits(text[..4], out int year) || !TryDigits(text.Slice(5, 2), out int month) || !TryDigits(text.Slice(8, 2), out int day)
            || !TryDigits(text.Slice(11, 2), out int hour) || !TryDigits(text.Slice(14, 2), out int minute) || !TryDigits(text.Slice(17, 2), out int second))
        {
            return false;
        }

        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59)
        {
            return false;
        }

        long fractionTicks = 0;
        int position = 19;
        if (position < text.Length && text[position] == '.')
        {
            position++;
            long scale = TimeSpan.TicksPerSecond / 10;
            int digits = 0;
            while (position < text.Length && (uint)(text[position] - '0') <= 9)
            {
                if (scale > 0)
                {
                    fractionTicks += (text[position] - '0') * scale;
                    scale /= 10;
                }

                position++;
                digits++;
            }

            if (digits == 0)
            {
                return false;
            }
        }

        if (position < text.Length && text[position] == 'Z')
        {
            position++;
        }

        if (position != text.Length)
        {
            return false;
        }

        value = new DateTimeOffset(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc).AddTicks(fractionTicks));
        return true;

        static bool TryDigits(ReadOnlySpan<byte> digits, out int number)
        {
            number = 0;
            foreach (byte digit in digits)
            {
                uint d = (uint)(digit - '0');
                if (d > 9)
                {
                    return false;
                }

                number = (number * 10) + (int)d;
            }

            return true;
        }
    }

    private sqlite3_stmt Prepare(sqlite3 database, VesselTrackReaderOptions options)
    {
        StringBuilder sql = new();
        List<object> parameters = [];

        sql.Append("select t.").Append(QuoteIdentifier(this.idColumn));
        sql.Append(", t.").Append(QuoteIdentifier(this.geometryColumn));
        for (int i = 0; i < this.attributeColumns.Length; i++)
        {
            string? column = this.attributeColumns[i];
            bool numeric = IsNumericOrdinal(i + MmsiOrdinal);
            sql.Append(", ");
            if (column is null)
            {
                sql.Append(numeric ? "-1" : "null");
            }
            else if (numeric)
            {
                // Numeric columns come back as -1 when null, so reading one costs a single native call
                // instead of a type probe followed by a fetch. Every real value is non-negative.
                sql.Append("ifnull(t.").Append(QuoteIdentifier(column)).Append(", -1)");
            }
            else
            {
                sql.Append("t.").Append(QuoteIdentifier(column));
            }
        }

        sql.Append(" from ").Append(QuoteIdentifier(this.tableName)).Append(" t");

        List<string> conditions = [];
        if (options.Bounds is { } bounds && this.rtreeTable is not null)
        {
            sql.Append(" join ").Append(QuoteIdentifier(this.rtreeTable)).Append(" r on r.id = t.").Append(QuoteIdentifier(this.idColumn));
            conditions.Add("r.maxx >= ? and r.minx <= ? and r.maxy >= ? and r.miny <= ?");
            parameters.AddRange([bounds.West, bounds.East, bounds.South, bounds.North]);
        }

        if (options.Mmsi is { } mmsi)
        {
            conditions.Add($"t.{QuoteIdentifier(this.attributeColumns[MmsiOrdinal - MmsiOrdinal]!)} = ?");
            parameters.Add((long)mmsi);
        }

        // The 'auto' modifier reads ISO text, Unix seconds and Julian days alike, matching ReadTimestamp,
        // so the pre-filter never discards a row the client-side check would keep.
        if (options.From is { } from)
        {
            conditions.Add($"datetime(t.{QuoteIdentifier(this.attributeColumns[EndTimeOrdinal - MmsiOrdinal]!)}, 'auto') >= datetime(?)");
            parameters.Add(from.UtcDateTime.ToString(SqlDateTimeFormat, CultureInfo.InvariantCulture));
        }

        if (options.To is { } to)
        {
            conditions.Add($"datetime(t.{QuoteIdentifier(this.attributeColumns[StartTimeOrdinal - MmsiOrdinal]!)}, 'auto') <= datetime(?)");
            parameters.Add(to.UtcDateTime.ToString(SqlDateTimeFormat, CultureInfo.InvariantCulture));
        }

        if (conditions.Count > 0)
        {
            sql.Append(" where ").AppendJoin(" and ", conditions);
        }

        sql.Append(" order by t.").Append(QuoteIdentifier(this.idColumn));

        int result = raw.sqlite3_prepare_v2(database, sql.ToString(), out sqlite3_stmt statement);
        if (result != raw.SQLITE_OK)
        {
            statement.Dispose();
            SqliteException.ThrowExceptionForRC(result, database);
        }

        for (int i = 0; i < parameters.Count; i++)
        {
            result = parameters[i] switch
            {
                double number => raw.sqlite3_bind_double(statement, i + 1, number),
                long number => raw.sqlite3_bind_int64(statement, i + 1, number),
                string text => raw.sqlite3_bind_text(statement, i + 1, text),
                _ => throw new InvalidOperationException("Unsupported parameter type."),
            };

            if (result != raw.SQLITE_OK)
            {
                statement.Dispose();
                SqliteException.ThrowExceptionForRC(result, database);
            }
        }

        return statement;
    }

    private VesselTrack? ReadTrack(sqlite3_stmt statement, VesselTrackReaderOptions options, RecentStrings strings)
    {
        if (IsNull(statement, GeometryOrdinal))
        {
            return null;
        }

        long id = raw.sqlite3_column_int64(statement, IdOrdinal);
        Position[] points = GeoPackageGeometry.DecodeLine(raw.sqlite3_column_blob(statement, GeometryOrdinal), out BoundingBox envelope);
        if (points.Length == 0)
        {
            return null;
        }

        if (options.Bounds is { } bounds && !bounds.Intersects(envelope))
        {
            return null;
        }

        DateTimeOffset start = ReadTimestamp(statement, StartTimeOrdinal, id, "start time");
        DateTimeOffset end = ReadTimestamp(statement, EndTimeOrdinal, id, "end time");
        if ((options.From is { } from && end < from) || (options.To is { } to && start > to))
        {
            return null;
        }

        long mmsiValue = raw.sqlite3_column_int64(statement, MmsiOrdinal);
        if (mmsiValue == -1)
        {
            throw new InvalidDataException($"Row {id} has no MMSI.");
        }

        if (mmsiValue is < 0 or > uint.MaxValue)
        {
            throw new InvalidDataException($"Row {id} has MMSI {mmsiValue}, which is out of range.");
        }

        uint? vesselType = ReadUInt32(statement, VesselTypeOrdinal);
        uint? cargo = ReadUInt32(statement, CargoOrdinal);
        uint? chosenType = vesselType ?? cargo;
        uint? navigationStatus = ReadUInt32(statement, NavigationStatusOrdinal);
        ReadOnlySpan<byte> transceiver = ReadText(statement, TransceiverOrdinal).Trim((byte)' ');
        double draught = raw.sqlite3_column_double(statement, DraughtOrdinal);

        return new VesselTrack(id, (uint)mmsiValue, start, end, points)
        {
            Duration = ReadUInt32(statement, DurationOrdinal) is { } minutes ? TimeSpan.FromMinutes(minutes) : end - start,
            Envelope = envelope,
            VesselName = strings.Get(0, ReadText(statement, VesselNameOrdinal)),
            CallSign = strings.Get(1, ReadText(statement, CallSignOrdinal)),
            ImoNumber = ParseImoNumber(ReadText(statement, ImoOrdinal)),
            ShipType = chosenType is <= 99 ? (ShipType)chosenType : ShipType.NotAvailable,
            ShipTypeName = strings.Get(2, ReadText(statement, VesselTypeNameOrdinal)),
            NavigationStatus = navigationStatus is <= 15 ? (NavigationStatus)navigationStatus : NavigationStatus.NotDefined,
            Length = ReadUInt32(statement, LengthOrdinal),
            Width = ReadUInt32(statement, WidthOrdinal),
            Draught = draught < 0 ? null : draught,
            Cargo = cargo,
            IsClassB = transceiver.Length == 1 && (transceiver[0] | 0x20) == 'b',
        };
    }

    private static bool IsNumericOrdinal(int ordinal) =>
        ordinal is MmsiOrdinal or VesselTypeOrdinal or NavigationStatusOrdinal or LengthOrdinal or WidthOrdinal or DraughtOrdinal or CargoOrdinal or DurationOrdinal;

    private static bool IsNull(sqlite3_stmt statement, int ordinal) =>
        raw.sqlite3_column_type(statement, ordinal) == raw.SQLITE_NULL;

    private static DateTimeOffset ReadTimestamp(sqlite3_stmt statement, int ordinal, long id, string role)
    {
        switch (raw.sqlite3_column_type(statement, ordinal))
        {
            case raw.SQLITE_TEXT:
            {
                ReadOnlySpan<byte> text = raw.sqlite3_column_blob(statement, ordinal);
                if (TryParseTimestamp(text, out DateTimeOffset parsed))
                {
                    return parsed;
                }

                string fallback = Encoding.UTF8.GetString(text);
                if (DateTimeOffset.TryParse(fallback, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
                {
                    return parsed;
                }

                throw new InvalidDataException($"Row {id} has an unreadable {role}: '{fallback}'.");
            }

            case raw.SQLITE_INTEGER:
                return DateTimeOffset.FromUnixTimeSeconds(raw.sqlite3_column_int64(statement, ordinal));

            case raw.SQLITE_FLOAT:
                return DateTimeOffset.UnixEpoch.AddSeconds((raw.sqlite3_column_double(statement, ordinal) - 2440587.5) * 86400);

            case raw.SQLITE_NULL:
                throw new InvalidDataException($"Row {id} has no {role}.");

            default:
                throw new InvalidDataException($"Row {id} has an unreadable {role}.");
        }
    }

    // sqlite3_column_blob on a TEXT value returns its UTF-8 bytes without conversion or copying, and an
    // empty span for null, which is all a missing text means here.
    private static ReadOnlySpan<byte> ReadText(sqlite3_stmt statement, int ordinal) =>
        raw.sqlite3_column_blob(statement, ordinal);

    /// <summary>
    /// Turns text columns into strings, handing back the previous row's string when the bytes are the
    /// same. The files are ordered by vessel, so a vessel's name, call sign and type name repeat over
    /// its consecutive tracks and most rows allocate no strings at all.
    /// </summary>
    private sealed class RecentStrings
    {
        private readonly byte[][] recentBytes = [new byte[64], new byte[64], new byte[64]];
        private readonly int[] recentLengths = new int[3];
        private readonly string[] recentStrings = [string.Empty, string.Empty, string.Empty];

        public string Get(int slot, ReadOnlySpan<byte> text)
        {
            if (text.IsEmpty)
            {
                return string.Empty;
            }

            if (text.SequenceEqual(this.recentBytes[slot].AsSpan(0, this.recentLengths[slot])))
            {
                return this.recentStrings[slot];
            }

            // A miss costs the string and nothing else: the bytes are kept in a buffer that only grows.
            string value = Encoding.UTF8.GetString(text);
            if (text.Length > this.recentBytes[slot].Length)
            {
                this.recentBytes[slot] = new byte[Math.Max(text.Length, 2 * this.recentBytes[slot].Length)];
            }

            text.CopyTo(this.recentBytes[slot]);
            this.recentLengths[slot] = text.Length;
            this.recentStrings[slot] = value;
            return value;
        }
    }

    // Numeric columns are selected through ifnull(..., -1), so -1 is null and no type probe is needed.
    private static uint? ReadUInt32(sqlite3_stmt statement, int ordinal)
    {
        long value = raw.sqlite3_column_int64(statement, ordinal);
        return value is >= 0 and <= uint.MaxValue ? (uint)value : null;
    }

    private static uint? ParseImoNumber(ReadOnlySpan<byte> text)
    {
        ReadOnlySpan<byte> digits = text.Trim((byte)' ');
        if (digits.Length >= 3 && (digits[0] | 0x20) == 'i' && (digits[1] | 0x20) == 'm' && (digits[2] | 0x20) == 'o')
        {
            digits = digits[3..];
        }

        return uint.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out uint imo) && imo != 0 ? imo : null;
    }

    private static object? Scalar(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command.ExecuteScalar();
    }

    private static string QuoteIdentifier(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string Normalise(string name) => name.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}
