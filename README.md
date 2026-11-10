# Ais.Net.MarineCadastre

[MarineCadastre.gov](https://hub.marinecadastre.gov/pages/vesseltraffic) (NOAA / BOEM) publishes years of archived AIS data for US coastal waters. These libraries read it in .NET, without GDAL. The CSV reader turns each row into the same [Ais.Net.Models](https://github.com/ais-dotnet/Ais.Net.Models) messages that a live [Ais.Net.Receiver](https://github.com/ais-dotnet/Ais.Net.Receiver) stream produces, so the same code can run on live and archived traffic.

## Which package do I need?

| MarineCadastre product                 | File format         | Package                        | You get                                              |
|----------------------------------------|---------------------|--------------------------------|------------------------------------------------------|
| Daily vessel traffic                   | CSV                 | **Ais.Net.MarineCadastre**     | AIS position and static-data messages                |
| Monthly AIS Vessel Tracks              | GeoPackage (`.gpkg`)| **Ais.Net.MarineCadastre.Gis** | One `VesselTrack` per vessel per part of a day       |
| Yearly AIS Vessel Transit Counts       | GeoTIFF (`.tif`)    | **Ais.Net.MarineCadastre.Gis** | Transit counts per 100 m cell, as cells or tiles     |

Both packages target .NET 10. Ais.Net.MarineCadastre depends only on Ais.Net.Models. Ais.Net.MarineCadastre.Gis also depends on Microsoft.Data.Sqlite and System.Reactive, so the CSV package stays free of a native SQLite dependency.

```powershell
dotnet add package Ais.Net.MarineCadastre
dotnet add package Ais.Net.MarineCadastre.Gis
```

The packages do not include any MarineCadastre data. Download the files you need from [MarineCadastre.gov](https://hub.marinecadastre.gov/pages/vesseltraffic).

## Read the daily CSV

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

Each row gives one position report. The first time the reader sees a vessel, and whenever that vessel's static columns change, it also gives a static-data message, so the output looks like a live feed. `ReadAsync` and `ReadFileAsync` return an `IAsyncEnumerable`. A full day of 8.5 million rows reads in about 2 seconds.

More: [what each row becomes, skipped rows and compressed files](https://github.com/ais-dotnet/Ais.Net.MarineCadastre/blob/main/docs/csv.md).

## Read vessel tracks

```csharp
using System.Reactive.Linq;
using Ais.Net.MarineCadastre;
using Ais.Net.MarineCadastre.Tracks;

VesselTrackReaderOptions pugetSound = new()
{
    Bounds = new BoundingBox(West: -122.6, South: 47.4, East: -122.2, North: 47.8),
};

IDisposable subscription = MarineCadastreTrackReader.Observe("AISVesselTracks2026-01.gpkg", pugetSound)
    .Subscribe(track => Console.WriteLine($"{track.Mmsi} {track.VesselName}: {track.Points.Count} positions"));
```

A track is one vessel's path over part of one day, with its static data. The positions have no individual timestamps: the file stores only each track's start and end time. `Observe` is a cold observable that reads on a thread-pool thread; `Read` returns the same tracks as an `IEnumerable`. You can filter by area, time window and MMSI. The area filter uses the file's spatial index.

More: [track contents, filters and errors](https://github.com/ais-dotnet/Ais.Net.MarineCadastre/blob/main/docs/vessel-tracks.md).

## Read transit counts

```csharp
using Ais.Net.MarineCadastre.TransitCounts;
using Ais.Net.Models.Abstractions;

using TransitCountRaster raster = MarineCadastreTransitCountReader.Open("ais-transit-count-2025.tif");

uint underSeattle = raster.CountAt(new Position(47.60, -122.34));
```

Each cell holds the number of vessel transits that crossed it in the year. Besides point lookups, the raster streams its cells or tiles, filtered by area and minimum count. The cells are 100 m squares in Web Mercator, so they cover less ground further north.

More: [streaming cells and tiles, options and caveats](https://github.com/ais-dotnet/Ais.Net.MarineCadastre/blob/main/docs/transit-counts.md).

## Documentation

- [Vessel-traffic CSV](https://github.com/ais-dotnet/Ais.Net.MarineCadastre/blob/main/docs/csv.md)
- [Vessel tracks](https://github.com/ais-dotnet/Ais.Net.MarineCadastre/blob/main/docs/vessel-tracks.md)
- [Transit counts](https://github.com/ais-dotnet/Ais.Net.MarineCadastre/blob/main/docs/transit-counts.md)
- [Namespaces and shared types](https://github.com/ais-dotnet/Ais.Net.MarineCadastre/blob/main/docs/README.md)
- [Building and testing](https://github.com/ais-dotnet/Ais.Net.MarineCadastre/blob/main/docs/building.md)

Each product page includes measured performance and how to run the benchmarks.

## Licenses

Ais.Net.MarineCadastre is available under the Apache 2.0 open source license.

The MarineCadastre data is produced by the US Coast Guard Navigation Center and published by NOAA and BOEM. See their site for terms of use. The repository does not include any of it: the tests use synthetic data, and the CSV benchmarks download their sample from NOAA when they run.

## Project Sponsor

This project is sponsored by [endjin](https://endjin.com), a UK based Technology Consultancy which specializes in Data & Analytics, AI & Cloud Native App Dev.

> We help small teams achieve big things.

We produce three free weekly newsletters:

 - [Azure Weekly](https://azureweekly.info) for all things about the Microsoft Azure Platform
 - [Power BI Weekly](https://powerbiweekly.info) for all things Power BI
 - [Fabric Weekly](https://fabricweekly.info) for all things Microsoft Fabric

Keep up with everything that's going on at endjin via our [blog](https://endjin.com/blog), follow us on [Twitter](https://twitter.com/endjin), [YouTube](https://www.youtube.com/c/endjin) or [LinkedIn](https://www.linkedin.com/company/endjin).

We have become the maintainers of a number of popular .NET Open Source Projects:

- [Reactive Extensions for .NET](https://github.com/dotnet/reactive)
- [Reaqtor](https://github.com/reaqtive)
- [Argotic Syndication Framework](https://github.com/argotic-syndication-framework/)

And we have over 50 Open Source projects of our own, spread across the following GitHub Orgs:

- [endjin](https://github.com/endjin/)
- [Corvus](https://github.com/corvus-dotnet)
- [Menes](https://github.com/menes-dotnet)
- [Marain](https://github.com/marain-dotnet)
- [AIS.NET](https://github.com/ais-dotnet)

And the DevOps tooling we have created for managing all these projects is available on the [PowerShell Gallery](https://www.powershellgallery.com/profiles/endjin).

For more information about our products and services, or for commercial support of this project, please [contact us](https://endjin.com/contact-us).

## Code of conduct

This project has adopted a code of conduct adapted from the [Contributor Covenant](http://contributor-covenant.org/) to clarify expected behaviour in our community. This code of conduct has been [adopted by many other projects](http://contributor-covenant.org/adopters/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [&#104;&#101;&#108;&#108;&#111;&#064;&#101;&#110;&#100;&#106;&#105;&#110;&#046;&#099;&#111;&#109;](&#109;&#097;&#105;&#108;&#116;&#111;:&#104;&#101;&#108;&#108;&#111;&#064;&#101;&#110;&#100;&#106;&#105;&#110;&#046;&#099;&#111;&#109;) with any additional questions or comments.
