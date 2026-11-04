// <copyright file="RasterLevel.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.MarineCadastre.GeoTiff;

/// <summary>
/// One resolution level of a GeoTIFF: the full image or one of its overviews, in tiles or strips.
/// </summary>
internal sealed class RasterLevel
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int BlockWidth { get; init; }

    public required int BlockHeight { get; init; }

    public required bool Tiled { get; init; }

    public required int Compression { get; init; }

    public required int Predictor { get; init; }

    public required int BitsPerSample { get; init; }

    public required int SampleFormat { get; init; }

    public required long[] BlockOffsets { get; init; }

    public required long[] BlockByteCounts { get; init; }

    public int BlocksAcross => (this.Width + this.BlockWidth - 1) / this.BlockWidth;

    public int BlocksDown => (this.Height + this.BlockHeight - 1) / this.BlockHeight;
}
