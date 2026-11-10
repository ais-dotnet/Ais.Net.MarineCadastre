# Vessel-traffic CSV

The **Ais.Net.MarineCadastre** package reads the daily vessel-traffic CSV files that MarineCadastre publishes and converts each row into [Ais.Net.Models](https://github.com/ais-dotnet/Ais.Net.Models) message types. Code written for a live [Ais.Net.Receiver](https://github.com/ais-dotnet/Ais.Net.Receiver) stream can then run over years of archived US coastal traffic.

## Usage

```csharp
using Ais.Net.MarineCadastre.Csv;
using Ais.Net.Models;
using Ais.Net.Models.Abstractions;

foreach (AisMessageBase message in MarineCadastreCsvReader.ReadFile("ais-2026-01-01.csv"))
{
    if (message is IVesselNavigation { Position: { } position })
    {
        Console.WriteLine($"{message.Timestamp:u} {message.Mmsi} {position.Latitude},{position.Longitude}");
    }
}
```

`ReadAsync` and `ReadFileAsync` return an `IAsyncEnumerable<AisMessageBase>`. Every message's `Timestamp` is the row's `base_date_time`, as UTC.

### Compressed files

NOAA publishes the 2026 daily files Zstandard-compressed, as `.csv.zst`. The reader does not decompress them, but `Read` and `ReadAsync` accept any `Stream`, so you can pass a decompressing stream instead of unpacking the file first. The benchmarks do this with the `DecompressionStream` from [ZstdSharp.Port](https://www.nuget.org/packages/ZstdSharp.Port).

## What a row becomes

A MarineCadastre row is a denormalised join of a position report and the vessel's static data. The reader splits it back into AIS messages:

| Row                         | Position report                         | Static data, on first sight or change                                       |
|-----------------------------|-----------------------------------------|-----------------------------------------------------------------------------|
| Class A (`transceiver` = A) | Message type 1 (configurable to 2 or 3) | Message type 5                                                              |
| Class B (`transceiver` = B) | Message type 18                         | Message type 19 in place of that row's type 18, then message type 24 part 1 |

Static data is emitted the first time an MMSI appears and again whenever its static columns change. The output therefore looks like a live feed: many position reports and occasional static reports. To get position reports only, set `EmitStaticData = false` on `MarineCadastreCsvReaderOptions`.

### Column mapping

Some columns exist in the CSV but not in AIS, and the reverse. They map as follows:

- An empty `heading` becomes 511 and an empty `status` becomes `NavigationStatus.NotDefined`, the AIS "not available" values. An empty `sog` or `cog` becomes `null`. So does a negative value, a `sog` of 102.3 or more, or a `cog` of 360 or more: older files write those AIS "not available" values where newer files leave the field empty.
- `length` and `width` go to `DimensionToBow` and `DimensionToPort`. The data does not give the reference point, so stern and starboard are 0.
- `imo` loses its `IMO` prefix. `draft` in metres becomes `Draught10thMetres`.
- The ship type comes from `vessel_type`, falling back to `cargo`. `ShipTypeSource` on the options swaps that order. Codes outside 0 to 99 become `ShipType.NotAvailable`.
- Rate of turn, RAIM, radio status, ETA, destination and the Class B capability flags are not in the CSV, so they get their "not available" values.

Header names match case-insensitively and ignore underscores, so both spellings MarineCadastre has used (`BaseDateTime` and `base_date_time`) work. Column order does not matter. `ClassAMessageType` on the options sets the Class A message type. It must be 1, 2 or 3; any other value throws `ArgumentOutOfRangeException`.

## Skipped rows

The reader skips a data row that it cannot parse and continues with the next row. A row is skipped when it has too few fields for the required columns, when its `mmsi` is empty or not an unsigned integer, or when its `base_date_time` is not a readable UTC date and time. A fraction of a second or a trailing `Z` is accepted; a UTC offset is not. A missing or out-of-range `latitude` or `longitude` does not cause a skip: the row is emitted with a `null` position. The header and blank lines are never reported.

To see the skipped rows, set `OnRowSkipped` on the options. The callback gets a `SkippedCsvRow` with the 1-based physical line number (the header is line 1), a `CsvRowSkipReason` and the raw UTF-8 bytes of the line. The bytes are valid only during the call, so copy what you need. The callback runs on the reading thread before the next row is read. If it throws, the read ends with that exception, so throwing makes the read strict:

```csharp
using System.Text;
using Ais.Net.MarineCadastre.Csv;

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

## Performance

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

### Benchmarks

`Solutions/Ais.Net.MarineCadastre.Benchmarks` measures each layer: line splitting, field splitting, positions only, with static data, and async with static data. The repository holds no MarineCadastre data. On first run the CSV benchmarks download the start of the 1 January 2026 daily file from NOAA (about 2 MB of the 239 MB `.csv.zst`, stopping after 10,000 rows) and cache it under your local application data folder, in `Ais.Net.MarineCadastre/samples`. To measure a whole day instead, point `AIS_NET_MARINECADASTRE_CSV` at a daily file:

```powershell
$env:AIS_NET_MARINECADASTRE_CSV = "D:\data\ais-2026-01-01.csv"
dotnet run -c Release --project Solutions/Ais.Net.MarineCadastre.Benchmarks -- --filter *
```
