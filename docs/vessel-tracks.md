# Vessel tracks

MarineCadastre publishes a monthly **AIS Vessel Tracks** product. It is a [GeoPackage](https://www.geopackage.org/), which is an SQLite database, with one row per vessel track. A track is one vessel's path over part of one day, stored as a `MULTILINESTRING` in WGS84, with the vessel's static data in the same row. The January 2026 file is 3.9 GB and holds 1.19 million tracks with 217 million vertices.

The vertices are the same positions as the daily CSV rows, with consecutive repeats removed and the line cut at midnight and at gaps. They are stored as a plain 2D line, so individual vertices have no timestamp: only the track's start and end times are known.

The **Ais.Net.MarineCadastre.Gis** package reads these files.

## Usage

```csharp
using System.Reactive.Linq;
using Ais.Net;
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

## What a track contains

A `VesselTrack` has:

- the row id and MMSI;
- start and end times (UTC) and the duration;
- the positions, as `IReadOnlyList<Position>`, and their bounding `Envelope`;
- the static columns: vessel name, call sign, IMO number, ship type (from `vessel_type`, falling back to `cargo`; `ShipTypeSource` on the options swaps that order, as for the CSV reader) and MarineCadastre's name for it, navigation status, length, width, draught, cargo code and transceiver class.

It implements the Ais.Net.Models `IVesselIdentity`, `IVesselName`, `ICallSign`, `IShipType` and `IVesselNavigationStatus` interfaces. Empty columns are `null`, except where an interface needs a value; there, the AIS "not available" value is used.

The positions list is read-only: it cannot be cast back to an array or another mutable collection.

## Errors

The reader accepts only geometries in WGS84 (GeoPackage `srs_id` 4326), which is what MarineCadastre publishes. It reports these problems with an `InvalidDataException`:

- a file that is not a GeoPackage;
- a track whose geometry declares another spatial reference system, or has a coordinate that is NaN or outside the WGS84 range;
- a row with no MMSI, or with a start or end time that it cannot read.

A GeoPackage that does not store its text as UTF-8 causes a `NotSupportedException`. A row whose geometry is null or has no positions is not a track, so the reader skips it without a report.

## Filtering

The filters in `VesselTrackReaderOptions` are optional and combine:

- `Bounds` uses the file's R-tree index, so a query for one harbour reads only that harbour's rows. The Puget Sound box above returns 29,465 of the month's tracks in about a second.
- `From` and `To` keep tracks that overlap the window.
- `Mmsi` keeps one vessel.

The reader finds the feature table and geometry column from the GeoPackage's own registry, so a file for any month works. By default it reads the first features table in `gpkg_contents`, in name order. That is the right table for MarineCadastre's files, because each holds one month in one table. To read a different table, set `TableName`. Attribute columns match by name, ignoring case and underscores.

## Performance

Reading the whole January 2026 file on the same desktop as the [CSV measurements](csv.md#performance), from the page cache:

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

### Benchmarks

`Solutions/Ais.Net.MarineCadastre.Benchmarks` has track benchmarks too. By default they run over a generated file with the shape of a real month; set `AIS_NET_MARINECADASTRE_GPKG` to a real file to measure that instead:

```powershell
dotnet run -c Release --project Solutions/Ais.Net.MarineCadastre.Benchmarks -- --filter *VesselTrack* --job short
```
