// <copyright file="CsvLine.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Ais.Net.MarineCadastre;

/// <summary>
/// Splits one CSV record into fields without allocating. Fields are returned as offsets into the line.
/// A quoted field has its quotes removed and any doubled quotes collapsed in place, which is why the
/// line is a writable <see cref="Span{T}"/>: the caller owns the buffer and reads each line once.
/// </summary>
internal static class CsvLine
{
    private const byte Comma = (byte)',';
    private const byte Quote = (byte)'"';

    /// <summary>
    /// Splits <paramref name="line"/> into at most <c>fields.Length</c> fields.
    /// </summary>
    /// <returns>The number of fields found, which can exceed <c>fields.Length</c>; the surplus are dropped.</returns>
    public static int Split(Span<byte> line, Span<CsvField> fields)
    {
        // Quoted fields are rare (a few hundred per million rows in MarineCadastre data), so decide
        // once per line with a vectorised search and keep the common path free of quote handling.
        if (line.IndexOf(Quote) < 0)
        {
            return Vector128.IsHardwareAccelerated && line.Length >= Vector128<byte>.Count
                ? SplitUnquotedVectorised(line, fields)
                : SplitUnquotedScalar(line, fields);
        }

        return SplitWithQuotes(line, fields);
    }

    private static int SplitUnquotedVectorised(ReadOnlySpan<byte> line, Span<CsvField> fields)
    {
        int count = 0;
        int start = 0;
        ref byte first = ref MemoryMarshal.GetReference(line);
        Vector128<byte> commas = Vector128.Create(Comma);

        // Find every comma 16 bytes at a time; each set bit in the mask is a field boundary.
        int offset = 0;
        int lastBlock = line.Length - Vector128<byte>.Count;
        while (offset <= lastBlock)
        {
            uint mask = Vector128.Equals(Vector128.LoadUnsafe(ref first, (nuint)offset), commas).ExtractMostSignificantBits();
            while (mask != 0)
            {
                int position = offset + BitOperations.TrailingZeroCount(mask);
                mask &= mask - 1;
                Record(fields, count++, start, position - start);
                start = position + 1;
            }

            offset += Vector128<byte>.Count;
        }

        // The tail that did not fill a vector.
        for (int i = offset; i < line.Length; i++)
        {
            if (Unsafe.Add(ref first, i) == Comma)
            {
                Record(fields, count++, start, i - start);
                start = i + 1;
            }
        }

        Record(fields, count++, start, line.Length - start);
        return count;
    }

    private static int SplitUnquotedScalar(ReadOnlySpan<byte> line, Span<CsvField> fields)
    {
        int count = 0;
        int start = 0;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == Comma)
            {
                Record(fields, count++, start, i - start);
                start = i + 1;
            }
        }

        Record(fields, count++, start, line.Length - start);
        return count;
    }

    private static int SplitWithQuotes(Span<byte> line, Span<CsvField> fields)
    {
        int count = 0;
        int position = 0;

        while (true)
        {
            if (position >= line.Length)
            {
                // The record ended with a separator (or is empty), so there is one more, empty, field.
                Record(fields, count++, position, 0);
                break;
            }

            if (line[position] == Quote)
            {
                int start = position + 1;
                int read = start;
                int write = start;
                bool closed = false;

                while (read < line.Length)
                {
                    byte b = line[read];
                    if (b == Quote)
                    {
                        if (read + 1 < line.Length && line[read + 1] == Quote)
                        {
                            line[write++] = Quote;
                            read += 2;
                            continue;
                        }

                        closed = true;
                        read++;
                        break;
                    }

                    if (write != read)
                    {
                        line[write] = b;
                    }

                    write++;
                    read++;
                }

                Record(fields, count++, start, write - start);

                // An unterminated quote swallows the rest of the line; otherwise skip to the separator.
                if (!closed)
                {
                    break;
                }

                int next = line[read..].IndexOf(Comma);
                if (next < 0)
                {
                    break;
                }

                position = read + next + 1;
            }
            else
            {
                int next = line[position..].IndexOf(Comma);
                if (next < 0)
                {
                    Record(fields, count++, position, line.Length - position);
                    break;
                }

                Record(fields, count++, position, next);
                position += next + 1;
            }
        }

        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Record(Span<CsvField> fields, int index, int start, int length)
    {
        if (index < fields.Length)
        {
            fields[index] = new CsvField(start, length);
        }
    }
}

/// <summary>
/// The location of one field within a CSV record.
/// </summary>
/// <param name="Start">Offset of the first byte.</param>
/// <param name="Length">Number of bytes.</param>
internal readonly record struct CsvField(int Start, int Length)
{
    /// <summary>Gets the bytes of this field within <paramref name="line"/>.</summary>
    public ReadOnlySpan<byte> In(ReadOnlySpan<byte> line) => line.Slice(this.Start, this.Length);
}
