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
- Release requirement: Include the applicable license notice and provide the corresponding ZedGraph source or a compliant source-access method for the exact binary distributed.

## clrzmq and historical libzmq

- clrzmq package version observed: 2.2.5
- Package author/owner metadata: zeromq
- Local package license: GNU Lesser General Public License version 3
- Preserved license: src/legacy-engine/packages/clrzmq.2.2.5/LICENSE
- Historical project: http://www.zeromq.org/bindings:clr
- Release requirement: Confirm the exact libzmq binary version and its historical license; distribute the relevant license, notices, and source-access materials. Current ZeroMQ licensing must not be substituted automatically for an older DLL's license.

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

## Release blockers to resolve

Before the first public binary release:

1. Add the exact ZedGraph license text and matching source-access material to the release package.
2. Identify the exact native libzmq version contained in the clrzmq 2.2.5 package and archive its matching source and license.
3. Add Microsoft SQL Server Compact redistribution terms or replace the dependency with a distribution method whose terms have been verified.
4. Add the Visual C++ 2008 redistribution notice required for the shipped runtime.
5. Copy the .NET runtime's generated license and third-party notice files into both distribution formats.

Until these items are completed, the source repository can be published, but the bundled setup and portable executables should be treated as not yet cleared for public redistribution.
