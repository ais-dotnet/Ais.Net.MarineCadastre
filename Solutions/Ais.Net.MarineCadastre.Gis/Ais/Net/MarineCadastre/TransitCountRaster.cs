// <copyright file="TransitCountRaster.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Reactive.Linq;

using Ais.Net.MarineCadastre.GeoTiff;
using Ais.Net.Models.Abstractions;

namespace Ais.Net.MarineCadastre;

/// <summary>
/// An open MarineCadastre AIS Vessel Transit Counts raster: a GeoTIFF whose cells count how many vessel
/// transits crossed each 100 m square in a year. Offers point lookups, and the cells or tiles that hold
/// data as pull sequences or cold observables.
/// </summary>
/// <remarks>
/// <para>
/// The file stays open until the raster is disposed; reads go through one shared handle, so several
/// subscriptions may run at once. Each read decodes one tile at a time, so memory use is a few
/// megabytes however large the raster is. Tiles that hold no data are detected without decoding once
/// one has been seen, because identical input compresses to identical bytes.
/// </para>
/// <para>
/// Cells are squares in the raster's own coordinate system. MarineCadastre uses Web Mercator, whose
/// metres shrink with latitude, so a 100 m cell at 47°N is about 68 m across on the water and counts
/// are not per equal area.
/// </para>
/// </remarks>
public sealed class TransitCountRaster : IDisposable
{
    private readonly GeoTiffFile file;
    private readonly Lock cacheLock = new();
    private uint[]? cachedCells;
    private int cachedLevel = -1;
    private int cachedBlock = -1;

    internal TransitCountRaster(GeoTiffFile file, string path)
    {
        this.file = file;
        this.Path = path;
        RasterLevel full = file.Levels[0];
        this.Extent = file.Georeference.CellBounds(0, 0, full.Width, full.Height, file.Georeference.CellWidth, file.Georeference.CellHeight);
        this.Statistics = TransitCountStatistics.Parse(file.GdalMetadata);
        this.NoData = file.NoData is { } value ? unchecked((uint)(long)value) : null;
    }

    /// <summary>
    /// Gets the path the raster was opened from.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the geographic box the raster covers.
    /// </summary>
    public BoundingBox Extent { get; }

    /// <summary>
    /// Gets the number of resolution levels: the full-resolution grid plus its overviews.
    /// </summary>
    public int LevelCount => this.file.Levels.Count;

    /// <summary>
    /// Gets a value indicating whether cells are in projected metres (Web Mercator) rather than degrees.
    /// </summary>
    public bool IsProjected => this.file.Georeference.Projection == RasterProjection.WebMercator;

    /// <summary>
    /// Gets the value that marks a cell without data, or <see langword="null"/> when the file declares none.
    /// </summary>
    public uint? NoData { get; }

    /// <summary>
    /// Gets the band statistics stored in the file, or <see langword="null"/> when it has none.
    /// </summary>
    public TransitCountStatistics? Statistics { get; }

    /// <summary>
    /// Gets the grid size at a level.
    /// </summary>
    /// <param name="level">The level; 0 is full resolution.</param>
    /// <returns>Columns and rows.</returns>
    public (int Columns, int Rows) Dimensions(int level = 0)
    {
        RasterLevel raster = this.Level(level);
        return (raster.Width, raster.Height);
    }

    /// <summary>
    /// Gets the size of a cell at a level, in the raster's units: metres when <see cref="IsProjected"/>, else degrees.
    /// </summary>
    /// <param name="level">The level; 0 is full resolution.</param>
    /// <returns>Cell width and height.</returns>
    public (double Width, double Height) CellSize(int level = 0)
    {
        RasterLevel raster = this.Level(level);
        return this.CellSize(raster);
    }

    /// <summary>
    /// Looks up the count of the cell under a position. Reads one tile, which is cached for the next
    /// lookup in the same tile.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <param name="level">The level; 0 is full resolution.</param>
    /// <returns>The count; 0 for a NoData cell or a position outside the raster.</returns>
    public uint CountAt(Position position, int level = 0)
    {
        RasterLevel raster = this.Level(level);
        (double cellWidth, double cellHeight) = this.CellSize(raster);
        (double x, double y) = this.file.Georeference.ToModel(position);
        double column = Math.Floor((x - this.file.Georeference.OriginX) / cellWidth);
        double row = Math.Floor((this.file.Georeference.OriginY - y) / cellHeight);
        if (column < 0 || row < 0 || column >= raster.Width || row >= raster.Height)
        {
            return 0;
        }

        int blockIndex = (((int)row / raster.BlockHeight) * raster.BlocksAcross) + ((int)column / raster.BlockWidth);
        int offset = (((int)row % raster.BlockHeight) * raster.BlockWidth) + ((int)column % raster.BlockWidth);

        lock (this.cacheLock)
        {
            uint[]? cells = this.cachedCells;
            if (cells is null || this.cachedLevel != level || this.cachedBlock != blockIndex)
            {
                if (cells is null || cells.Length != raster.BlockWidth * raster.BlockHeight)
                {
                    cells = new uint[raster.BlockWidth * raster.BlockHeight];
                    this.cachedCells = cells;
                }

                this.file.DecodeBlock(raster, blockIndex, cells);
                this.cachedLevel = level;
                this.cachedBlock = blockIndex;
            }

            uint value = cells[offset];
            return value == this.NoData ? 0 : value;
        }
    }

    /// <summary>
    /// Reads the cells that hold data, tile by tile from north-west to south-east.
    /// </summary>
    /// <param name="options">Filters; <see cref="TransitCountReaderOptions.Default"/> when omitted.</param>
    /// <returns>The cells.</returns>
    public IEnumerable<TransitCell> Read(TransitCountReaderOptions? options = null) =>
        this.ReadCells(options ?? TransitCountReaderOptions.Default, CancellationToken.None);

    /// <summary>
    /// Reads the tiles that hold data, from north-west to south-east.
    /// </summary>
    /// <param name="options">Filters; <see cref="TransitCountReaderOptions.Default"/> when omitted.</param>
    /// <returns>The tiles.</returns>
    public IEnumerable<TransitTile> ReadTiles(TransitCountReaderOptions? options = null) =>
        this.ReadTilesCore(options ?? TransitCountReaderOptions.Default, CancellationToken.None);

    /// <summary>
    /// Streams the cells that hold data. Each subscription reads on a thread-pool thread and completes
    /// when the last tile is done; disposing the subscription stops the read.
    /// </summary>
    /// <param name="options">Filters; <see cref="TransitCountReaderOptions.Default"/> when omitted.</param>
    /// <returns>The cells, tile by tile from north-west to south-east.</returns>
    public IObservable<TransitCell> Observe(TransitCountReaderOptions? options = null)
    {
        TransitCountReaderOptions settings = options ?? TransitCountReaderOptions.Default;
        return Observable.Create<TransitCell>((observer, cancellationToken) => Task.Run(
            () =>
            {
                foreach (TransitCell cell in this.ReadCells(settings, cancellationToken))
                {
                    observer.OnNext(cell);
                }
            },
            cancellationToken));
    }

    /// <summary>
    /// Streams the tiles that hold data, with the same behaviour as <see cref="Observe"/>.
    /// </summary>
    /// <param name="options">Filters; <see cref="TransitCountReaderOptions.Default"/> when omitted.</param>
    /// <returns>The tiles, from north-west to south-east.</returns>
    public IObservable<TransitTile> ObserveTiles(TransitCountReaderOptions? options = null)
    {
        TransitCountReaderOptions settings = options ?? TransitCountReaderOptions.Default;
        return Observable.Create<TransitTile>((observer, cancellationToken) => Task.Run(
            () =>
            {
                foreach (TransitTile tile in this.ReadTilesCore(settings, cancellationToken))
                {
                    observer.OnNext(tile);
                }
            },
            cancellationToken));
    }

    /// <inheritdoc/>
    public void Dispose() => this.file.Dispose();

    private RasterLevel Level(int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(level, this.file.Levels.Count);
        return this.file.Levels[level];
    }

    private (double Width, double Height) CellSize(RasterLevel level)
    {
        RasterLevel full = this.file.Levels[0];
        return (this.file.Georeference.CellWidth * full.Width / level.Width, this.file.Georeference.CellHeight * full.Height / level.Height);
    }

    private IEnumerable<TransitCell> ReadCells(TransitCountReaderOptions options, CancellationToken cancellationToken)
    {
        RasterLevel level = this.Level(options.Level);
        (double cellWidth, double cellHeight) = this.CellSize(level);
        uint noData = this.NoData ?? 0;
        uint minimum = Math.Max(options.MinimumTransits, this.NoData is null ? 1u : 0u);
        RasterGeoreference geo = this.file.Georeference;

        BlockReader reader = new(this.file, level, noData);
        foreach ((int tileColumn, int tileRow) in this.TilesTouching(level, options.Bounds, cellWidth, cellHeight))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reader.TryDecode((tileRow * level.BlocksAcross) + tileColumn))
            {
                continue;
            }

            int firstColumn = tileColumn * level.BlockWidth;
            int firstRow = tileRow * level.BlockHeight;
            int width = Math.Min(level.BlockWidth, level.Width - firstColumn);
            int height = Math.Min(level.BlockHeight, level.Height - firstRow);
            for (int row = 0; row < height; row++)
            {
                int rowStart = row * level.BlockWidth;
                double y = geo.OriginY - ((firstRow + row + 0.5) * cellHeight);
                for (int column = 0; column < width; column++)
                {
                    uint value = reader.Cells[rowStart + column];
                    if (value == noData || value < minimum)
                    {
                        continue;
                    }

                    double x = geo.OriginX + ((firstColumn + column + 0.5) * cellWidth);
                    Position centre = geo.ToPosition(x, y);
                    if (options.Bounds is { } bounds && !bounds.Contains(centre))
                    {
                        continue;
                    }

                    yield return new TransitCell(firstColumn + column, firstRow + row, centre, value);
                }
            }
        }
    }

    private IEnumerable<TransitTile> ReadTilesCore(TransitCountReaderOptions options, CancellationToken cancellationToken)
    {
        RasterLevel level = this.Level(options.Level);
        (double cellWidth, double cellHeight) = this.CellSize(level);
        uint noData = this.NoData ?? 0;

        BlockReader reader = new(this.file, level, noData);
        foreach ((int tileColumn, int tileRow) in this.TilesTouching(level, options.Bounds, cellWidth, cellHeight))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!reader.TryDecode((tileRow * level.BlocksAcross) + tileColumn))
            {
                continue;
            }

            int firstColumn = tileColumn * level.BlockWidth;
            int firstRow = tileRow * level.BlockHeight;
            int width = Math.Min(level.BlockWidth, level.Width - firstColumn);
            int height = Math.Min(level.BlockHeight, level.Height - firstRow);

            uint[] counts = new uint[width * height];
            for (int row = 0; row < height; row++)
            {
                Span<uint> source = reader.Cells.AsSpan(row * level.BlockWidth, width);
                Span<uint> target = counts.AsSpan(row * width, width);
                source.CopyTo(target);
                if (this.NoData is { } marker && marker != 0)
                {
                    target.Replace(marker, 0u);
                }
            }

            BoundingBox bounds = this.file.Georeference.CellBounds(firstColumn, firstRow, width, height, cellWidth, cellHeight);
            yield return new TransitTile(tileColumn, tileRow, options.Level, firstColumn, firstRow, width, height, bounds, counts);
        }
    }

    private IEnumerable<(int Column, int Row)> TilesTouching(RasterLevel level, BoundingBox? bounds, double cellWidth, double cellHeight)
    {
        int firstTileColumn = 0;
        int lastTileColumn = level.BlocksAcross - 1;
        int firstTileRow = 0;
        int lastTileRow = level.BlocksDown - 1;

        if (bounds is { } box)
        {
            RasterGeoreference geo = this.file.Georeference;
            (double westX, double southY) = geo.ToModel(new Position(box.South, box.West));
            (double eastX, double northY) = geo.ToModel(new Position(box.North, box.East));
            long firstColumn = (long)Math.Floor((westX - geo.OriginX) / cellWidth);
            long lastColumn = (long)Math.Floor((eastX - geo.OriginX) / cellWidth);
            long firstRow = (long)Math.Floor((geo.OriginY - northY) / cellHeight);
            long lastRow = (long)Math.Floor((geo.OriginY - southY) / cellHeight);
            if (lastColumn < 0 || lastRow < 0 || firstColumn >= level.Width || firstRow >= level.Height)
            {
                yield break;
            }

            firstTileColumn = (int)(Math.Max(firstColumn, 0) / level.BlockWidth);
            lastTileColumn = (int)(Math.Min(lastColumn, level.Width - 1) / level.BlockWidth);
            firstTileRow = (int)(Math.Max(firstRow, 0) / level.BlockHeight);
            lastTileRow = (int)(Math.Min(lastRow, level.Height - 1) / level.BlockHeight);
        }

        for (int row = firstTileRow; row <= lastTileRow; row++)
        {
            for (int column = firstTileColumn; column <= lastTileColumn; column++)
            {
                yield return (column, row);
            }
        }
    }

    /// <summary>
    /// Decodes blocks of one level into a reused buffer and skips blocks that hold no data, which it
    /// recognises by their compressed bytes once it has decoded one.
    /// </summary>
    private sealed class BlockReader
    {
        private readonly GeoTiffFile file;
        private readonly RasterLevel level;
        private readonly uint noData;
        private readonly byte[] compressed;
        private byte[]? emptySignature;

        public BlockReader(GeoTiffFile file, RasterLevel level, uint noData)
        {
            this.file = file;
            this.level = level;
            this.noData = noData;
            long largest = level.BlockByteCounts.Length == 0 ? 0 : level.BlockByteCounts.Max();
            this.compressed = new byte[checked((int)largest)];
            this.Cells = new uint[level.BlockWidth * level.BlockHeight];
        }

        public uint[] Cells { get; }

        public bool TryDecode(int blockIndex)
        {
            if (this.level.BlockByteCounts[blockIndex] == 0)
            {
                return false;
            }

            int length = this.file.ReadBlockBytes(this.level, blockIndex, this.compressed);
            ReadOnlySpan<byte> bytes = this.compressed.AsSpan(0, length);
            if (this.emptySignature is not null && bytes.SequenceEqual(this.emptySignature))
            {
                return false;
            }

            this.file.DecodeBlock(this.level, bytes, this.Cells);
            if (this.Cells.AsSpan().ContainsAnyExcept(this.noData))
            {
                return true;
            }

            this.emptySignature ??= bytes.ToArray();
            return false;
        }
    }
}
