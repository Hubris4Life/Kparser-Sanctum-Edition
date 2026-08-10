# KParser - Sanctum Edition Preview 23

Preview 23 adds SanctumChat-aware pet ownership to KParser - Sanctum Edition and provides an opt-in Ashita v4 addon installer. It retains the modern Windows dashboard, XiLoader-compatible memory detection, and Preview 22 reporting features.

## Downloads

- **Setup** installs the application for the current Windows user and adds Start Menu shortcuts.
- **Portable ZIP** contains a self-contained executable and the required notices. Extract the ZIP before running it.
- **Portable 7z** contains the identical payload with stronger compression for a smaller download. Extract it with Windows 11's archive support or 7-Zip.
- **SanctumChat addon ZIP** is the optional standalone Ashita v4 addon for users who prefer to install it manually.

Both packages contain the same parser engine and report features.

The matching SanctumChat addon is included in both packages. It is not installed or loaded automatically.

## Highlights

- Automatic FFXI chat-memory detection for Sanctum/XiLoader
- Modern dashboard and compact live monitor
- Running totals and individual-fight filtering
- Damage, defense, healing, buffs, debuffs, EXP, HELM, crafting, chat, and item-drop reports
- Accuracy, share, multi-attack, Corsair roll, DoT estimation, and build-comparison reports
- CSV export, party-summary support, player information, and dark/light themes
- SanctumChat `Owner's Pet` parsing with pet damage attributed to its player by default
- Separate player-only and pet-only damage filters for reviewing each contribution
- Tools > SanctumChat Addon with Ashita detection, manual selection, safe updates, and recoverable removal
- Full chat pet names plus nickname-preserving abbreviations for long in-game nameplates

## Requirements and limitations

- Windows 10 version 1809 or later, x64
- Final Fantasy XI must be running before live-memory parsing begins
- The parser engine remains 32-bit internally for FFXI memory and SQL Server Compact compatibility
- DoT totals are estimates where the game chat log does not expose individual damage ticks
- SanctumChat is optional, requires Ashita v4 and compatible Sanctum server support, and must be loaded by the player
- The experimental 25-character nameplate display should be tested with each supported client update
- This preview is unsigned, so Windows SmartScreen may display an unknown-publisher warning
- Uninstalling the application does not automatically remove parse data stored under the current user's local application-data folder

## Privacy

KParser reads the locally running FFXI process and stores parser data on the same computer. The project does not include dedicated analytics or telemetry. See the repository privacy document for details.

## Source and license

KParser - Sanctum Edition is distributed under GNU GPL version 2 or later. It is based on KParser and preserves the original project notices. Matching source and modification history are available at:

https://github.com/Hubris4Life/Kparser-Sanctum-Edition/releases/tag/v0.23.0-preview
