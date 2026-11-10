# Transit counts

MarineCadastre's yearly **AIS Vessel Transit Counts** product is a GeoTIFF raster: a grid of 100 m cells in Web Mercator. Each cell holds the number of vessel transits that crossed it in the year. A transit is one passage of one vessel through a cell, however many position reports the vessel sent there, so this is a density product, not a message archive.

The 2025 file is a 534 MiB BigTIFF of 360,676 × 56,286 cells (20 billion), in 512-cell LZW tiles with seven Deflate overviews. 371 million of those cells hold data; the rest are NoData.

The **Ais.Net.MarineCadastre.Gis** package reads these files. It does not need GDAL.

## Usage

```csharp
using Ais.Net.MarineCadastre;
using Ais.Net.MarineCadastre.TransitCounts;
using Ais.Net.Models.Abstractions;

using TransitCountRaster raster = MarineCadastreTransitCountReader.Open("ais-transit-count-2025.tif");

uint underSeattle = raster.CountAt(new Position(47.60, -122.34));

TransitCountReaderOptions pugetSound = new()
{
    Bounds = new BoundingBox(West: -122.6, South: 47.4, East: -122.2, North: 47.8),
    MinimumTransits = 1000,
};

IDisposable subscription = raster.Observe(pugetSound)
    .Subscribe(cell => Console.WriteLine($"{cell.Centre.Latitude:F4},{cell.Centre.Longitude:F4}: {cell.Transits}"));
```

The raster offers these ways in:

- `Observe` streams the cells that hold data as `TransitCell` values (column, row, centre `Position`, count). It goes tile by tile from north-west to south-east, and is a cold observable on the same terms as the [tracks reader](vessel-tracks.md#usage).
- `ObserveTiles` streams `TransitTile` values instead: a rectangle of counts with its geographic bounds, for consumers that want the grid. `Counts` always holds `Width` × `Height` values, and the indexer rejects cells outside the tile.
- `Read` and `ReadTiles` are the pull-based equivalents.
- `CountAt` is the cheap path for a single point. It reads one tile and caches it for the next lookup in the same tile. It returns 0 both for a NoData cell and for a position outside the raster; test the position against `Extent` to tell them apart.

The static `MarineCadastreTransitCountReader.Observe(path)` opens and closes the file for each subscription.

## Options

`TransitCountReaderOptions` has three options:

- `Bounds`: only tiles that touch the box are read, and a cell is kept when its centre is inside the box.
- `Level`: 0 is the exact 100 m grid. Each overview level doubles the cell size, resampled by whatever tool built the file.
- `MinimumTransits`: the smallest count a cell must have to be returned.

The raster also exposes `Extent`, `LevelCount`, `Dimensions`, `CellSize`, `NoData` and the band `Statistics` that GDAL stored in the file. `CellSize` is in metres when `IsProjected` is true (Web Mercator), and in degrees otherwise. Once the reader has seen one tile with no data, it skips other such tiles without decoding them, because identical content compresses to identical bytes.

## Caveats

The API cannot hide two properties of the data:

- Cells are 100 m in Web Mercator metres, and those shrink with latitude. A cell at 47°N is about 68 m across on the water, so counts are not per equal area.
- The 2025 file's extent runs from 174°W east to 150°E, but only from 10°N to 51.7°N. It leaves out most of Alaska.

## Supported files

The reader handles the file as published and nearby variations:

- classic TIFF or BigTIFF, in either byte order;
- tiles or strips;
- no compression, LZW or Deflate, with or without the horizontal-differencing predictor;
- 8, 16 or 32-bit integer cells;
- Web Mercator declared by EPSG code or as ArcGIS's user-defined CRS, or geographic coordinates.

It refuses other projections with an error that names the CRS. A compression it does not support is reported when you read a cell or tile from that level, not when you open the file.

## Performance

Measured on the 2025 file on the same desktop as the [CSV measurements](csv.md#performance):

| Operation                                  | Result                                                                         | Elapsed          |
|--------------------------------------------|--------------------------------------------------------------------------------|------------------|
| `CountAt`, four lookups in different tiles | one 70 KB tile each, decoded only as far as the cell                           | under 10 ms each |
| Puget Sound box, `Read`                    | 116,153 cells                                                                  | 0.06 s           |
| Whole file, `Read` or `Observe`            | 371,149,894 cells; count, maximum and mean match the stored statistics exactly | 14 s             |
| Whole file, `ReadTiles`                    | 7,643 non-empty tiles, 2 billion cells                                         | 10 s             |

The whole-file read allocates about 1 MiB in total and peaks at 45 MiB of working set, because `TransitCell` is a struct and tiles decode into one reused buffer.

Most of the time goes to decoding the LZW tiles. The decoder keeps its code table as offsets into the output it has already produced, so each code is one block copy. That suits a raster where most codes stand for long runs of NoData. The cell scan skips NoData runs with a vectorised search and converts each row's latitude once, so it adds a few seconds over the tile read instead of ten. Because LZW is sequential, a point lookup decodes a tile only as far as the cell it needs.

### Benchmarks

By default, the transit-count benchmarks run over a generated 2048 × 2048 raster with the real file's density: about 2% of cells hold data. To measure a real file, set `AIS_NET_MARINECADASTRE_TIF`. The 2025 file has 7,643 non-empty tiles, so use `--job short`:

```powershell
$env:AIS_NET_MARINECADASTRE_TIF = "D:\data\ais-transit-count-2025.tif"
dotnet run -c Release --project Solutions/Ais.Net.MarineCadastre.Benchmarks -- --filter *TransitCount* --job short
```
