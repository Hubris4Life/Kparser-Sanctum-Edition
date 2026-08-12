# Changelog

All notable public changes should be documented here. Preview entries describe development milestones and are not promises of complete compatibility.

## Unreleased

## Preview 27 - 0.27.0 - 2026-08-12

### Added

- Added a transparency slider directly to the True Overlay toolbar.
- Added **Options > Overlay Customizations** with persistent text size, bold text, player-name color, statistic color, live preview, and default restoration. True Overlay player names and statistics are bold by default.
- Added a dedicated **Server > Horizon** profile. It enables standard LSB-style calculated DoTs and player-stat capture while excluding Sanctum-only weapon-effect rules.
- Added **Damage Dealt > Critical hits**, showing each combatant's total critical damage, critical-damage share, critical-hit count, highest, lowest, average, and critical rate from eligible melee/ranged hits.

### Compatibility

- Horizon recognizes standard pet types and can use optional KParserBridge owner mappings. Generic or ambiguous pet names are never guessed and remain unattributed/separate when no reliable mapping exists.

### Fixed

- Player Information now lists only players from a validated, logged-in live parser session, rejects action-group rows such as Bio II as job labels, and preserves saved overrides without displaying stale offline players.
- Application startup now archives the previous parser session, creates a clean database, and leaves parsing stopped before Start becomes available, preventing stale data from reappearing on first use.

## Preview 26 - 0.26.0 - 2026

### Added

- Added a persistent **Options > Display Pet Damage Separately** toggle. Pet damage remains attributed to the master by default; separate rows identify the owning player when the mapping is available.
- Added a persistent, always-on-top **True Overlay** live-monitor mode showing only player, damage, share, accuracy, and critical-hit rate, with a minimal drag strip and controls to return to the full monitor or close it.
- Added visible critical-hit-rate columns to the live monitor and the main physical-damage reports, including damage dealt, damage taken, and player performance views.
- Added **Tools > Diagnostics** with live dashboard/engine state, detected game clients, memory status, compatibility and pet-attribution state, registered DoT player, redacted support-report copying, and direct access to local logs.
- Added application-wide diagnostic logging and guarded recovery for ordinary WPF and background-task failures while leaving fatal runtime failures untouched.
- Added a dark/light WPF rendering smoke test for the damage timeline using live dynamic theme resources.

### Changed

- Simplified the combatant scope list to **Alliance**, **Party**, and **Self** on the main report and live monitor.
- Renamed the DoT stat-capture action to **Register Player Stats** and the build snapshot action to **Save Parse** for clearer wording throughout the main window and live monitor.
- Changed the **Live Monitor** action into a toggle: selecting it while the monitor is already open now closes the monitor.
- Extended live-monitor text and CSV exports with critical-hit rate.

### Fixed

- Owned-pet damage now qualifies a fight immediately, so a pet can open the tracked encounter before its master deals direct damage.
- Added provisional owner attribution so a fully identified pet can contribute to its master's row before the master's first combat action creates a player row.
- Corrected multi-attack and defensive-buff report rows to retain their physical hit and critical-hit metrics while preserving their existing inferred-rate calculations.
- Successful zero-damage Dia, Diaga, and Bio applications now register in calculated DoT totals and the Magic damage views instead of being discarded for lacking direct damage.
- Hardened the damage timeline against collection changes, invalid render dimensions, negative/corrupt values, numeric overflow, and renderer exceptions so a graph failure displays a temporary fallback instead of terminating KParser; throttled failure details are retained in the local timeline diagnostic log.
- Prevented the damage timeline from freezing pens and brushes backed by live theme resources, which previously caused an unhandled `This Freezable cannot be frozen` exception when the graph opened.
- Guarded asynchronous dashboard startup and shutdown so engine initialization or cleanup errors cannot bypass normal UI reporting and close handling.
- Extended regression coverage for rejected zero-damage Dia/Bio casts, pet-display scope boundaries, combined and separate pet physical rates, and critical-rate denominators and export surfaces.

## Preview 25 - 0.25.0 - 2026

### Added

- Added startup and manual GitHub update checks with opt-in preview-channel support.
- Added separate patch-note pages for every release between the installed and available versions.
- Added verified Setup and Portable update flows with checksum validation and portable rollback protection.
- Added an interactive interval and cumulative damage timeline with bounded adaptive time buckets.
- Added weapon-skill pacing and TP-cycle analysis with attack-feed statistics and optional legacy TP-return echoes.
- Added consumable item-use reporting with player filtering, searching, timestamps, fight counts, and target counts.
- Added main-page DoT stat capture that works while parsing is stopped, remembers the local character, and safely confirms the character before reading stats.

### Refined

- Expanded inferred multi-attack reports with total attacks, extra attacks, Zanshin candidates, retaliation counts, attacks per round, and complete round distributions.
- Renamed Item Drops to Items & Loot so drop reports, HELM, and consumable usage share one report family.
- Preserved the Preview 24 Sanctum XI / Other compatibility boundary across every new report.

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
