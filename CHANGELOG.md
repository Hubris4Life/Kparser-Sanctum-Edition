# Changelog

All notable public changes should be documented here. Preview entries describe development milestones and are not promises of complete compatibility.

## Unreleased

## Preview 24 - 0.24.0 - 2026

### Added

- Added persistent **Server > Sanctum XI / Other** compatibility profiles.
- Added optional passive KParserBridge Ashita v4 pet-owner mapping for Other servers.
- Added profile identity and supported-feature metadata to the local engine bridge.

### Safety and compatibility

- Profile changes archive the current parse and begin a clean session so incompatible rules cannot mix.
- Other mode disables Sanctum-only pet-name decoding, calculated DoT reports, and player-stat DoT capture.
- Ambiguous generic pet mappings are never merged into a player total.
- Fresh installations default to Other; existing Preview 23 settings migrate to Sanctum XI to preserve behavior.
- Added Horizon Loader recognition alongside XiLoader, PlayOnline, and FFXIMain.

## Preview 23 - 0.23.0 - 2026

### Added

- Added Sanctum server pet-owner names, owner-attributed damage totals, and separate direct-player and pet-only damage filters.
- Added the optional Ashita v4 `sanctumchat` addon prototype, authoritative server pet mappings, and `Owner's Pet` parser support.
- Added Tools > SanctumChat Addon for opt-in Ashita v4 detection, installation, updating, and recoverable removal.
- Bundled the matching SanctumChat addon in both setup and portable release packages.

### Refined

- Expanded SanctumChat pet nameplates locally, matched readable possessive names between nameplates and combat chat, and restored spaces in CamelCase pet names.
- Removed SanctumChat's 30-second polling, made registration event-driven, and discarded stale aliases when a pet entity is reused.
- Increased readable pet nameplates to 27 characters, added nickname-preserving abbreviations for longer combinations, and restored full pet names in combat chat.
- Restored the tested-safe 23-character nameplate limit and replaced raw truncation with conventional initials while retaining full chat names.
- Raised the experimental nameplate limit to 25 characters and changed overflow formatting to descriptor initials plus the complete Beastmaster pet nickname.
- Added automated coverage for pet parsing, owner attribution, addon location detection, safe updates, and recoverable removal.

## Preview 22 - 0.22.0 - 2026

### Added

- Multi-attack analysis
- Offensive buff-performance reports
- Defensive buff-performance reports
- Expanded healing and status-removal data
- Dedicated experience report with chain and fight details
- HELM activity tracking
- Corsair roll statistics
- Options > Player Information with persistent job labels and notes

### Retained and refined

- Crafting, chat, item-drop, export, theme, DoT, live-monitor, and saved-build features
- Compact live monitor and one-second live refresh
- Player snapshots and side-by-side build comparisons
- Repository documentation, licensing, privacy, security, and release guidance prepared for public source distribution
- Removed the unused inherited ZeroMQ packet-reader dependency from public builds

## Development previews 1-21 - 2026

Development previews introduced the modern WPF dashboard, local engine bridge, automated engine lifecycle, memory detection, fight filtering, compact monitor, transparency, report tabs, detailed action breakdowns, performance improvements, exports, build comparisons, calculated DoT reports, chat and drop tracking, crafting reports, and light/dark themes.

Earlier per-preview text files were working build notes. Future public releases should use this changelog and signed or immutable Git tags as the authoritative history.
