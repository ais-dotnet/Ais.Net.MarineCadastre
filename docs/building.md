# Building and testing

You need the .NET 10 SDK. `global.json` asks for 10.0.403 or a later 10.x release. To build and run the tests:

```powershell
dotnet build Solutions/Ais.Net.MarineCadastre.slnx
dotnet test --solution Solutions/Ais.Net.MarineCadastre.slnx
```

The tests use MSTest on Microsoft.Testing.Platform. They need no MarineCadastre data: `Solutions/Ais.Net.MarineCadastre.Tests/TestSupport` generates the GeoPackage and GeoTIFF fixtures, and the CSV rows are synthetic.

## The CI build

`build.ps1` runs the ZeroFailed build process that CI runs on GitHub Actions (`.github/workflows/build.yml`). It needs PowerShell 7. That process also runs `dotnet format --verify-no-changes`, so a file that does not match the `.editorconfig` rules fails the build. To fix such a failure, run `dotnet format Solutions/Ais.Net.MarineCadastre.slnx`.

## Public API

Both libraries use Microsoft.CodeAnalysis.PublicApiAnalyzers and treat warnings as errors. A change to the public API therefore fails the build until you record it in that project's `PublicAPI.Unshipped.txt`.

## Lock files

Every project has a `packages.lock.json`. A local restore updates it when a package version changes. CI restores in locked mode, so a lock file that does not match the project fails the build. After you change a package version, run `dotnet restore` and commit the lock files with the change.

## Benchmarks

`Solutions/Ais.Net.MarineCadastre.Benchmarks` uses BenchmarkDotNet. Each product page describes its benchmarks and the environment variable that points them at a real file:

- [CSV](csv.md#benchmarks): `AIS_NET_MARINECADASTRE_CSV`
- [Vessel tracks](vessel-tracks.md#benchmarks): `AIS_NET_MARINECADASTRE_GPKG`
- [Transit counts](transit-counts.md#benchmarks): `AIS_NET_MARINECADASTRE_TIF`
