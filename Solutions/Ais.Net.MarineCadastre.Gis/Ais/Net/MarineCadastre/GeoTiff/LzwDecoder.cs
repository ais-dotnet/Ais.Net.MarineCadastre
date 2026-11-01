// <copyright file="LzwDecoder.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Buffers;

namespace Ais.Net.MarineCadastre.GeoTiff;

/// <summary>
/// Decodes TIFF LZW (compression 5): the LZW variant with most-significant-bit-first codes, 9 to 12
/// bits wide, where the code width grows one code earlier than in the original LZW, as libtiff writes
/// and reads it.
/// </summary>
/// <remarks>
/// Every string the code table defines is a substring of the output already produced since the last
/// clear code, so the table holds (start, length) pairs into the output and a code decodes as one block
/// copy. On a sparse raster most codes stand for long runs of zero bytes, and copying them is several
/// times faster than rebuilding them a byte at a time through prefix links.
/// </remarks>
internal static class LzwDecoder
{
    private const int ClearCode = 256;
    private const int EndOfInformation = 257;
    private const int FirstFreeCode = 258;
    private const int TableSize = 4096;
    private const int MaxCodeBits = 12;

    /// <summary>
    /// Decodes until the output is full, the end-of-information code arrives, or the input runs out.
    /// </summary>
    /// <param name="input">The compressed bytes.</param>
    /// <param name="output">Where the decoded bytes go.</param>
    /// <returns>The number of bytes decoded, which is less than the output length only when the stream ended early.</returns>
    /// <exception cref="InvalidDataException">Thrown when a code refers to an entry the table does not have.</exception>
    public static int Decode(ReadOnlySpan<byte> input, Span<byte> output)
    {
        int[] start = ArrayPool<int>.Shared.Rent(TableSize);
        int[] length = ArrayPool<int>.Shared.Rent(TableSize);
        try
        {
            ulong bitBuffer = 0;
            int bitCount = 0;
            int inputIndex = 0;
            int codeBits = 9;
            int nextCode = FirstFreeCode;
            int previousStart = -1;
            int previousLength = 0;
            int written = 0;

            while (written < output.Length)
            {
                while (bitCount < codeBits && inputIndex < input.Length)
                {
                    bitBuffer = (bitBuffer << 8) | input[inputIndex++];
                    bitCount += 8;
                }

                if (bitCount < codeBits)
                {
                    break;
                }

                int code = (int)((bitBuffer >> (bitCount - codeBits)) & ((1u << codeBits) - 1));
                bitCount -= codeBits;

                if (code == ClearCode)
                {
                    codeBits = 9;
                    nextCode = FirstFreeCode;
                    previousStart = -1;
                    continue;
                }

                if (code == EndOfInformation)
                {
                    break;
                }

                int stringStart = written;
                int stringLength;
                int remaining = output.Length - written;

                if (code < ClearCode)
                {
                    output[written++] = (byte)code;
                    stringLength = 1;
                }
                else if (code < nextCode && previousStart >= 0)
                {
                    stringLength = length[code];
                    int fit = Math.Min(stringLength, remaining);
                    output.Slice(start[code], fit).CopyTo(output.Slice(written, fit));
                    written += fit;
                    if (fit < stringLength)
                    {
                        break;
                    }
                }
                else if (code == nextCode && previousStart >= 0)
                {
                    // The KwKwK case: the previous string followed by its own first byte. The previous
                    // string ends exactly where this one begins, so the copy does not overlap.
                    stringLength = previousLength + 1;
                    int fit = Math.Min(previousLength, remaining);
                    output.Slice(previousStart, fit).CopyTo(output.Slice(written, fit));
                    written += fit;
                    if (fit < previousLength)
                    {
                        break;
                    }

                    if (remaining == previousLength)
                    {
                        break;
                    }

                    output[written++] = output[previousStart];
                }
                else
                {
                    throw new InvalidDataException(previousStart < 0
                        ? "LZW data starts with a code that is not a literal byte."
                        : "LZW data refers to a table entry that has not been defined.");
                }

                if (previousStart >= 0)
                {
                    if (nextCode < TableSize)
                    {
                        // The new entry is the previous string plus the first byte of this one, which
                        // sit next to each other in the output.
                        start[nextCode] = previousStart;
                        length[nextCode] = previousLength + 1;
                        nextCode++;
                    }

                    if (nextCode + 1 >= (1 << codeBits) && codeBits < MaxCodeBits)
                    {
                        codeBits++;
                    }
                }

                previousStart = stringStart;
                previousLength = stringLength;
            }

            return written;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(start);
            ArrayPool<int>.Shared.Return(length);
        }
    }
}
