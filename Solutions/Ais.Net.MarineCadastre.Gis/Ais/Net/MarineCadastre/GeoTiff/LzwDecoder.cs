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
        ushort[] prefix = ArrayPool<ushort>.Shared.Rent(TableSize);
        ushort[] length = ArrayPool<ushort>.Shared.Rent(TableSize);
        byte[] suffix = ArrayPool<byte>.Shared.Rent(TableSize);
        try
        {
            for (int i = 0; i < 256; i++)
            {
                suffix[i] = (byte)i;
                length[i] = 1;
            }

            Span<byte> scratch = stackalloc byte[TableSize];

            ulong bitBuffer = 0;
            int bitCount = 0;
            int inputIndex = 0;
            int codeBits = 9;
            int nextCode = FirstFreeCode;
            int previous = -1;
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
                    previous = -1;
                    continue;
                }

                if (code == EndOfInformation)
                {
                    break;
                }

                if (previous < 0)
                {
                    if (code > 255)
                    {
                        throw new InvalidDataException("LZW data starts with a code that is not a literal byte.");
                    }

                    output[written++] = (byte)code;
                    previous = code;
                    continue;
                }

                byte firstByte;
                int stringCode;
                bool addFirstOfPrevious;
                if (code < nextCode)
                {
                    stringCode = code;
                    addFirstOfPrevious = false;
                }
                else if (code == nextCode)
                {
                    // The KwKwK case: the string is the previous string followed by its own first byte.
                    stringCode = previous;
                    addFirstOfPrevious = true;
                }
                else
                {
                    throw new InvalidDataException("LZW data refers to a table entry that has not been defined.");
                }

                int stringLength = length[stringCode] + (addFirstOfPrevious ? 1 : 0);
                bool overflows = written + stringLength > output.Length;
                Span<byte> target = overflows ? scratch[..stringLength] : output.Slice(written, stringLength);

                int position = length[stringCode] - 1;
                int current = stringCode;
                while (current >= FirstFreeCode)
                {
                    target[position--] = suffix[current];
                    current = prefix[current];
                }

                firstByte = suffix[current];
                target[position] = firstByte;
                if (addFirstOfPrevious)
                {
                    target[stringLength - 1] = firstByte;
                }

                if (overflows)
                {
                    int fit = output.Length - written;
                    target[..fit].CopyTo(output[written..]);
                    written += fit;
                    break;
                }

                written += stringLength;

                if (nextCode < TableSize)
                {
                    prefix[nextCode] = (ushort)previous;
                    suffix[nextCode] = firstByte;
                    length[nextCode] = (ushort)(length[previous] + 1);
                    nextCode++;
                }

                if (nextCode + 1 >= (1 << codeBits) && codeBits < MaxCodeBits)
                {
                    codeBits++;
                }

                previous = code;
            }

            return written;
        }
        finally
        {
            ArrayPool<ushort>.Shared.Return(prefix);
            ArrayPool<ushort>.Shared.Return(length);
            ArrayPool<byte>.Shared.Return(suffix);
        }
    }
}
