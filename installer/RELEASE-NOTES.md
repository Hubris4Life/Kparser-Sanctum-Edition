# KParser - Sanctum Edition Preview 22

This is the first public preview of KParser - Sanctum Edition, a GPL-licensed derivative of KParser with a modern Windows dashboard and integrated XiLoader-compatible memory detection.

## Downloads

- **Setup** installs the application for the current Windows user and adds Start Menu shortcuts.
- **Portable** contains a self-contained executable and the required notices. Extract the ZIP before running it.

Both packages contain the same parser engine and report features.

## Highlights

- Automatic FFXI chat-memory detection for Sanctum/XiLoader
- Modern dashboard and compact live monitor
- Running totals and individual-fight filtering
- Damage, defense, healing, buffs, debuffs, EXP, HELM, crafting, chat, and item-drop reports
- Accuracy, share, multi-attack, Corsair roll, DoT estimation, and build-comparison reports
- CSV export, party-summary support, player information, and dark/light themes

## Requirements and limitations

- Windows 10 version 1809 or later, x64
- Final Fantasy XI must be running before live-memory parsing begins
- The parser engine remains 32-bit internally for FFXI memory and SQL Server Compact compatibility
- DoT totals are estimates where the game chat log does not expose individual damage ticks
- This preview is unsigned, so Windows SmartScreen may display an unknown-publisher warning
- Uninstalling the application does not automatically remove parse data stored under the current user's local application-data folder

## Privacy

KParser reads the locally running FFXI process and stores parser data on the same computer. The project does not include dedicated analytics or telemetry. See the repository privacy document for details.

## Source and license

KParser - Sanctum Edition is distributed under GNU GPL version 2 or later. It is based on KParser and preserves the original project notices. Matching source and modification history are available at:

https://github.com/Hubris4Life/Kparser-Sanctum-Edition/releases/tag/v0.22.0-preview
