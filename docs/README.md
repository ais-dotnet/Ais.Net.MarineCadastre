# Ais.Net.MarineCadastre documentation

- [Vessel-traffic CSV](csv.md): what each row becomes, column mapping, skipped rows, performance.
- [Vessel tracks](vessel-tracks.md): the monthly GeoPackage, track contents, filters, errors, performance.
- [Transit counts](transit-counts.md): the yearly GeoTIFF, lookups and streams, options, supported files, performance.
- [Building and testing](building.md): the SDK, the CI build, public API tracking, lock files, benchmarks.

## Namespaces

Each product has its own namespace: `Ais.Net.MarineCadastre.Csv`, `Ais.Net.MarineCadastre.Tracks` and `Ais.Net.MarineCadastre.TransitCounts`. Each holds that product's reader, its options and the types it produces. The root `Ais.Net.MarineCadastre` namespace holds the types they share, `BoundingBox` and `ShipTypeSource`. These live in the Ais.Net.MarineCadastre package, which Ais.Net.MarineCadastre.Gis depends on.

## BoundingBox

`BoundingBox` checks its values when you construct it. Latitudes must be between -90 and 90 degrees, longitudes between -180 and 180 degrees, and none may be NaN. `West` must not exceed `East`, and `South` must not exceed `North`. So a box with latitude and longitude swapped throws an exception instead of silently matching nothing. Its properties are get-only, so a `with` expression cannot bypass these checks. A box cannot cross the antimeridian.
