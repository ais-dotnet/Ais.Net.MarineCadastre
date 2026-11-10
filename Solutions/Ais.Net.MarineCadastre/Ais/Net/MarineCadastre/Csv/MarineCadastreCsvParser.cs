// <copyright file="MarineCadastreCsvParser.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Buffers.Text;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

using Ais.Net.Models;
using Ais.Net.Models.Abstractions;

namespace Ais.Net.MarineCadastre.Csv;

/// <summary>
/// Converts MarineCadastre CSV records into Ais.Net.Models messages. The header line fixes the column
/// positions. Each subsequent line gives a position report. If the static columns of the vessel are new
/// or changed, the line also gives a static-data message.
/// </summary>
/// <remarks>
/// <para>
/// A CSV row is a denormalised join of a position report and the static data of the vessel. Thus a row
/// maps to a Class A message type 1 (or 2 or 3) or to a Class B message type 18. For Class A, the
/// static columns become a message type 5.
/// </para>
/// <para>
/// For Class B, the model of message type 24 part 0 has no field for the name. Thus, on a static
/// change, the parser returns the position of the row as a message type 19. That message carries the
/// name, the ship type and the dimensions. A message type 24 part 1 follows it with the call sign.
/// </para>
/// <para>
/// A real AIS message has fields that the CSV does not have (rate of turn, RAIM, radio status, ETA,
/// destination and more). The parser sets each of these fields to the AIS "not available" value for
/// that field.
/// </para>
/// </remarks>
internal sealed class MarineCadastreCsvParser(MarineCadastreCsvReaderOptions options)
{
    /// <summary>The most fields a record is expected to have; extra columns are ignored.</summary>
    internal const int MaxFields = 32;

    private const uint HeadingNotAvailable = 511;
    private const int RateOfTurnNotAvailable = -128;

    // The AIS "not available" values for speed (1023 tenths of a knot) and course (3600 tenths of a
    // degree). Older MarineCadastre files write them out; newer ones leave the field empty.
    private const double SpeedNotAvailable = 102.3;
    private const double CourseNotAvailable = 360;

    /// <summary>10^0 to 10^22, every one exactly representable as a double.</summary>
    private static readonly double[] PowersOfTen =
    [
        1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11,
        1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
    ];

    private readonly Dictionary<uint, ulong> staticSignatures = [];
    private Columns columns;
    private bool headerParsed;

    // The 1-based physical line number of the line being parsed; the header is line 1.
    private long lineNumber;

    // A daily file has one date on every row, so the date part is validated and converted to ticks
    // once and each row only adds its time of day.
    private int cachedDateKey = -1;
    private long cachedDateTicks;

    /// <summary>Gets the number of data rows parsed so far.</summary>
    public long RowCount { get; private set; }

    /// <summary>Gets the number of rows that could not be parsed and were skipped.</summary>
    public long SkippedRowCount { get; private set; }

    /// <summary>
    /// Parses one record. The first record must be the header. Returns the position report (if any)
    /// and the static-data message (if any) the row produced.
    /// </summary>
    public void ParseLine(Span<byte> line, out AisMessageBase? position, out AisMessageBase? staticData)
    {
        this.lineNumber++;
        position = null;
        staticData = null;

        Span<CsvField> fields = stackalloc CsvField[MaxFields];
        int fieldCount = CsvLine.Split(line, fields);

        // From here on, fields holds only the fields the line has, so a column index past the end of a
        // short row is out of range rather than a stale or zeroed slot.
        fields = fields[..Math.Min(fieldCount, MaxFields)];

        if (!this.headerParsed)
        {
            this.columns = Columns.FromHeader(line, fields);
            this.headerParsed = true;
            return;
        }

        if (fieldCount == 1 && fields[0].Length == 0)
        {
            // Blank line.
            return;
        }

        if (!this.TryParseRow(line, fields, fieldCount, out Row row, out CsvRowSkipReason reason))
        {
            this.SkippedRowCount++;
            options.OnRowSkipped?.Invoke(new SkippedCsvRow(this.lineNumber, reason, line));
            return;
        }

        this.RowCount++;

        bool emitStatic = options.EmitStaticData && this.IsNewStaticData(line, fields, row.Mmsi);

        if (row.IsClassB)
        {
            if (emitStatic)
            {
                position = this.CreateType19(line, fields, in row);
                staticData = this.CreateType24Part1(line, fields, in row);
            }
            else
            {
                position = CreateType18(in row);
            }
        }
        else
        {
            position = this.CreateType1Through3(in row);
            if (emitStatic)
            {
                staticData = this.CreateType5(line, fields, in row);
            }
        }
    }

    private static AisMessageType18 CreateType18(in Row row) =>
        new(
            CanAcceptMessage22ChannelAssignment: false,
            CanSwitchBands: false,
            CourseOverGround: row.CourseOverGround,
            CsUnit: ClassBUnit.Sotdma,
            HasDisplay: false,
            IsAssigned: false,
            IsDscAttached: false,
            Mmsi: row.Mmsi,
            Position: row.Position,
            PositionAccuracy: false,
            RadioStatusType: ClassBRadioStatusType.Sotdma,
            RaimFlag: false,
            RegionalReserved139: 0,
            RegionalReserved38: 0,
            RepeatIndicator: 0,
            SpeedOverGround: row.SpeedOverGround,
            TimeStampSecond: row.TimeStampSecond,
            TrueHeadingDegrees: row.Heading)
        {
            Timestamp = row.Timestamp,
        };

    private static string GetText(ReadOnlySpan<byte> line, ReadOnlySpan<CsvField> fields, int column)
    {
        if (column < 0 || column >= fields.Length)
        {
            return string.Empty;
        }

        ReadOnlySpan<byte> bytes = fields[column].In(line).Trim((byte)' ');
        return bytes.IsEmpty ? string.Empty : Encoding.UTF8.GetString(bytes);
    }

    private static ReadOnlySpan<byte> GetBytes(ReadOnlySpan<byte> line, ReadOnlySpan<CsvField> fields, int column) =>
        column < 0 || column >= fields.Length ? default : fields[column].In(line);

    /// <summary>
    /// Parses a decimal number. MarineCadastre writes plain fixed-point values ("-122.50573", "0.0",
    /// "14"). These take a fast path: the parser accumulates the digits as an integer and divides one
    /// time by a power of ten. With a maximum of 15 significant digits, the integer is below 2^53, and
    /// the power of ten up to 10^22 is exact. Thus that one division is correctly rounded (Clinger's
    /// fast path). The result is bit-identical to <see cref="Utf8Parser"/>, which handles all the other
    /// cases.
    /// </summary>
    internal static bool TryParseDouble(ReadOnlySpan<byte> bytes, out double value)
    {
        value = 0;
        if (bytes.IsEmpty)
        {
            return false;
        }

        int i = 0;
        bool negative = false;
        if (bytes[0] == '-')
        {
            negative = true;
            i = 1;
        }

        long mantissa = 0;
        int significantDigits = 0;
        int fractionDigits = 0;
        bool inFraction = false;
        bool anyDigit = false;

        for (; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            uint digit = (uint)(b - '0');
            if (digit <= 9)
            {
                anyDigit = true;
                if (mantissa != 0 || digit != 0)
                {
                    if (++significantDigits > 15)
                    {
                        return TryParseDoubleSlow(bytes, out value);
                    }
                }

                mantissa = (mantissa * 10) + digit;
                if (inFraction)
                {
                    fractionDigits++;
                }
            }
            else if (b == '.' && !inFraction)
            {
                inFraction = true;
            }
            else
            {
                return TryParseDoubleSlow(bytes, out value);
            }
        }

        if (!anyDigit || fractionDigits > 22)
        {
            return TryParseDoubleSlow(bytes, out value);
        }

        double result = mantissa;
        if (fractionDigits > 0)
        {
            result /= PowersOfTen[fractionDigits];
        }

        value = negative ? -result : result;
        return true;
    }

    private static bool TryParseDoubleSlow(ReadOnlySpan<byte> bytes, out double value) =>
        Utf8Parser.TryParse(bytes, out value, out int consumed) && consumed == bytes.Length;

    private static bool TryParseInt(ReadOnlySpan<byte> bytes, out int value) =>
        Utf8Parser.TryParse(bytes, out value, out int consumed) && consumed == bytes.Length;

    /// <summary>
    /// Parses a speed or course. The result is <see langword="null"/> if the field is empty or
    /// unreadable, or if the value is negative, NaN, or at or above <paramref name="notAvailable"/>.
    /// </summary>
    private static float? ParseOptionalFloat(ReadOnlySpan<byte> bytes, double notAvailable) =>
        !bytes.IsEmpty && TryParseDouble(bytes, out double value) && value >= 0 && value < notAvailable ? (float)value : null;

    private static int ParseOptionalInt(ReadOnlySpan<byte> bytes, int missing)
    {
        if (bytes.IsEmpty)
        {
            return missing;
        }

        // Integer columns are occasionally written as "12.0".
        if (TryParseInt(bytes, out int value))
        {
            return value;
        }

        return TryParseDouble(bytes, out double asDouble) ? (int)Math.Round(asDouble) : missing;
    }

    private static uint ParseImoNumber(ReadOnlySpan<byte> bytes)
    {
        bytes = bytes.Trim((byte)' ');
        if (bytes.Length >= 3 && (bytes[0] | 0x20) == 'i' && (bytes[1] | 0x20) == 'm' && (bytes[2] | 0x20) == 'o')
        {
            bytes = bytes[3..];
        }

        return Utf8Parser.TryParse(bytes, out uint value, out int consumed) && consumed == bytes.Length ? value : 0;
    }

    /// <summary>
    /// Parses <c>yyyy-MM-dd HH:mm:ss</c> (or with a <c>T</c> separator, optionally followed by a
    /// fraction, a <c>Z</c> or both) as UTC, which is what MarineCadastre writes. The fraction is
    /// dropped. Any other text after the seconds, such as a UTC offset, makes the value unreadable:
    /// to ignore it would give the wrong time.
    /// </summary>
    private bool TryParseTimestamp(ReadOnlySpan<byte> bytes, out DateTimeOffset value)
    {
        value = default;
        if (bytes.Length < 19
            || bytes[4] != '-' || bytes[7] != '-' || (bytes[10] != ' ' && bytes[10] != 'T') || bytes[13] != ':' || bytes[16] != ':')
        {
            return false;
        }

        if (!TryDigits(bytes[..4], out int year)
            || !TryDigits(bytes.Slice(5, 2), out int month)
            || !TryDigits(bytes.Slice(8, 2), out int day)
            || !TryDigits(bytes.Slice(11, 2), out int hour)
            || !TryDigits(bytes.Slice(14, 2), out int minute)
            || !TryDigits(bytes.Slice(17, 2), out int second))
        {
            return false;
        }

        if (hour > 23 || minute > 59 || second > 59 || !IsTimestampTail(bytes[19..]))
        {
            return false;
        }

        int dateKey = (year * 10000) + (month * 100) + day;
        if (dateKey != this.cachedDateKey)
        {
            if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            {
                return false;
            }

            this.cachedDateTicks = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc).Ticks;
            this.cachedDateKey = dateKey;
        }

        long ticks = this.cachedDateTicks + ((((hour * 60) + minute) * 60) + second) * TimeSpan.TicksPerSecond;
        value = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;

        static bool IsTimestampTail(ReadOnlySpan<byte> tail)
        {
            if (!tail.IsEmpty && tail[0] == '.')
            {
                int digits = tail[1..].IndexOfAnyExceptInRange((byte)'0', (byte)'9');
                if (digits == 0 || tail.Length == 1)
                {
                    return false;
                }

                tail = digits < 0 ? default : tail[(1 + digits)..];
            }

            return tail.IsEmpty || (tail.Length == 1 && tail[0] == 'Z');
        }

        static bool TryDigits(ReadOnlySpan<byte> digits, out int result)
        {
            result = 0;
            foreach (byte b in digits)
            {
                uint d = (uint)(b - '0');
                if (d > 9)
                {
                    return false;
                }

                result = (result * 10) + (int)d;
            }

            return true;
        }
    }

    private bool TryParseRow(ReadOnlySpan<byte> line, ReadOnlySpan<CsvField> fields, int fieldCount, out Row row, out CsvRowSkipReason reason)
    {
        row = default;
        if (fieldCount <= this.columns.Highest)
        {
            reason = CsvRowSkipReason.TooFewFields;
            return false;
        }

        ReadOnlySpan<byte> mmsiBytes = fields[this.columns.Mmsi].In(line).Trim((byte)' ');
        if (!Utf8Parser.TryParse(mmsiBytes, out uint mmsi, out int mmsiConsumed) || mmsiConsumed != mmsiBytes.Length)
        {
            reason = CsvRowSkipReason.InvalidMmsi;
            return false;
        }

        if (!this.TryParseTimestamp(fields[this.columns.BaseDateTime].In(line), out DateTimeOffset timestamp))
        {
            reason = CsvRowSkipReason.InvalidTimestamp;
            return false;
        }

        reason = default;

        Position? position = null;
        if (TryParseDouble(fields[this.columns.Latitude].In(line), out double latitude)
            && TryParseDouble(fields[this.columns.Longitude].In(line), out double longitude)
            && Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180)
        {
            position = new Position(latitude, longitude);
        }

        int heading = ParseOptionalInt(GetBytes(line, fields, this.columns.Heading), (int)HeadingNotAvailable);
        int status = ParseOptionalInt(GetBytes(line, fields, this.columns.Status), (int)NavigationStatus.NotDefined);
        ReadOnlySpan<byte> transceiver = GetBytes(line, fields, this.columns.Transceiver);

        row = new Row
        {
            Mmsi = mmsi,
            Timestamp = timestamp,
            Position = position,
            SpeedOverGround = ParseOptionalFloat(GetBytes(line, fields, this.columns.Sog), SpeedNotAvailable),
            CourseOverGround = ParseOptionalFloat(GetBytes(line, fields, this.columns.Cog), CourseNotAvailable),
            Heading = heading is >= 0 and < 360 ? (uint)heading : HeadingNotAvailable,
            NavigationStatus = status is >= 0 and <= 15 ? (NavigationStatus)status : NavigationStatus.NotDefined,
            IsClassB = !transceiver.IsEmpty && (transceiver[0] | 0x20) == 'b',
        };
        return true;
    }

    /// <summary>
    /// Hashes the static columns of the row and compares the hash with the last value for the MMSI.
    /// Thus the parser returns the static data of a vessel one time, not on each of its position rows.
    /// </summary>
    private bool IsNewStaticData(ReadOnlySpan<byte> line, ReadOnlySpan<CsvField> fields, uint mmsi)
    {
        // A 64-bit multiply-mix over the raw bytes of each static column, eight bytes at a time, with
        // each column's length folded in so that moving bytes between adjacent columns changes the
        // hash. It only has to be stable within one read, not across runs or machines.
        const ulong Seed = 14695981039346656037;
        const ulong Prime = 0x9E3779B97F4A7C15;

        ulong hash = Seed;
        bool anyContent = false;
        foreach (int column in this.columns.StaticColumns)
        {
            int length = 0;
            if (column >= 0 && column < fields.Length)
            {
                ReadOnlySpan<byte> bytes = fields[column].In(line);
                length = bytes.Length;
                while (bytes.Length >= sizeof(ulong))
                {
                    hash = BitOperations.RotateLeft((hash ^ MemoryMarshal.Read<ulong>(bytes)) * Prime, 29);
                    bytes = bytes[sizeof(ulong)..];
                }

                if (!bytes.IsEmpty)
                {
                    ulong tail = 0;
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        tail |= (ulong)bytes[i] << (8 * i);
                    }

                    hash = BitOperations.RotateLeft((hash ^ tail) * Prime, 29);
                }
            }

            anyContent |= length > 0;
            hash = (hash ^ (ulong)(length + 1)) * Prime;
        }

        if (!anyContent)
        {
            return false;
        }

        ref ulong known = ref CollectionsMarshal.GetValueRefOrAddDefault(this.staticSignatures, mmsi, out bool exists);
        if (exists && known == hash)
        {
            return false;
        }

        known = hash;
        return true;
    }

    private AisMessageType1Through3 CreateType1Through3(in Row row) =>
        new(
            CourseOverGround: row.CourseOverGround,
            ManoeuvreIndicator: ManoeuvreIndicator.NotAvailable,
            MessageType: options.ClassAMessageType,
            Mmsi: row.Mmsi,
            NavigationStatus: row.NavigationStatus,
            Position: row.Position,
            PositionAccuracy: false,
            RadioSlotTimeout: 0,
            RadioSubMessage: 0,
            RadioSyncState: RadioSyncState.UtcDirect,
            RateOfTurn: RateOfTurnNotAvailable,
            RaimFlag: false,
            RepeatIndicator: 0,
            SpareBits145: 0,
            SpeedOverGround: row.SpeedOverGround,
            TimeStampSecond: row.TimeStampSecond,
            TrueHeadingDegrees: row.Heading)
        {
            Timestamp = row.Timestamp,
        };

    private AisMessageType5 CreateType5(ReadOnlySpan<byte> line, ReadOnlySpan<CsvField> fields, in Row row)
    {
        StaticColumns statics = this.ReadStaticColumns(line, fields);
        return new AisMessageType5(
            AisVersion: 0,
            CallSign: GetText(line, fields, this.columns.CallSign),
            Destination: string.Empty,
            DimensionToBow: statics.Length,
            DimensionToPort: statics.Width,
            DimensionToStarboard: 0,
            DimensionToStern: 0,
            Draught10thMetres: statics.Draught10thMetres,
            EtaDay: 0,
            EtaHour: 0,
            EtaMinute: 0,
            EtaMonth: 0,
            IsDteNotReady: false,
            ImoNumber: ParseImoNumber(GetBytes(line, fields, this.columns.Imo)),
            Mmsi: row.Mmsi,
            PositionFixType: EpfdFixType.Undefined,
            RepeatIndicator: 0,
            ShipType: statics.ShipType,
            Spare423: 0,
            VesselName: GetText(line, fields, this.columns.VesselName))
        {
            Timestamp = row.Timestamp,
        };
    }

    private AisMessageType19 CreateType19(ReadOnlySpan<byte> line, ReadOnlySpan<CsvField> fields, in Row row)
    {
        StaticColumns statics = this.ReadStaticColumns(line, fields);
        return new AisMessageType19(
            CourseOverGround: row.CourseOverGround,
            DimensionToBow: statics.Length,
            DimensionToPort: statics.Width,
            DimensionToStarboard: 0,
            DimensionToStern: 0,
            IsAssigned: false,
            IsDteNotReady: false,
            Mmsi: row.Mmsi,
            Position: row.Position,
            PositionAccuracy: false,
            PositionFixType: EpfdFixType.Undefined,
            RaimFlag: false,
            RegionalReserved139: 0,
            RegionalReserved38: 0,
            RepeatIndicator: 0,
            ShipName: GetText(line, fields, this.columns.VesselName),
            ShipType: statics.ShipType,
            Spare308: 0,
            SpeedOverGround: row.SpeedOverGround,
            TimeStampSecond: row.TimeStampSecond,
            TrueHeadingDegrees: row.Heading)
        {
            Timestamp = row.Timestamp,
        };
    }

    private AisMessageType24Part1 CreateType24Part1(ReadOnlySpan<byte> line, ReadOnlySpan<CsvField> fields, in Row row)
    {
        StaticColumns statics = this.ReadStaticColumns(line, fields);
        return new AisMessageType24Part1(
            CallSign: GetText(line, fields, this.columns.CallSign),
            DimensionToBow: statics.Length,
            DimensionToPort: statics.Width,
            DimensionToStarboard: 0,
            DimensionToStern: 0,
            Mmsi: row.Mmsi,
            MothershipMmsi: 0,
            PartNumber: 1,
            RepeatIndicator: 0,
            SerialNumber: 0,
            ShipType: statics.ShipType,
            Spare162: 0,
            UnitModelCode: 0,
            VendorIdRev3: string.Empty,
            VendorIdRev4: string.Empty)
        {
            Timestamp = row.Timestamp,
        };
    }

    private StaticColumns ReadStaticColumns(ReadOnlySpan<byte> line, ReadOnlySpan<CsvField> fields)
    {
        int vesselType = ParseOptionalInt(GetBytes(line, fields, this.columns.VesselType), -1);
        int cargo = ParseOptionalInt(GetBytes(line, fields, this.columns.Cargo), -1);
        int chosen = options.ShipTypeSource == ShipTypeSource.CargoThenVesselType
            ? (cargo >= 0 ? cargo : vesselType)
            : (vesselType >= 0 ? vesselType : cargo);

        int length = ParseOptionalInt(GetBytes(line, fields, this.columns.Length), 0);
        int width = ParseOptionalInt(GetBytes(line, fields, this.columns.Width), 0);

        ReadOnlySpan<byte> draftBytes = GetBytes(line, fields, this.columns.Draft);
        uint draught = !draftBytes.IsEmpty && TryParseDouble(draftBytes, out double draft) && draft >= 0
            ? (uint)Math.Round(draft * 10)
            : 0;

        return new StaticColumns(
            ShipType: chosen is >= 0 and <= 99 ? (ShipType)chosen : ShipType.NotAvailable,
            Length: length > 0 ? (uint)length : 0,
            Width: width > 0 ? (uint)width : 0,
            Draught10thMetres: draught);
    }

    /// <summary>The per-row values every message type needs.</summary>
    private struct Row
    {
        public uint Mmsi;
        public DateTimeOffset Timestamp;
        public Position? Position;
        public float? SpeedOverGround;
        public float? CourseOverGround;
        public uint Heading;
        public NavigationStatus NavigationStatus;
        public bool IsClassB;

        public readonly uint TimeStampSecond => (uint)this.Timestamp.Second;
    }

    private readonly record struct StaticColumns(ShipType ShipType, uint Length, uint Width, uint Draught10thMetres);

    /// <summary>Column positions resolved from the header, -1 when a column is absent.</summary>
    private readonly struct Columns
    {
        public readonly int Mmsi;
        public readonly int BaseDateTime;
        public readonly int Longitude;
        public readonly int Latitude;
        public readonly int Sog;
        public readonly int Cog;
        public readonly int Heading;
        public readonly int VesselName;
        public readonly int Imo;
        public readonly int CallSign;
        public readonly int VesselType;
        public readonly int Status;
        public readonly int Length;
        public readonly int Width;
        public readonly int Draft;
        public readonly int Cargo;
        public readonly int Transceiver;

        /// <summary>The highest index among the required columns; a row with fewer fields is skipped.</summary>
        public readonly int Highest;

        /// <summary>The columns whose change means a new static-data message.</summary>
        public readonly int[] StaticColumns;

        private Columns(ReadOnlySpan<int> indices)
        {
            this.Mmsi = indices[0];
            this.BaseDateTime = indices[1];
            this.Longitude = indices[2];
            this.Latitude = indices[3];
            this.Sog = indices[4];
            this.Cog = indices[5];
            this.Heading = indices[6];
            this.VesselName = indices[7];
            this.Imo = indices[8];
            this.CallSign = indices[9];
            this.VesselType = indices[10];
            this.Status = indices[11];
            this.Length = indices[12];
            this.Width = indices[13];
            this.Draft = indices[14];
            this.Cargo = indices[15];
            this.Transceiver = indices[16];
            this.Highest = Math.Max(Math.Max(this.Mmsi, this.BaseDateTime), Math.Max(this.Longitude, this.Latitude));
            this.StaticColumns = [this.VesselName, this.Imo, this.CallSign, this.VesselType, this.Length, this.Width, this.Draft, this.Cargo];
        }

        public static Columns FromHeader(ReadOnlySpan<byte> line, ReadOnlySpan<CsvField> fields)
        {
            // The names MarineCadastre has used across years, lower-cased with underscores removed:
            // "BaseDateTime" and "base_date_time" both resolve to "basedatetime".
            ReadOnlySpan<string> names =
            [
                "mmsi", "basedatetime", "lon", "lat", "sog", "cog", "heading", "vesselname", "imo",
                "callsign", "vesseltype", "status", "length", "width", "draft", "cargo", "transceiverclass",
            ];

            Span<int> indices = stackalloc int[names.Length];
            indices.Fill(-1);

            Span<char> normalised = stackalloc char[64];
            for (int i = 0; i < fields.Length; i++)
            {
                ReadOnlySpan<byte> raw = fields[i].In(line);
                int n = 0;
                foreach (byte b in raw)
                {
                    if (b == '_' || b == ' ' || n == normalised.Length)
                    {
                        continue;
                    }

                    normalised[n++] = char.ToLowerInvariant((char)b);
                }

                ReadOnlySpan<char> name = normalised[..n];
                for (int j = 0; j < names.Length; j++)
                {
                    if (indices[j] < 0 && Matches(name, names[j]))
                    {
                        indices[j] = i;
                        break;
                    }
                }
            }

            ReadOnlySpan<(int Index, string Name)> required = [(indices[0], "mmsi"), (indices[1], "base_date_time"), (indices[2], "longitude"), (indices[3], "latitude")];
            foreach ((int index, string name) in required)
            {
                if (index < 0)
                {
                    throw new FormatException($"The CSV header has no '{name}' column.");
                }
            }

            return new Columns(indices);

            static bool Matches(ReadOnlySpan<char> header, string expected) =>
                header.SequenceEqual(expected)
                || (expected == "lon" && (header.SequenceEqual("longitude") || header.SequenceEqual("long")))
                || (expected == "lat" && header.SequenceEqual("latitude"))
                || (expected == "transceiverclass" && header.SequenceEqual("transceiver"));
        }
    }
}
