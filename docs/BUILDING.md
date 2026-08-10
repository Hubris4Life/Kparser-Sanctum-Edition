# Building from source

These instructions describe the source layout in this repository. Public release builds should be produced from a clean tagged checkout.

## Prerequisites

- Windows 10 version 1809 or later
- Visual Studio with .NET desktop development tools
- .NET 10 SDK
- .NET Framework 3.5 targeting tools for the legacy projects
- Microsoft SQL Server Compact 4.0 development/runtime files
- Inno Setup 6 for the setup executable
- PowerShell for packaging tasks

The legacy projects contain an absolute reference to the normal SQL Server Compact 4.0 installation path. If SQL Server Compact is installed elsewhere, update the reference locally or replace it with a documented reproducible dependency mechanism before publishing a release.

## Build the legacy engine

From a Visual Studio Developer PowerShell:

    msbuild .\src\legacy-engine\FFXILogParser.sln /t:Build /p:Configuration=Release /p:Platform=x86

The engine must remain x86 because its memory structures and SQL Server Compact native dependencies are 32-bit.

`src/legacy-engine/ParserCore/KPDatabase.sdf` is the empty ten-table schema template embedded by ParserCore. It contains no parse, player, or account rows and must remain in the source tree for reproducible builds.

The legacy tree retains its historical ZedGraph reference. Include the exact ZedGraph 5.1.5 license and source archive when distributing the resulting DLLs. See [Third-party notices](THIRD-PARTY-NOTICES.md).

## Build the modern interface

    dotnet restore .\src\modern-ui\KParser.Sanctum.UI\KParser.Sanctum.UI.csproj
    dotnet build .\src\modern-ui\KParser.Sanctum.UI\KParser.Sanctum.UI.csproj -c Release

To run during development:

    dotnet run --project .\src\modern-ui\KParser.Sanctum.UI\KParser.Sanctum.UI.csproj

The development run expects a compatible engine in the location handled by EngineProcessManager. If it is absent, the interface reports that live engine data is unavailable.

## Run available smoke projects

    dotnet run --project .\tests\PlayerComparisonSmoke\PlayerComparisonSmoke.csproj
    dotnet run --project .\tests\KParserBridgeInstallerSmoke\KParserBridgeInstallerSmoke.csproj

PortableSmoke requires two arguments: the published portable assembly path and a disposable extraction directory. Run it only after producing a portable build.

    dotnet run --project .\tests\PortableSmoke\PortableSmoke.csproj -- PATH_TO_PORTABLE_ASSEMBLY PATH_TO_EMPTY_EXTRACTION_DIRECTORY

BridgeReportSmoke and StatCaptureProbe target the legacy framework and are diagnostic tools rather than standalone unit-test suites. Run them only against controlled local data and never commit generated memory reports or parse databases.

## Publish the dashboard

An installed self-contained dashboard can be produced with:

    dotnet publish .\src\modern-ui\KParser.Sanctum.UI\KParser.Sanctum.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false -p:SatelliteResourceLanguages=en -o .\artifacts\dashboard

Stage the complete tested engine and its required redistributable files under:

    installer\payload\current\Engine

Stage the published dashboard and README-FIRST.txt directly under:

    installer\payload\current

Do not commit installer payloads. They are generated release artifacts.

## Build the setup executable

After staging the payload, compile:

    installer\KParser-Sanctum.iss

with Inno Setup 6. The script writes the installer to installer/output.

## Portable single-file build

The portable configuration embeds a zip named EnginePayload.zip as a managed resource. Create that zip from the same tested engine payload used by the setup release, place it temporarily under the modern project's Assets directory, and publish with the SanctumPortable property enabled.

    dotnet publish .\src\modern-ui\KParser.Sanctum.UI\KParser.Sanctum.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:SanctumPortable=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false -p:SatelliteResourceLanguages=en -o .\artifacts\portable

Remove the generated EnginePayload.zip after publishing. It is intentionally ignored by Git.

The release script creates both a standard ZIP and a smaller solid-compressed
7z archive from the same portable payload. The 7z archive is the compact
download; the ZIP remains available for broader built-in Windows extraction
compatibility.

## Reproducibility requirement

For every binary release, retain the exact tag, tool versions, packaging definition, dependency versions, and source tree used to produce it. The source need not reproduce a byte-for-byte identical executable when timestamps or tool metadata differ, but it must be the actual preferred source used for the release.
