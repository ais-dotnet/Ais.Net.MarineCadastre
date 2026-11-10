using System.Buffers;
using System.Buffers.Binary;

using Ais.Net.Models.Abstractions;

namespace Ais.Net.MarineCadastre.TestSupport;

/// <summary>
/// Encodes line geometries in the GeoPackage binary format, so that tests and benchmarks can build
/// fixtures in every variation the reader must accept. Shared between the two projects as a linked file.
/// </summary>
public static class GeoPackageGeometryEncoder
{
    private const uint LineStringType = 2;
    private const uint MultiLineStringType = 5;

    // PostGIS-style flags some writers put in the WKB type word instead of adding 1000/2000/3000.
    private const uint ExtendedZFlag = 0x80000000;
    private const uint ExtendedMFlag = 0x40000000;

    /// <summary>
    /// Encodes line parts as a GeoPackage geometry.
    /// </summary>
    /// <param name="parts">The line parts. One part is written as a LineString unless <paramref name="forceMulti"/> is set.</param>
    /// <param name="srsId">The spatial reference system id for the header.</param>
    /// <param name="bigEndian">Set to write the header and the WKB big-endian.</param>
    /// <param name="envelopeIndicator">The envelope indicator (0 to 4) for the header. The envelope is filled from the parts.</param>
    /// <param name="forceMulti">Set to write a MultiLineString even for one part.</param>
    /// <param name="withZ">Set to add a Z ordinate (written as zero).</param>
    /// <param name="withM">Set to add an M ordinate (written as zero).</param>
    /// <param name="extendedFlags">Set to signal Z and M with PostGIS-style flags instead of ISO type offsets.</param>
    /// <param name="empty">Set to set the empty flag. The WKB is still written.</param>
    /// <returns>The encoded geometry.</returns>
    public static byte[] Encode(
        IReadOnlyList<IReadOnlyList<Position>> parts,
        int srsId = 4326,
        bool bigEndian = false,
        int envelopeIndicator = 0,
        bool forceMulti = true,
        bool withZ = false,
        bool withM = false,
        bool extendedFlags = false,
        bool empty = false)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(envelopeIndicator, 4);

        ArrayBufferWriter<byte> output = new();
        byte flags = (byte)((bigEndian ? 0 : 1) | (envelopeIndicator << 1) | (empty ? 0x10 : 0));
        WriteByte(output, (byte)'G');
        WriteByte(output, (byte)'P');
        WriteByte(output, 0);
        WriteByte(output, flags);
        WriteInt32(output, srsId, bigEndian);

        if (envelopeIndicator > 0)
        {
            Position[] all = [.. parts.SelectMany(p => p)];
            BoundingBox box = all.Length == 0 ? default : BoundingBox.FromPositions(all);
            WriteDouble(output, box.West, bigEndian);
            WriteDouble(output, box.East, bigEndian);
            WriteDouble(output, box.South, bigEndian);
            WriteDouble(output, box.North, bigEndian);
            int extraPairs = envelopeIndicator switch { 2 or 3 => 1, 4 => 2, _ => 0 };
            for (int i = 0; i < extraPairs * 2; i++)
            {
                WriteDouble(output, 0, bigEndian);
            }
        }

        uint typeOffset = extendedFlags ? 0 : (uint)((withZ ? 1000 : 0) + (withM ? 2000 : 0));
        uint typeFlags = extendedFlags ? (withZ ? ExtendedZFlag : 0) | (withM ? ExtendedMFlag : 0) : 0;

        if (forceMulti || parts.Count != 1)
        {
            WriteByte(output, (byte)(bigEndian ? 0 : 1));
            WriteUInt32(output, (MultiLineStringType + typeOffset) | typeFlags, bigEndian);
            WriteUInt32(output, (uint)parts.Count, bigEndian);
        }

        foreach (IReadOnlyList<Position> part in parts)
        {
            WriteByte(output, (byte)(bigEndian ? 0 : 1));
            WriteUInt32(output, (LineStringType + typeOffset) | typeFlags, bigEndian);
            WriteUInt32(output, (uint)part.Count, bigEndian);
            foreach (Position position in part)
            {
                WriteDouble(output, position.Longitude, bigEndian);
                WriteDouble(output, position.Latitude, bigEndian);
                if (withZ)
                {
                    WriteDouble(output, 0, bigEndian);
                }

                if (withM)
                {
                    WriteDouble(output, 0, bigEndian);
                }
            }
        }

        return output.WrittenSpan.ToArray();
    }

    private static void WriteByte(ArrayBufferWriter<byte> output, byte value)
    {
        output.GetSpan(1)[0] = value;
        output.Advance(1);
    }

    private static void WriteInt32(ArrayBufferWriter<byte> output, int value, bool bigEndian)
    {
        Span<byte> span = output.GetSpan(sizeof(int));
        if (bigEndian)
        {
            BinaryPrimitives.WriteInt32BigEndian(span, value);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(span, value);
        }

        output.Advance(sizeof(int));
    }

    private static void WriteUInt32(ArrayBufferWriter<byte> output, uint value, bool bigEndian) =>
        WriteInt32(output, unchecked((int)value), bigEndian);

    private static void WriteDouble(ArrayBufferWriter<byte> output, double value, bool bigEndian)
    {
        Span<byte> span = output.GetSpan(sizeof(double));
        if (bigEndian)
        {
            BinaryPrimitives.WriteDoubleBigEndian(span, value);
        }
        else
        {
            BinaryPrimitives.WriteDoubleLittleEndian(span, value);
        }

        output.Advance(sizeof(double));
    }
}
