// <copyright file="TransitCountRaster.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Reactive.Linq;

using Ais.Net.MarineCadastre.GeoTiff;
using Ais.Net.Models.Abstractions;

namespace Ais.Net.MarineCadastre.TransitCounts;

/// <summary>
/// An open MarineCadastre AIS Vessel Transit Counts raster. It is a GeoTIFF whose cells count the
/// vessel transits that crossed each 100 m square in a year. It offers point lookups. It also offers
/// the cells or tiles that hold data, as pull sequences or as cold observables.
/// </summary>
/// <remarks>
/// <para>
/// The file stays open until you dispose the raster. Reads go through one shared handle, so several
/// subscriptions can run at the same time. Each read decodes one tile at a time. Thus memory use is a
/// few megabytes for a raster of any size. After the reader sees one tile with no data, it finds the
/// other such tiles without a decode. This works because identical input compresses to identical bytes.
/// </para>
/// <para>
/// Cells are squares in the coordinate system of the raster. MarineCadastre uses Web Mercator, whose
/// metres become smaller with latitude. Thus a 100 m cell at 47°N is approximately 68 m across on the
/// water, and the counts are not for equal areas.
/// </para>
/// </remarks>
public sealed class TransitCountRaster : IDisposable
{
    private readonly GeoTiffFile file;
    private readonly Lock cacheLock = new();
    private uint[]? cachedCells;
    private int cachedLevel = -1;
    private int cachedBlock = -1;
    private int cachedCellCount;

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
    /// Gets the path of the file of the raster.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the geographic box that the raster covers.
    /// </summary>
    public BoundingBox Extent { get; }

    /// <summary>
    /// Gets the number of resolution levels: the full-resolution grid and its overviews.
    /// </summary>
    public int LevelCount => this.file.Levels.Count;

    /// <summary>
    /// Gets a value that is <see langword="true"/> if the cells are in projected metres (Web Mercator),
    /// and <see langword="false"/> if they are in degrees.
    /// </summary>
    public bool IsProjected => this.file.Georeference.Projection == RasterProjection.WebMercator;

    /// <summary>
    /// Gets the value that marks a cell without data, or <see langword="null"/> if the file declares none.
    /// </summary>
    public uint? NoData { get; }

    /// <summary>
    /// Gets the band statistics that the file stores, or <see langword="null"/> if it has none.
    /// </summary>
    public TransitCountStatistics? Statistics { get; }

    /// <summary>
    /// Gets the grid size at a level.
    /// </summary>
    /// <param name="level">The level. 0 is full resolution.</param>
    /// <returns>The columns and the rows.</returns>
    public (int Columns, int Rows) Dimensions(int level = 0)
    {
        RasterLevel raster = this.Level(level);
        return (raster.Width, raster.Height);
    }

    /// <summary>
    /// Gets the size of a cell at a level, in the units of the raster: metres if <see cref="IsProjected"/>,
    /// or degrees if not.
    /// </summary>
    /// <param name="level">The level. 0 is full resolution.</param>
    /// <returns>The cell width and the cell height.</returns>
    public (double Width, double Height) CellSize(int level = 0)
    {
        RasterLevel raster = this.Level(level);
        return this.CellSize(raster);
    }

    /// <summary>
    /// Gets the count of the cell under a position. The raster reads one tile and caches it for the
    /// next lookup in the same tile. The result is 0 for a NoData cell and for a position outside the
    /// raster. To tell the two apart, test the position with <see cref="Extent"/>.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <param name="level">The level. 0 is full resolution.</param>
    /// <returns>The count. It is 0 for a NoData cell or for a position outside the raster.</returns>
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
            int blockCells = raster.BlockWidth * raster.BlockHeight;
            uint[] cells = this.cachedCells is { } existing && existing.Length == blockCells ? existing : new uint[blockCells];
            bool sameBlock = ReferenceEquals(cells, this.cachedCells) && this.cachedLevel == level && this.cachedBlock == blockIndex;
            if (!sameBlock || offset >= this.cachedCellCount)
            {
                this.cachedCells = cells;

                // Decode only as far as the cell asked for: the compression is sequential, so a cell
                // near the top of a tile costs a fraction of the tile. A later lookup further down the
                // same tile decodes the whole of it.
                int needed = sameBlock ? cells.Length : offset + 1;
                this.file.DecodeBlock(raster, blockIndex, cells.AsSpan(0, needed));
                this.cachedLevel = level;
                this.cachedBlock = blockIndex;
                this.cachedCellCount = needed;
            }

            uint value = cells[offset];
            return value == this.NoData ? 0 : value;
        }
    }

    /// <summary>
    /// Reads the cells that hold data, tile by tile from north-west to south-east.
    /// </summary>
    /// <param name="options">The filters. If you do not give options, the raster uses <see cref="TransitCountReaderOptions.Default"/>.</param>
    /// <returns>The cells.</returns>
    public IEnumerable<TransitCell> Read(TransitCountReaderOptions? options = null) =>
        this.ReadCells(options ?? TransitCountReaderOptions.Default, CancellationToken.None);

    /// <summary>
    /// Reads the tiles that hold data, from north-west to south-east.
    /// </summary>
    /// <param name="options">The filters. If you do not give options, the raster uses <see cref="TransitCountReaderOptions.Default"/>.</param>
    /// <returns>The tiles.</returns>
    public IEnumerable<TransitTile> ReadTiles(TransitCountReaderOptions? options = null) =>
        this.ReadTilesCore(options ?? TransitCountReaderOptions.Default, CancellationToken.None);

    /// <summary>
    /// Streams the cells that hold data. Each subscription reads on a thread-pool thread and completes
    /// after the last tile. If you dispose the subscription, the read stops.
    /// </summary>
    /// <param name="options">The filters. If you do not give options, the raster uses <see cref="TransitCountReaderOptions.Default"/>.</param>
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
    /// <param name="options">The filters. If you do not give options, the raster uses <see cref="TransitCountReaderOptions.Default"/>.</param>
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

                // Most cells are NoData, so jump between the others with a vectorised search, and
                // convert the row's latitude once: every cell in the row shares it.
                int column = NextCellWithData(reader.Cells, rowStart, 0, width, noData);
                if (column < 0)
                {
                    continue;
                }

                double latitude = geo.LatitudeOf(geo.OriginY - ((firstRow + row + 0.5) * cellHeight));
                while (column >= 0)
                {
                    uint value = reader.Cells[rowStart + column];
                    if (value >= minimum)
                    {
                        Position centre = new(latitude, geo.LongitudeOf(geo.OriginX + ((firstColumn + column + 0.5) * cellWidth)));
                        if (options.Bounds is not { } bounds || bounds.Contains(centre))
                        {
                            yield return new TransitCell(firstColumn + column, firstRow + row, centre, value);
                        }
                    }

                    column = NextCellWithData(reader.Cells, rowStart, column + 1, width, noData);
                }
            }
        }
    }

    private static int NextCellWithData(uint[] cells, int rowStart, int from, int width, uint noData)
    {
        if (from >= width)
        {
            return -1;
        }

        int offset = cells.AsSpan(rowStart + from, width - from).IndexOfAnyExcept(noData);
        return offset < 0 ? -1 : from + offset;
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
