# Third-party notices

This inventory is based on the current Preview 22 source and payload. It must be rechecked for every public binary release. Do not assume that a dependency's current license is identical to the license of the historical binary being distributed.

## KParser

- Component: Original KParser engine and plugins
- Copyright: Copyright (C) 2007-2009 David Smith
- License: GNU GPL version 2 or later
- Notice: src/legacy-engine/Documentation/readme.txt
- Source lineage: https://github.com/poroburu/kparser

## ZedGraph

- Component version observed: 5.1.5.28844
- Copyright metadata: Copyright (C) 2003-2007 John Champion
- Upstream project: https://sourceforge.net/projects/zedgraph/
- Reported upstream license: GNU Library/Lesser GPL version 2
- Release materials: `third_party/ZedGraph-5.1.5` contains the applicable LGPL 2.1 text and the official SourceForge 5.1.5 source archive. The archive metadata uses assembly version `5.1.5.*`, matching the bundled `5.1.5.28844` binary.

## Removed historical dependency: clrzmq and libzmq

The upstream tree contained an unfinished optional PacketReader using clrzmq 2.2.5 and a historical native libzmq binary. Sanctum Edition does not use that reader. The reader, package references, and binaries were removed before the public binary release and are not part of the Sanctum distribution.

## Microsoft SQL Server Compact 4.0

- Components observed: System.Data.SqlServerCe.dll and x86 native SQL CE runtime files
- Vendor: Microsoft
- License: Microsoft proprietary redistributable terms
- Release requirement: Obtain the files through a licensed Microsoft distribution, verify that redistribution is permitted, and include all required Microsoft license/EULA material. Do not treat these binaries as GPL code.

## Microsoft Visual C++ 2008 runtime

- Components observed: msvcr90.dll and Microsoft.VC90.CRT.manifest
- Vendor: Microsoft
- License: Microsoft proprietary redistributable terms
- Release requirement: Distribute only files authorized by the applicable Visual Studio redistributable license and preserve the accompanying notice material.

## Microsoft .NET

- Component: Self-contained .NET 10 Windows desktop runtime in published modern builds
- Vendor/project: Microsoft and .NET Foundation contributors
- Project information: https://github.com/dotnet/runtime
- Release requirement: Preserve the THIRD-PARTY-NOTICES and license files emitted for the exact runtime pack used to publish the application.

## Inno Setup

- Component: Installer build tool; the compiler itself is not intended to be included in the application payload
- Project: https://jrsoftware.org/isinfo.php
- Release requirement: Follow the Inno Setup license and do not misrepresent the installer compiler as part of this project's source.

## Release packaging requirements

Before the first public binary release:

1. Copy `third_party/ZedGraph-5.1.5` into each distribution.
2. Copy the SQL Server Compact EULA and redistribution notice from `third_party/Microsoft-SQL-Server-Compact-4.0`.
3. Copy the Visual C++ 2008 runtime notice from `third_party/Microsoft-VC90-CRT`.
4. Copy the .NET license and third-party notices from `third_party/Microsoft-dotnet`.

Until these items are completed, the source repository can be published, but the bundled setup and portable executables should be treated as not yet cleared for public redistribution.
