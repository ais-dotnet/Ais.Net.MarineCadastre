# Ais.Net.MarineCadastre

Readers for the archived AIS data that [MarineCadastre.gov](https://hub.marinecadastre.gov/pages/vesseltraffic) (NOAA / BOEM) publishes. There are two packages:

- **Ais.Net.MarineCadastre** reads the daily vessel-traffic CSV files and converts each row into [Ais.Net.Models](https://github.com/ais-dotnet/Ais.Net.Models) message types. Code written for a live [Ais.Net.Receiver](https://github.com/ais-dotnet/Ais.Net.Receiver) stream can then run over years of archived US coastal traffic.
- **Ais.Net.MarineCadastre.Gis** reads two GIS products published alongside the CSVs, without GDAL:
  - the monthly AIS Vessel Tracks GeoPackage, as an `IObservable<VesselTrack>`. A track is one vessel's path over part of a day, with its static data. See [Vessel tracks](#vessel-tracks).
  - the yearly AIS Vessel Transit Counts GeoTIFF, as an `IObservable` of cells or tiles, with point lookups. See [Transit counts](#transit-counts).

Each product has its own namespace: `Ais.Net.MarineCadastre.Csv`, `Ais.Net.MarineCadastre.Tracks` and `Ais.Net.MarineCadastre.TransitCounts`. Each holds that product's reader, its options and the types it produces. The root `Ais.Net.MarineCadastre` namespace holds the types they share, `BoundingBox` and `ShipTypeSource`. These live in the Ais.Net.MarineCadastre package, which Ais.Net.MarineCadastre.Gis depends on.

`BoundingBox` checks its values when you construct it. Latitudes must be between -90 and 90 degrees, longitudes between -180 and 180 degrees, and none may be NaN. `West` must not exceed `East`, and `South` must not exceed `North`. So a box with latitude and longitude swapped throws an exception instead of silently matching nothing. Its properties are get-only, so a `with` expression cannot bypass these checks.

## Vessel-traffic CSV

### Usage

```csharp
using Ais.Net.MarineCadastre.Csv;
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

`ReadAsync` and `ReadFileAsync` return an `IAsyncEnumerable<AisMessageBase>`. Every message's `Timestamp` is the row's `base_date_time`, as UTC.

### What a row becomes

A MarineCadastre row is a denormalised join of a position report and the vessel's static data. The reader splits it back into AIS messages:

| Row                         | Position report                         | Static data, on first sight or change                                       |
|-----------------------------|-----------------------------------------|-----------------------------------------------------------------------------|
| Class A (`transceiver` = A) | Message type 1 (configurable to 2 or 3) | Message type 5                                                              |
| Class B (`transceiver` = B) | Message type 18                         | Message type 19 in place of that row's type 18, then message type 24 part 1 |

Static data is emitted the first time an MMSI appears and again whenever its static columns change. The output therefore looks like a live feed: many position reports and occasional static reports. To get position reports only, set `EmitStaticData = false` on `MarineCadastreCsvReaderOptions`.

Some columns exist in the CSV but not in AIS, and the reverse. They map as follows:

- An empty `heading` becomes 511 and an empty `status` becomes `NavigationStatus.NotDefined`, the AIS "not available" values. An empty `sog` or `cog` becomes `null`.
- `length` and `width` go to `DimensionToBow` and `DimensionToPort`. The data does not give the reference point, so stern and starboard are 0.
- `imo` loses its `IMO` prefix. `draft` in metres becomes `Draught10thMetres`.
- The ship type comes from `vessel_type`, falling back to `cargo`. `ShipTypeSource` on the options swaps that order. Codes outside 0 to 99 become `ShipType.NotAvailable`.
- Rate of turn, RAIM, radio status, ETA, destination and the Class B capability flags are not in the CSV, so they get their "not available" values.

Header names match case-insensitively and ignore underscores, so both spellings MarineCadastre has used (`BaseDateTime` and `base_date_time`) work. Column order does not matter. `ClassAMessageType` on the options sets the Class A message type. It must be 1, 2 or 3; any other value throws `ArgumentOutOfRangeException`.

#### Skipped rows

The reader skips a data row that it cannot parse and continues with the next row. A row is skipped when it has too few fields for the required columns, when its `mmsi` is empty or not a number, or when its `base_date_time` is not a readable date and time. A missing or out-of-range `latitude` or `longitude` does not cause a skip: the row is emitted with a `null` position. The header and blank lines are never reported.

To see the skipped rows, set `OnRowSkipped` on the options. The callback gets a `SkippedCsvRow` with the 1-based physical line number (the header is line 1), a `CsvRowSkipReason` and the raw UTF-8 bytes of the line. The bytes are valid only during the call, so copy what you need. The callback runs on the reading thread before the next row is read. If it throws, the read ends with that exception, so throwing makes the read strict:

```csharp
MarineCadastreCsvReaderOptions options = new()
{
    OnRowSkipped = row => Console.Error.WriteLine(
        $"Line {row.LineNumber}: {row.Reason}: {Encoding.UTF8.GetString(row.Line)}"),
};

MarineCadastreCsvReaderOptions strict = new()
{
    OnRowSkipped = row => throw new InvalidDataException($"Line {row.LineNumber}: {row.Reason}"),
};
```

### Performance

The reader streams. It holds one 256 KB block of the file and splits lines and fields in place, without creating strings. It allocates one record per position report and creates strings only for static-data messages. To de-duplicate static data, a `Dictionary<uint, ulong>` keeps an 8-byte signature per vessel.

Measured on the 1 January 2026 daily file (828 MiB, 8.5 million rows) on a desktop machine, .NET 10, reading from disk:

| Mode             | Elapsed | Messages per second | Allocated per message | Peak working set |
|------------------|---------|---------------------|-----------------------|------------------|
| Positions only   | 1.9 s   | 4.5 million         | 134 B                 | 47 MiB           |
| With static data | 2.5 s   | 3.4 million         | 134 B                 | 48 MiB           |

134 bytes is the size of one `AisMessageType1Through3` record; nothing else is allocated per row. Three things keep the per-row cost down:

- Field splitting uses a vectorised comma scan for the usual line with no quotes.
- Numbers take an exact fast path for fixed-point text.
- The date part of the timestamp is converted once per day, not once per row.

The `OnRowSkipped` callback adds no cost to rows that parse, and it allocates nothing per skipped row.

`Solutions/Ais.Net.MarineCadastre.Benchmarks` measures each layer: line splitting, field splitting, positions only, with static data, and async. The repository holds no MarineCadastre data. On first run the CSV benchmarks download the start of the 1 January 2026 daily file from NOAA (about 2 MB of the 239 MB `.csv.zst`, stopping after 10,000 rows) and cache it under your local application data folder, in `Ais.Net.MarineCadastre/samples`. To measure a whole day instead, point `AIS_NET_MARINECADASTRE_CSV` at a daily file:

```powershell
$env:AIS_NET_MARINECADASTRE_CSV = "D:\data\AIS_2026_01_01.csv"
dotnet run -c Release --project Solutions/Ais.Net.MarineCadastre.Benchmarks -- --filter *
```

## Vessel tracks

MarineCadastre publishes a monthly **AIS Vessel Tracks** product. It is a [GeoPackage](https://www.geopackage.org/), which is an SQLite database, with one row per vessel track. A track is one vessel's path over part of one day, stored as a `MULTILINESTRING` in WGS84, with the vessel's static data in the same row. The January 2026 file is 3.9 GB and holds 1.19 million tracks with 217 million vertices.

The vertices are the same positions as the daily CSV rows, with consecutive repeats removed and the line cut at midnight and at gaps. They are stored as a plain 2D line, so individual vertices have no timestamp: only the track's start and end times are known.

### Usage

```csharp
using System.Reactive.Linq;
using Ais.Net.MarineCadastre;
using Ais.Net.MarineCadastre.Tracks;

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

`Observe` returns a cold observable. Each subscription opens the file read-only, reads on a thread-pool thread, emits tracks in file order and completes. Disposing the subscription stops the read, and failures arrive through `OnError`. For pull-based code, `Read` returns the same tracks as a lazy `IEnumerable<VesselTrack>`.

### What a track contains

A `VesselTrack` has:

- the row id and MMSI;
- start and end times (UTC) and the duration;
- the positions, as `IReadOnlyList<Position>`, and their bounding `Envelope`;
- the static columns: vessel name, call sign, IMO number, ship type (from `vessel_type`, falling back to `cargo`; `ShipTypeSource` on the options swaps that order, as for the CSV reader) and MarineCadastre's name for it, navigation status, length, width, draught, cargo code and transceiver class.

It implements the Ais.Net.Models `IVesselIdentity`, `IVesselName`, `ICallSign`, `IShipType` and `IVesselNavigationStatus` interfaces. Empty columns are `null`, except where an interface needs a value; there, the AIS "not available" value is used.

The positions list is read-only: it cannot be cast back to an array or another mutable collection. If a track's geometry has a coordinate that is NaN or outside the WGS84 range, the reader reports it with an `InvalidDataException`.

### Filtering

The filters in `VesselTrackReaderOptions` are optional and combine:

- `Bounds` uses the file's R-tree index, so a query for one harbour reads only that harbour's rows. The Puget Sound box above returns 29,465 of the month's tracks in about a second.
- `From` and `To` keep tracks that overlap the window.
- `Mmsi` keeps one vessel.

The reader finds the feature table and geometry column from the GeoPackage's own registry, so a file for any month works. Attribute columns match by name, ignoring case and underscores.

### Performance

Reading the whole January 2026 file on the same desktop as above, from the page cache:

| Mode                            | Elapsed | Tracks per second | Allocated per vertex | Peak working set |
|---------------------------------|---------|-------------------|----------------------|------------------|
| All tracks, `Read` or `Observe` | 5.5 s   | 215,000           | 18 B                 | 56 MiB           |
| Puget Sound bounding box        | 1.2 s   | 25,000            | 17 B                 | 57 MiB           |

The floor is about 2 s: that is how long SQLite takes to step through the same rows and touch the geometry blob, with nothing decoded. The `StepRowsOnly` and `StepAndDecodeGeometry` benchmarks measure these layers. The rest of the time goes to decoding 217 million vertices into `Position` values, which is also where 16 of the bytes per vertex go.

The row loop steps a prepared statement through SQLitePCLRaw, the API underneath Microsoft.Data.Sqlite, because it returns the geometry blob and text columns as spans over SQLite's memory. Going through the ADO.NET reader instead took three times as long, mostly in blob streaming, date parsing and per-column type checks. Three smaller changes also help:

- Numeric columns are selected through `ifnull`, so a null costs no extra native call.
- Position arrays are allocated without zeroing, since every element is written.
- A vessel's name, call sign and type name are reused across its consecutive tracks instead of allocated again.

The read-only wrapper around each track's positions costs 24 bytes per track.

`Solutions/Ais.Net.MarineCadastre.Benchmarks` has track benchmarks too. By default they run over a generated file with the shape of a real month; set `AIS_NET_MARINECADASTRE_GPKG` to a real file to measure that instead:

```powershell
dotnet run -c Release --project Solutions/Ais.Net.MarineCadastre.Benchmarks -- --filter *VesselTrack* --job short
```

## Transit counts

MarineCadastre's yearly **AIS Vessel Transit Counts** product is a GeoTIFF raster: a grid of 100 m cells in Web Mercator. Each cell holds the number of vessel transits that crossed it in the year. A transit is one passage of one vessel through a cell, however many position reports the vessel sent there, so this is a density product, not a message archive.

The 2025 file is a 534 MiB BigTIFF of 360,676 × 56,286 cells (20 billion), in 512-cell LZW tiles with seven Deflate overviews. 371 million of those cells hold data; the rest are NoData.

### Usage

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

- `Observe` streams the cells that hold data as `TransitCell` values (column, row, centre `Position`, count). It goes tile by tile from north-west to south-east, and is a cold observable on the same terms as the tracks reader.
- `ObserveTiles` streams `TransitTile` values instead: a rectangle of counts with its geographic bounds, for consumers that want the grid. `Counts` always holds `Width` × `Height` values, and the indexer rejects cells outside the tile.
- `Read` and `ReadTiles` are the pull-based equivalents.
- `CountAt` is the cheap path for a single point. It reads one tile and caches it for the next lookup in the same tile.

The static `MarineCadastreTransitCountReader.Observe(path)` opens and closes the file for each subscription.

### Options

`TransitCountReaderOptions` has three options:

- `Bounds`: only tiles that touch the box are read, and a cell is kept when its centre is inside the box.
- `Level`: 0 is the exact 100 m grid. Each overview level doubles the cell size, resampled by whatever tool built the file.
- `MinimumTransits`: the smallest count a cell must have to be returned.

The raster also exposes `Extent`, `Dimensions`, `CellSize`, `NoData` and the band `Statistics` that GDAL stored in the file. Once the reader has seen one tile with no data, it skips other such tiles without decoding them, because identical content compresses to identical bytes.

### Caveats

The API cannot hide two properties of the data:

- Cells are 100 m in Web Mercator metres, and those shrink with latitude. A cell at 47°N is about 68 m across on the water, so counts are not per equal area.
- The 2025 file's extent runs from 174°W east to 150°E, but only from 10°N to 51.7°N. It leaves out most of Alaska.

### Supported files

The reader handles the file as published and nearby variations:

- classic TIFF or BigTIFF, in either byte order;
- tiles or strips;
- no compression, LZW or Deflate, with or without the horizontal-differencing predictor;
- 8, 16 or 32-bit integer cells;
- Web Mercator declared by EPSG code or as ArcGIS's user-defined CRS, or geographic coordinates.

It refuses other projections with an error that names the CRS.

### Performance

Measured on the 2025 file on the same desktop:

| Operation                                  | Result                                                                         | Elapsed          |
|--------------------------------------------|--------------------------------------------------------------------------------|------------------|
| `CountAt`, four lookups in different tiles | one 70 KB tile each, decoded only as far as the cell                           | under 10 ms each |
| Puget Sound box, `Read`                    | 116,153 cells                                                                  | 0.06 s           |
| Whole file, `Read` or `Observe`            | 371,149,894 cells; count, maximum and mean match the stored statistics exactly | 14 s             |
| Whole file, `ReadTiles`                    | 7,643 non-empty tiles, 2 billion cells                                         | 10 s             |

The whole-file read allocates about 1 MiB in total and peaks at 45 MiB of working set, because `TransitCell` is a struct and tiles decode into one reused buffer.

Most of the time goes to decoding the LZW tiles. The decoder keeps its code table as offsets into the output it has already produced, so each code is one block copy. That suits a raster where most codes stand for long runs of NoData. The cell scan skips NoData runs with a vectorised search and converts each row's latitude once, so it adds a few seconds over the tile read instead of ten. Because LZW is sequential, a point lookup decodes a tile only as far as the cell it needs.

## Licenses

Ais.Net.MarineCadastre is available under the Apache 2.0 open source license.

The MarineCadastre data is produced by the US Coast Guard Navigation Center and published by NOAA and BOEM. See their site for terms of use. The repository does not include any of it: the tests use synthetic rows, and the CSV benchmarks download their sample from NOAA when they run.

## Project Sponsor

This project is sponsored by [endjin](https://endjin.com), a UK based Technology Consultancy which specializes in Data & Analytics, AI & Cloud Native App Dev, and is a [.NET Foundation Corporate Sponsor](https://dotnetfoundation.org/membership/corporate-sponsorship).
