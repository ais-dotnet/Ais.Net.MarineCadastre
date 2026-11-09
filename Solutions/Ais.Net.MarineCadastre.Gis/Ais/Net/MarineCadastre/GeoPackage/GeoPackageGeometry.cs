// <copyright file="GeoPackageGeometry.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

using Ais.Net.Models.Abstractions;

namespace Ais.Net.MarineCadastre.GeoPackage;

/// <summary>
/// Decodes (and, for tests, encodes) the GeoPackage binary geometry format: an 8-byte header, an
/// optional envelope, then ISO well-known binary. Only line geometries are handled, since that is all
/// a tracks file contains.
/// </summary>
internal static class GeoPackageGeometry
{
    private const int HeaderLength = 8;
    private const uint LineStringType = 2;
    private const uint MultiLineStringType = 5;

    // PostGIS-style flags some writers put in the WKB type word instead of adding 1000/2000/3000.
    private const uint ExtendedZFlag = 0x80000000;
    private const uint ExtendedMFlag = 0x40000000;
    private const uint ExtendedSridFlag = 0x20000000;

    /// <summary>
    /// Decodes a LineString or MultiLineString into positions. Parts of a MultiLineString are
    /// concatenated in order; Z and M ordinates are dropped.
    /// </summary>
    /// <param name="blob">The geometry column value.</param>
    /// <param name="envelope">The smallest box enclosing the positions; default when there are none.</param>
    /// <returns>The positions, in storage order.</returns>
    /// <exception cref="InvalidDataException">Thrown when the blob is not a GeoPackage line geometry.</exception>
    public static Position[] DecodeLine(ReadOnlySpan<byte> blob, out BoundingBox envelope)
    {
        if (blob.Length < HeaderLength || blob[0] != (byte)'G' || blob[1] != (byte)'P')
        {
            throw new InvalidDataException("The value is not a GeoPackage geometry: it does not start with the GP magic bytes.");
        }

        byte flags = blob[3];
        if ((flags & 0x20) != 0)
        {
            throw new InvalidDataException("Extended GeoPackage geometry encodings are not supported.");
        }

        int envelopeLength = ((flags >> 1) & 7) switch
        {
            0 => 0,
            1 => 32,
            2 or 3 => 48,
            4 => 64,
            _ => throw new InvalidDataException("The GeoPackage geometry header has an invalid envelope indicator."),
        };

        if ((flags & 0x10) != 0)
        {
            envelope = default;
            return [];
        }

        if (blob.Length < HeaderLength + envelopeLength)
        {
            throw new InvalidDataException("The GeoPackage geometry ends inside its envelope.");
        }

        ReadOnlySpan<byte> wkb = blob[(HeaderLength + envelopeLength)..];

        int offset = 0;
        int count = 0;
        Walk(wkb, ref offset, Span<Position>.Empty, ref count, countOnly: true);

        // Every element is assigned by the fill pass, so skip the zeroing: on a month of tracks that is
        // 3.5 GB of memory the runtime would otherwise clear first.
        Position[] points = GC.AllocateUninitializedArray<Position>(count);
        offset = 0;
        int written = 0;
        Walk(wkb, ref offset, points, ref written, countOnly: false);

        try
        {
            envelope = count == 0 ? default : BoundingBox.FromPositions(points);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("The geometry has a coordinate that is NaN or outside the WGS84 range.", ex);
        }

        return points;
    }

    /// <summary>
    /// Encodes line parts as a GeoPackage geometry. Used by specs and benchmarks to build fixtures.
    /// </summary>
    /// <param name="parts">The line parts; one part is written as a LineString unless <paramref name="forceMulti"/>.</param>
    /// <param name="srsId">The spatial reference system id for the header.</param>
    /// <param name="bigEndian">Whether to write the header and WKB big-endian.</param>
    /// <param name="envelopeIndicator">The envelope indicator (0 to 4) for the header; the envelope is filled from the parts.</param>
    /// <param name="forceMulti">Whether to write a MultiLineString even for one part.</param>
    /// <param name="withZ">Whether to add a Z ordinate (written as zero).</param>
    /// <param name="withM">Whether to add an M ordinate (written as zero).</param>
    /// <param name="extendedFlags">Whether to signal Z and M with PostGIS-style flags rather than ISO type offsets.</param>
    /// <param name="empty">Whether to set the empty flag; the WKB is still written.</param>
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

    private static void Walk(ReadOnlySpan<byte> wkb, ref int offset, Span<Position> target, ref int written, bool countOnly)
    {
        Require(wkb, offset, 5);
        bool littleEndian = wkb[offset] == 1;
        uint rawType = ReadUInt32(wkb, offset + 1, littleEndian);
        offset += 5;

        bool hasZ = (rawType & ExtendedZFlag) != 0;
        bool hasM = (rawType & ExtendedMFlag) != 0;
        if ((rawType & ExtendedSridFlag) != 0)
        {
            Require(wkb, offset, 4);
            offset += 4;
        }

        uint type = rawType & 0x0FFFFFFF;
        uint baseType = type % 1000;
        uint dimensionCode = type / 1000;
        hasZ |= dimensionCode is 1 or 3;
        hasM |= dimensionCode is 2 or 3;
        int stride = (2 + (hasZ ? 1 : 0) + (hasM ? 1 : 0)) * sizeof(double);

        switch (baseType)
        {
            case LineStringType:
            {
                Require(wkb, offset, 4);
                int count = ReadCount(wkb, offset, littleEndian);
                offset += 4;
                Require(wkb, offset, (long)count * stride);
                if (countOnly)
                {
                    offset += count * stride;
                    written += count;
                    break;
                }

                if (littleEndian == BitConverter.IsLittleEndian)
                {
                    // The file's byte order is the machine's, which is always so in practice: read the
                    // ordinates as doubles directly, with one bounds check per span rather than per value.
                    ReadOnlySpan<double> ordinates = MemoryMarshal.Cast<byte, double>(wkb.Slice(offset, count * stride));
                    int doublesPerPoint = stride / sizeof(double);
                    Span<Position> destination = target.Slice(written, count);
                    for (int i = 0, ordinate = 0; i < count; i++, ordinate += doublesPerPoint)
                    {
                        destination[i] = new Position(ordinates[ordinate + 1], ordinates[ordinate]);
                    }

                    written += count;
                    offset += count * stride;
                    break;
                }

                for (int i = 0; i < count; i++)
                {
                    double longitude = ReadDouble(wkb, offset, littleEndian);
                    double latitude = ReadDouble(wkb, offset + sizeof(double), littleEndian);
                    target[written++] = new Position(latitude, longitude);
                    offset += stride;
                }

                break;
            }

            case MultiLineStringType:
            {
                Require(wkb, offset, 4);
                int parts = ReadCount(wkb, offset, littleEndian);
                offset += 4;
                for (int i = 0; i < parts; i++)
                {
                    Walk(wkb, ref offset, target, ref written, countOnly);
                }

                break;
            }

            default:
                throw new InvalidDataException($"Expected a LineString or MultiLineString geometry but found WKB type {type}.");
        }
    }

    private static void Require(ReadOnlySpan<byte> wkb, int offset, long bytes)
    {
        if (offset + bytes > wkb.Length)
        {
            throw new InvalidDataException("The geometry ends before its declared content.");
        }
    }

    // A point or part count. The remaining bytes bound it, so a count beyond int.MaxValue is corrupt
    // data, not a size this code must support.
    private static int ReadCount(ReadOnlySpan<byte> wkb, int offset, bool littleEndian)
    {
        uint count = ReadUInt32(wkb, offset, littleEndian);
        if (count > int.MaxValue)
        {
            throw new InvalidDataException("The geometry declares more elements than it can hold.");
        }

        return (int)count;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..])
            : BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);

    private static double ReadDouble(ReadOnlySpan<byte> bytes, int offset, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadDoubleLittleEndian(bytes[offset..])
            : BinaryPrimitives.ReadDoubleBigEndian(bytes[offset..]);

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
