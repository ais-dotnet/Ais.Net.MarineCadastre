# Ais.Net.MarineCadastre

Reads the archived AIS vessel-traffic CSV files published by [MarineCadastre.gov](https://hub.marinecadastre.gov/pages/vesseltraffic) (NOAA / BOEM) and converts each row into [Ais.Net.Models](https://github.com/ais-dotnet/Ais.Net.Models) message types, so the same code that consumes a live [Ais.Net.Receiver](https://github.com/ais-dotnet/Ais.Net.Receiver) stream can run over years of archived US coastal traffic.

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

## Licenses

Ais.Net.MarineCadastre is available under the Apache 2.0 open source license.

The MarineCadastre data is produced by the US Coast Guard Navigation Center and published by NOAA and BOEM; see their site for terms of use.

## Project Sponsor

This project is sponsored by [endjin](https://endjin.com), a UK based Technology Consultancy which specializes in Data & Analytics, AI & Cloud Native App Dev, and is a [.NET Foundation Corporate Sponsor](https://dotnetfoundation.org/membership/corporate-sponsorship).
