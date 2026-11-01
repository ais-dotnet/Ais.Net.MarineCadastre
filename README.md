# Ais.Net.MarineCadastre

Readers for the archived AIS data published by [MarineCadastre.gov](https://hub.marinecadastre.gov/pages/vesseltraffic) (NOAA / BOEM), in two packages:

- **Ais.Net.MarineCadastre** reads the daily vessel-traffic CSV files and converts each row into [Ais.Net.Models](https://github.com/ais-dotnet/Ais.Net.Models) message types, so the same code that consumes a live [Ais.Net.Receiver](https://github.com/ais-dotnet/Ais.Net.Receiver) stream can run over years of archived US coastal traffic.
- **Ais.Net.MarineCadastre.Gis** reads the GIS products published alongside: the monthly AIS Vessel Tracks GeoPackage as an `IObservable<VesselTrack>`, one vessel's path over part of a day with its static data, and the yearly AIS Vessel Transit Counts GeoTIFF as an `IObservable` of cells or tiles, with point lookups. Neither needs GDAL. See [Vessel tracks](#vessel-tracks) and [Transit counts](#transit-counts).

## Usage

```csharp
using Ais.Net.MarineCadastre;
using Ais.Net.Models;
using Ais.Net.Models.Abstractions;

foreach (AisMessageBase message in MarineCadastreCsvReader.ReadFile("AIS_2026_01_01.csv"))
{
    if (message is IVesselNavigation { Position: { } position })
    {
        Console.WriteLine($"{message.Timestamp:u} {message.Mmsi} {position.Latitude},{position.Longitude}");
    }
}
```

`ReadAsync` and `ReadFileAsync` return an `IAsyncEnumerable<AisMessageBase>`. Every message has `Timestamp` set from the row's `base_date_time`, as UTC.

## What a row becomes

A MarineCadastre row is a denormalised join of a position report with the vessel's static data. The reader emits:

| Row | Position report | Static data, first sight or change |
|---|---|---|
| Class A (`transceiver` = A) | Message type 1 (configurable to 2 or 3) | Message type 5 |
| Class B (`transceiver` = B) | Message type 18 | Message type 19 in place of the type 18 for that row, then message type 24 part 1 |

Static data is emitted the first time an MMSI is seen and again whenever its static columns change, so the output has the shape of a live feed: many position reports and occasional static reports. Set `EmitStaticData = false` on `MarineCadastreReaderOptions` to get position reports only.

Columns the CSV has but AIS does not, and the reverse, are mapped as follows:

- `heading` empty becomes 511 and `status` empty becomes `NavigationStatus.NotDefined`, the AIS "not available" values. `sog` and `cog` empty become `null`.
- `length` and `width` go to `DimensionToBow` and `DimensionToPort`; the reference point is not in the data, so stern and starboard are 0.
- `imo` has its `IMO` prefix removed. `draft` in metres becomes `Draught10thMetres`.
- The ship type comes from `vessel_type`, falling back to `cargo`; `ShipTypeSource` on the options swaps that order. Codes outside 0 to 99 become `ShipType.NotAvailable`.
- Rate of turn, RAIM, radio status, ETA, destination and the Class B capability flags are not in the CSV and are set to their "not available" values.

Header names are matched case-insensitively, ignoring underscores, so both the `BaseDateTime` and `base_date_time` spellings MarineCadastre has used work, and column order does not matter.

## Performance

The reader streams. It holds one 256 KB block of the file, splits lines and fields in place without creating strings, and allocates one record per position report. Strings are created only for the static-data messages. A `Dictionary<uint, ulong>` keeps an 8-byte signature per vessel for static-data de-duplication.

Measured on the 1 January 2026 daily file (828 MiB, 8.5 million rows) on a desktop machine, .NET 10, reading from disk:

| Mode | Elapsed | Messages per second | Allocated per message | Peak working set |
|---|---|---|---|---|
| Positions only | 1.9 s | 4.5 million | 134 B | 47 MiB |
| With static data | 2.5 s | 3.4 million | 134 B | 48 MiB |

134 bytes is the size of one `AisMessageType1Through3` record; nothing else is allocated per row. Field splitting uses a vectorised comma scan for the usual quote-free line, numbers take an exact fast path for fixed-point text, and the date part of the timestamp is converted once per day rather than per row.

`Solutions/Ais.Net.MarineCadastre.Benchmarks` measures the layers (line splitting, field splitting, positions only, with static data, async). Point `AIS_NET_MARINECADASTRE_CSV` at a real daily file to run over real traffic:

```powershell
$env:AIS_NET_MARINECADASTRE_CSV = "D:\data\AIS_2026_01_01.csv"
dotnet run -c Release --project Solutions/Ais.Net.MarineCadastre.Benchmarks -- --filter *
```

## Vessel tracks

MarineCadastre also publishes a monthly **AIS Vessel Tracks** product: a [GeoPackage](https://www.geopackage.org/) (an SQLite database) holding one row per vessel track, where a track is one vessel's path over part of one day as a `MULTILINESTRING` in WGS84 with the vessel's static data alongside. The January 2026 file is 3.9 GB and holds 1.19 million tracks with 217 million vertices. The vertices are the same positions as the daily CSV rows, with consecutive repeats removed and the line cut at midnight and at gaps, but they are stored as a plain 2D line, so individual vertices have no timestamp. Only the track's start and end are known.

`Ais.Net.MarineCadastre.Gis` reads these files:

```csharp
using System.Reactive.Linq;
using Ais.Net.MarineCadastre;

VesselTrackReaderOptions pugetSound = new()
{
    Bounds = new BoundingBox(West: -122.6, South: 47.4, East: -122.2, North: 47.8),
    From = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero),
    To = new DateTimeOffset(2026, 1, 11, 0, 0, 0, TimeSpan.Zero),
};

IDisposable subscription = MarineCadastreTrackReader.Observe("AISVesselTracks2026-01.gpkg", pugetSound)
    .Where(track => track.ShipType == ShipType.TankerAll)
    .Subscribe(track => Console.WriteLine($"{track.Mmsi} {track.VesselName}: {track.Points.Count} positions over {track.Duration}"));
```

`Observe` returns a cold observable: each subscription opens the file read-only, reads on a thread-pool thread, emits tracks in file order and completes; disposing the subscription stops the read, and failures arrive through `OnError`. `Read` returns the same tracks as a lazy `IEnumerable<VesselTrack>` for pull-based code.

A `VesselTrack` has the row id, MMSI, start and end times (UTC), duration, the positions as `IReadOnlyList<Position>`, their bounding `Envelope`, and the static columns: vessel name, call sign, IMO number, ship type (from `vessel_type`, falling back to `cargo`) and MarineCadastre's name for it, navigation status, length, width, draught, cargo code and transceiver class. It implements the Ais.Net.Models `IVesselIdentity`, `IVesselName`, `ICallSign`, `IShipType` and `IVesselNavigationStatus` interfaces. Empty columns are `null`, except where an interface needs a value, when the AIS "not available" value is used.

The filters in `VesselTrackReaderOptions` are optional and combine. `Bounds` uses the file's R-tree index, so a query for one harbour reads only that harbour's rows: the Puget Sound box above returns 29,465 of the month's tracks in about a second. `From` and `To` keep tracks that overlap the window, and `Mmsi` keeps one vessel. The feature table and geometry column are found from the GeoPackage's own registry, so a file for any month works, and attribute columns are matched by name ignoring case and underscores.

Reading the whole January 2026 file on the same desktop as above, from the page cache:

| Mode | Elapsed | Tracks per second | Allocated per vertex | Peak working set |
|---|---|---|---|---|
| All tracks, `Read` or `Observe` | 6.5 s | 183,000 | 18 B | 57 MiB |
| Puget Sound bounding box | 1.2 s | 25,000 | 17 B | 57 MiB |

Stepping SQLite through the same rows without reading any column takes 3.9 s, so that is the floor. The rest is decoding 217 million vertices into `Position` values, which is where the 16 bytes per vertex go. The row loop steps a prepared statement through SQLitePCLRaw, the API underneath Microsoft.Data.Sqlite, because that returns the geometry blob and text columns as spans over SQLite's memory; going through the ADO.NET reader instead cost three times as long, mostly in blob streaming, date parsing and per-column type checks.

`Solutions/Ais.Net.MarineCadastre.Benchmarks` has track benchmarks too, over a generated file with the shape of a real month; set `AIS_NET_MARINECADASTRE_GPKG` to a real file to measure that instead:

```powershell
dotnet run -c Release --project Solutions/Ais.Net.MarineCadastre.Benchmarks -- --filter *VesselTrack* --job short
```

## Transit counts

MarineCadastre's yearly **AIS Vessel Transit Counts** product is a GeoTIFF raster: a grid of 100 m cells in Web Mercator, each holding how many vessel transits crossed it in the year. A transit is one passage of one vessel through a cell, however many position reports it sent there, so this is a density product rather than a message archive. The 2025 file is a 534 MiB BigTIFF of 360,676 × 56,286 cells (20 billion) in 512-cell LZW tiles with seven Deflate overviews, of which 371 million cells hold data and the rest are NoData.

`Ais.Net.MarineCadastre.Gis` reads it without GDAL:

```csharp
using Ais.Net.MarineCadastre;
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

`Observe` streams the cells that hold data as `TransitCell` values (column, row, centre `Position`, count), tile by tile from north-west to south-east, on the same cold-observable terms as the tracks reader. `ObserveTiles` streams `TransitTile` values instead, a rectangle of counts with its geographic bounds, for consumers that want the grid. `Read` and `ReadTiles` are the pull-based twins, and the static `MarineCadastreTransitCountReader.Observe(path)` opens and closes the file per subscription. `CountAt` is the cheap path for a point: one tile read, cached for the next lookup in the same tile.

Options are `Bounds` (only tiles touching the box are read; cells are kept when their centre is inside), `Level` (0 is the exact 100 m grid; each overview doubles the cell and is resampled by whatever built the file) and `MinimumTransits`. `Extent`, `Dimensions`, `CellSize`, `NoData` and the band `Statistics` GDAL stored in the file are exposed on the raster. Tiles that hold no data are skipped without decoding once one has been seen, since identical content compresses to identical bytes.

Two caveats the API cannot hide. Cells are 100 m in Web Mercator metres, which shrink with latitude, so a cell at 47°N is about 68 m across on the water and counts are not per equal area. And the 2025 file's extent runs from 174°W east to 150°E but only from 10°N to 51.7°N, so it leaves out most of Alaska.

The reader handles the file as published and the nearby variations: classic TIFF or BigTIFF, either byte order, tiles or strips, no compression, LZW or Deflate, the horizontal-differencing predictor, 8, 16 or 32-bit integer cells, Web Mercator declared by EPSG code or as ArcGIS's user-defined CRS, or geographic coordinates. Other projections are refused with the CRS named.

Measured on the 2025 file on the same desktop:

| Operation | Result | Elapsed |
|---|---|---|
| `CountAt`, four lookups in different tiles | one 70 KB tile each | 18 ms in total |
| Puget Sound box, `Read` | 116,153 cells | 0.04 s |
| Whole file, `Read` or `Observe` | 371,149,894 cells; count, maximum and mean match the stored statistics exactly | 23 s |
| Whole file, `ReadTiles` | 7,643 non-empty tiles, 2 billion cells | 13 s |

The whole-file read allocates about 1 MiB in total and peaks at 45 MiB of working set, since `TransitCell` is a struct and tiles decode into one reused buffer. The ten seconds between the tile and cell figures is the per-cell scan of two billion decoded values and the Web Mercator conversion of the 371 million centres.

## Licenses

Ais.Net.MarineCadastre is available under the Apache 2.0 open source license.

The MarineCadastre data is produced by the US Coast Guard Navigation Center and published by NOAA and BOEM; see their site for terms of use.

## Project Sponsor

This project is sponsored by [endjin](https://endjin.com), a UK based Technology Consultancy which specializes in Data & Analytics, AI & Cloud Native App Dev, and is a [.NET Foundation Corporate Sponsor](https://dotnetfoundation.org/membership/corporate-sponsorship).
