// <copyright file="LineBuffer.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre.Csv;

/// <summary>
/// Reads a stream in large blocks and hands out one line at a time as a slice of its own buffer, so
/// no per-line array or string is created. Lines end at LF; a preceding CR is dropped. The buffer
/// grows only when a single line is longer than the whole buffer.
/// </summary>
internal sealed class LineBuffer(Stream stream, int initialCapacity = LineBuffer.DefaultCapacity)
{
    /// <summary>
    /// Default buffer size. Large enough that a daily MarineCadastre file (roughly 100 bytes per row)
    /// is read in a few thousand calls.
    /// </summary>
    public const int DefaultCapacity = 256 * 1024;

    private const byte Lf = (byte)'\n';
    private const byte Cr = (byte)'\r';

    private byte[] buffer = new byte[initialCapacity];
    private int start;
    private int end;
    private int scanned;
    private bool endOfStream;
    private bool first = true;

    /// <summary>Gets the bytes of a line returned by <see cref="TryTakeLine"/> or <see cref="TryTakeRemainder"/>.</summary>
    public Span<byte> Slice(int offset, int length) => this.buffer.AsSpan(offset, length);

    /// <summary>
    /// Takes the next complete line from the buffered data. Returns <see langword="false"/> when the
    /// buffer holds no complete line, in which case the caller fills it (or, at end of stream, takes
    /// the remainder with <see cref="TryTakeRemainder"/>).
    /// </summary>
    public bool TryTakeLine(out int offset, out int length)
    {
        int index = this.buffer.AsSpan(this.scanned, this.end - this.scanned).IndexOf(Lf);
        if (index < 0)
        {
            this.scanned = this.end;
            offset = 0;
            length = 0;
            return false;
        }

        int lineEnd = this.scanned + index;
        offset = this.start;
        length = lineEnd - this.start;
        this.start = lineEnd + 1;
        this.scanned = this.start;

        if (length > 0 && this.buffer[offset + length - 1] == Cr)
        {
            length--;
        }

        this.SkipByteOrderMark(ref offset, ref length);
        return true;
    }

    /// <summary>
    /// At end of stream, takes whatever follows the last LF as a final line. Returns
    /// <see langword="false"/> when nothing is left.
    /// </summary>
    public bool TryTakeRemainder(out int offset, out int length)
    {
        offset = this.start;
        length = this.end - this.start;
        this.start = this.end;
        this.scanned = this.end;

        if (length > 0 && this.buffer[offset + length - 1] == Cr)
        {
            length--;
        }

        this.SkipByteOrderMark(ref offset, ref length);
        return length > 0;
    }

    /// <summary>
    /// Reads more data from the stream. Returns <see langword="false"/> at end of stream.
    /// </summary>
    public bool Fill()
    {
        if (this.endOfStream)
        {
            return false;
        }

        this.MakeRoom();
        int read = stream.Read(this.buffer, this.end, this.buffer.Length - this.end);
        return this.Advance(read);
    }

    /// <summary>
    /// Reads more data from the stream. Returns <see langword="false"/> at end of stream.
    /// </summary>
    public async ValueTask<bool> FillAsync(CancellationToken cancellationToken)
    {
        if (this.endOfStream)
        {
            return false;
        }

        this.MakeRoom();
        int read = await stream.ReadAsync(this.buffer.AsMemory(this.end), cancellationToken).ConfigureAwait(false);
        return this.Advance(read);
    }

    private bool Advance(int read)
    {
        if (read == 0)
        {
            this.endOfStream = true;
            return false;
        }

        this.end += read;
        return true;
    }

    private void MakeRoom()
    {
        if (this.start > 0)
        {
            // Move the partial line at the tail to the front so the rest of the buffer is free.
            int pending = this.end - this.start;
            this.buffer.AsSpan(this.start, pending).CopyTo(this.buffer);
            this.scanned -= this.start;
            this.start = 0;
            this.end = pending;
        }

        if (this.end == this.buffer.Length)
        {
            // One line is longer than the whole buffer.
            Array.Resize(ref this.buffer, this.buffer.Length * 2);
        }
    }

    private void SkipByteOrderMark(ref int offset, ref int length)
    {
        if (!this.first)
        {
            return;
        }

        this.first = false;
        if (length >= 3 && this.buffer[offset] == 0xEF && this.buffer[offset + 1] == 0xBB && this.buffer[offset + 2] == 0xBF)
        {
            offset += 3;
            length -= 3;
        }
    }
}
