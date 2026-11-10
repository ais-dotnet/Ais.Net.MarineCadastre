// <copyright file="GeoPackageGeometry.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Buffers.Binary;
using System.Runtime.InteropServices;

using Ais.Net.Models.Abstractions;

namespace Ais.Net.MarineCadastre.GeoPackage;

/// <summary>
/// Decodes the GeoPackage binary geometry format: an 8-byte header, an optional envelope, then ISO
/// well-known binary. It handles only line geometries, because a tracks file contains nothing else.
/// </summary>
internal static class GeoPackageGeometry
{
    private const int HeaderLength = 8;

    // The srs_id every GeoPackage must define for WGS84 longitude and latitude (EPSG:4326).
    private const int Wgs84SrsId = 4326;
    private const uint LineStringType = 2;
    private const uint MultiLineStringType = 5;

    // PostGIS-style flags some writers put in the WKB type word instead of adding 1000/2000/3000.
    private const uint ExtendedZFlag = 0x80000000;
    private const uint ExtendedMFlag = 0x40000000;
    private const uint ExtendedSridFlag = 0x20000000;

    /// <summary>
    /// Decodes a LineString or MultiLineString into positions. The decoder concatenates the parts of a
    /// MultiLineString in order and drops the Z and M ordinates.
    /// </summary>
    /// <param name="blob">The geometry column value.</param>
    /// <param name="envelope">The smallest box that contains the positions, or default if there are none.</param>
    /// <returns>The positions, in storage order.</returns>
    /// <exception cref="InvalidDataException">This exception occurs if the blob is not a GeoPackage line geometry.</exception>
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

        // Positions are returned as WGS84 degrees, so a geometry in any other coordinate system would be
        // misread: a Web Mercator point a few metres from the origin passes every range check. The header
        // carries the id in its own byte order (bit 0 of the flags), which need not be that of the WKB.
        int srsId = (flags & 1) != 0
            ? BinaryPrimitives.ReadInt32LittleEndian(blob[4..])
            : BinaryPrimitives.ReadInt32BigEndian(blob[4..]);
        if (srsId != Wgs84SrsId)
        {
            throw new InvalidDataException($"The geometry uses spatial reference system {srsId}. Only WGS84 (srs_id {Wgs84SrsId}) is supported.");
        }

        ReadOnlySpan<byte> wkb = blob[(HeaderLength + envelopeLength)..];

        int offset = 0;
        int count = 0;
        Walk(wkb, ref offset, Span<Position>.Empty, ref count, countOnly: true, insideMulti: false);

        // Every element is assigned by the fill pass, so skip the zeroing: on a month of tracks that is
        // 3.5 GB of memory the runtime would otherwise clear first.
        Position[] points = GC.AllocateUninitializedArray<Position>(count);
        offset = 0;
        int written = 0;
        Walk(wkb, ref offset, points, ref written, countOnly: false, insideMulti: false);

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

    private static void Walk(ReadOnlySpan<byte> wkb, ref int offset, Span<Position> target, ref int written, bool countOnly, bool insideMulti)
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
                    // WKB does not nest MultiLineStrings. Rejecting one here also bounds the recursion at
                    // one level: each nested level costs only 9 bytes, so a crafted blob could otherwise
                    // exhaust the stack, which ends the process rather than raising an exception.
                    if (insideMulti)
                    {
                        throw new InvalidDataException("A MultiLineString contains another MultiLineString. Its parts must be LineStrings.");
                    }

                    Require(wkb, offset, 4);
                    int parts = ReadCount(wkb, offset, littleEndian);
                    offset += 4;
                    for (int i = 0; i < parts; i++)
                    {
                        Walk(wkb, ref offset, target, ref written, countOnly, insideMulti: true);
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
}
