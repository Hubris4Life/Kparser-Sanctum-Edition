# KParser - Sanctum Edition Preview 26

Preview 26 focuses on owner-aware pet reporting, physical critical-rate visibility, calculated DoT completeness, a more useful live overlay, and stability diagnostics. It retains Preview 25's complete report and update foundation while hardening the damage timeline and application error handling ahead of wider stability testing.

## Downloads

- **Setup** installs the application for the current Windows user and adds Start Menu shortcuts.
- **Portable ZIP** contains a self-contained executable and the required notices. Extract the ZIP before running it.
- **Portable 7z** contains the identical payload with stronger compression for a smaller download. Extract it with Windows 11's archive support or 7-Zip.
- **KParserBridge addon ZIP** is the optional standalone Ashita v4 addon for users of the Other profile.

Both packages contain the same parser engine and report features.

KParserBridge is included in both packages but is not installed or loaded automatically. SanctumChat is maintained separately by Sanctum and is not bundled with KParser.

## Highlights

- Automatic FFXI chat-memory detection for Sanctum/XiLoader
- Modern dashboard and compact live monitor
- Minimal always-on-top True Overlay with damage, share, accuracy, and critical rate
- Running totals and individual-fight filtering
- Damage, defense, healing, buffs, debuffs, EXP, HELM, crafting, chat, item-drop, and consumable-use reports
- Interactive interval and cumulative damage timeline with hover details
- Weapon-skill pacing and TP-cycle proxy statistics, including optional legacy TP-return echoes
- Expanded multi-attack round details, extra attacks, Zanshin candidates, and retaliation counts
- Accuracy, share, Corsair roll, DoT estimation, and build-comparison reports
- Owner-attributed pet totals by default with an optional separate pet row that identifies its master
- Pet-initiated encounter tracking before the owner deals direct damage
- Successful zero-damage Dia, Diaga, and Bio applications in calculated DoT and Magic reports
- Main-page character registration and DoT stat capture without starting a parse
- CSV export, party-summary support, player information, and dark/light themes
- Automatic update checks at startup, manual checks under Help, and an optional preview-release channel
- Separate patch-note pages when multiple versions are available
- SHA-256-verified Setup and Portable update application with portable rollback protection
- Server > Sanctum XI / Other with persistent, engine-level rule isolation
- Clean parse boundary whenever the active server profile changes
- Sanctum-only pet naming, calculated DoTs, and DoT stat capture limited to Sanctum XI
- Optional passive KParserBridge pet-owner mapping for Other servers
- Separate player-only and pet-only damage filters for reviewing each contribution
- Tools > KParserBridge Addon with Ashita detection, manual selection, safe updates, and recoverable removal
- Tools > Diagnostics with redacted support-report copying and direct access to local error logs
- Hardened timeline rendering with dark/light theme regression coverage
- XiLoader, Horizon Loader, PlayOnline, and FFXIMain client-window recognition

## Requirements and limitations

- Windows 10 version 1809 or later, x64
- Final Fantasy XI must be running before live-memory parsing begins
- The parser engine remains 32-bit internally for FFXI memory and SQL Server Compact compatibility
- DoT totals are estimates where the game chat log does not expose individual damage ticks
- Other-server combat logs may differ and can still require a dedicated message adapter
- KParserBridge requires Ashita v4; ambiguous visible pet names remain separate instead of being attributed unsafely
- This preview is unsigned, so Windows SmartScreen may display an unknown-publisher warning
- Update checks contact only the project's public GitHub Releases API and do not send parse data or analytics
- Uninstalling the application does not automatically remove parse data stored under the current user's local application-data folder

## Privacy

KParser reads the locally running FFXI process and stores parser data on the same computer. The project does not include dedicated analytics or telemetry. See the repository privacy document for details.

## Source and license

KParser - Sanctum Edition is distributed under GNU GPL version 2 or later. It is based on KParser and preserves the original project notices. Matching source and modification history are available at:

https://github.com/Hubris4Life/Kparser-Sanctum-Edition/releases/tag/v0.26.0-preview
