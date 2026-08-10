# Release process

## Release gate

Do not publish a setup or portable executable until every item below is complete.

- Modern and legacy sources are committed with no untracked release changes.
- Version numbers agree in the project file, installer, README, changelog, and tag.
- Legacy inherited files contain appropriate modification notices.
- The third-party dependency inventory has been verified for the exact binaries.
- Required third-party licenses and notices are included in the payload.
- The obsolete translation helper has been removed, disabled, or clearly disclosed.
- No parse database, memory report, credentials, API tokens, personal paths, or player-private data is present.
- Both install types have been tested on a clean supported Windows account.

## Versioning

Preview 23 currently uses:

    Product version: 0.23.0
    Display version: Preview 23

For stable public releases, prefer semantic tags such as v1.0.0. Every binary asset must identify the same version.

## Build

1. Create and check out the release tag candidate.
2. Follow BUILDING.md to compile the x86 engine.
3. Run the relevant smoke and manual parser tests.
4. Stage the engine and runtime files under installer/payload/current/Engine.
5. Publish the self-contained x64 dashboard.
6. Build the setup and portable editions from that same engine payload.
7. Install, launch, detect memory, parse a controlled fight, export a report, reset, and uninstall.

## Release contents

A GitHub release should include:

- Setup executable
- Portable ZIP, when offered
- Compact portable 7z archive, when offered
- Standalone versioned SanctumChat ZIP
- SHA-256 checksum file
- Release notes and known limitations
- Direct link to the matching source tag
- GPL license and third-party notices within each distribution

The ZIP and 7z portable assets must contain the same executable and notices.
The dashboard must be published with native libraries embedded; an EXE that
depends on WPF DLLs left beside the publish directory is not a complete
single-file release.

Do not attach a parse database or generated EnginePayload.zip as a source archive.

## Verification

At minimum, verify:

- Start, stop, reset, shutdown, and automatic detection
- No doubled melee or magic events
- Running totals and individual fight filters
- Live monitor one-second refresh and compact mode
- Damage, defense, healing, buff, debuff, EXP, HELM, crafting, chat, and drop reports
- Player jobs, accuracy, share, multi-attacks, rolls, DoTs, and build comparisons
- SanctumChat installation/update/removal, pet-owner attribution, and player-only/pet-only damage filters
- Dark and light themes
- CSV export and party-summary fallback
- Data cleanup on a fresh application launch

Record the exact Windows, XiLoader, and client build used for verification.

## Checksums

Generate SHA-256 hashes after the final binaries are built and do not modify the assets afterward. Publish the hashes as a plain text release asset and repeat them in the release notes.

## Rollback

Do not replace files attached to an existing version tag. If a packaging defect is found, withdraw the affected binary, document the reason, correct the source or packaging, and publish a new version.
