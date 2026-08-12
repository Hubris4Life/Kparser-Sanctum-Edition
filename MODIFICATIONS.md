# Sanctum Edition modification notice

This file provides a prominent project-level record that KParser has been modified. Source-control history should record the author and date of each later change. In inherited files, preserve existing copyright and license headers and add a short changed-by notice when making further material edits.

## 2026 Sanctum changes

The Sanctum Edition work includes:

- Reworked process discovery and memory access for Sanctum's XiLoader/FFXI environment
- Automatic chat-log signature scanning and one-click memory detection
- A headless legacy-engine mode owned by the modern interface
- A current-user-only named-pipe bridge with a bounded report and command protocol
- Faster safe message commits while preserving the parser's action-join window
- Integrated parse storage, reset archiving, report filtering, and export support
- A new .NET 10 WPF dashboard and compact live monitor
- Damage, defense, healing, status, buff, experience, crafting, HELM, chat, drop, item-use, Corsair roll, multi-attack, damage-timeline, WS/TP-cycle, and player-build reports
- Server-rule DoT estimation and player-stat capture support
- Sanctum server pet-owner name decoding with combined owner totals and separate pet-only filtering
- Optional SanctumChat/Ashita pet-name expansion using authoritative server mappings
- SanctumChat pet nameplate expansion and readable spacing for multiword pet names
- Opt-in SanctumChat detection, installation, updating, and recoverable removal from the modern Tools menu
- Selectable Sanctum XI, Horizon, and Other compatibility profiles with clean parse boundaries
- Horizon standard LSB-style DoT calculations with Sanctum-only weapon effects excluded
- Conservative Horizon pet handling with optional unambiguous KParserBridge owner mappings
- Conservative Other-server rules and optional passive KParserBridge pet-owner mapping
- Installer and portable single-file packaging
- GitHub release update checks, per-release patch notes, package verification, and safe Setup/Portable update application
- Disabled the obsolete inherited Google AJAX translation key and no-key network path
- Removed the unfinished inherited ZeroMQ PacketReader and migrated old Packet preferences to the supported RAM reader

## Inherited engine files changed for Sanctum

The initial Sanctum work modified or added files in these areas:

- FFXILogParser project configuration, application configuration, options, main window, resources, assembly metadata, and theme support
- ParserCore database management, process access, RAM reading, message timing, settings, configuration, and assembly metadata
- ParserCore Bridge directory
- ParserCore Monitors/RamReader/ChatLogSignatureScanner.cs

Use repository history for the exact line-level changes. This summary must not be used as a substitute for preserving the original notices or publishing the complete corresponding source.
