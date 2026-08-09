# Open-source compliance guide

This is a practical release checklist, not legal advice.

## Project license

The inherited KParser notice permits redistribution and modification under GNU GPL version 2 or, at the recipient's option, any later version. This repository uses the SPDX expression GPL-2.0-or-later to describe that choice.

## Required for source releases

- Keep the complete GPL text in LICENSE.
- Preserve David Smith's 2007-2009 copyright notice.
- Preserve notices inherited from maintenance forks and third parties.
- Identify Sanctum Edition as modified and give the modification date.
- License contributions to the combined work under GPL-2.0-or-later.
- Do not impose additional restrictions that conflict with the GPL.

## Required for executable releases

Provide recipients with the complete corresponding source for the exact executable release. The safest GitHub workflow is:

1. Build only from a clean, immutable version tag.
2. Publish that tag before or at the same time as the executables.
3. Link the release description directly to the tag.
4. Include the source code, project files, interface definitions, and build/packaging scripts actually used.
5. Include the GPL and all applicable third-party notices with the installed and portable distributions.
6. Keep each release and its source available for as long as the binaries are distributed.

GitHub's automatically generated source archives are helpful but should not be the only compliance check. Confirm that vendored or generated inputs needed to build the engine have not been omitted by ignore rules or missing submodules.

## Modified inherited files

GPL version 2 requires modified files to carry prominent notices stating that they were changed and when. MODIFICATIONS.md is the project-level summary. Before the first public release, audit each materially changed inherited source file and add a concise comment such as:

    Modified for KParser - Sanctum Edition, 2026.

Do not remove or replace the original copyright header.

## Separate third-party obligations

GPL licensing does not replace the licenses for SQL Server Compact, the Visual C++ runtime, .NET, or ZedGraph. Complete the verification items in THIRD-PARTY-NOTICES.md before uploading an installer.

## Private use

The GPL does not require a modified version used only privately to be published. Source-distribution duties arise when copies are conveyed to other people. Testers who receive an executable are recipients and must receive, or be offered equivalent access to, its corresponding source under the license.
