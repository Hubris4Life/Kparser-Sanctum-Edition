# Privacy and memory access

## What the application accesses

KParser inspects a locally running XiLoader/POL/FFXI process to locate chat-log and supported player-state structures. This is necessary because not every combat effect is exposed through a separate public API.

Memory access is local and read-oriented. The modern party-summary feature does not write chat directly into process memory; when explicitly requested, it activates the game window and sends keyboard input. Windows may block that action, in which case the summary is copied for manual use.

## What is stored

The application may store:

- Parsed combat and non-combat messages
- Character and combatant names appearing in those messages
- Fight history and calculated statistics
- Item-drop, chat, crafting, EXP, and HELM records
- Saved build-comparison snapshots
- User-entered player labels and notes
- Application preferences

Runtime files are stored below the current user's Local Application Data folder. Exports are saved only where the user chooses.

## Local communication

The dashboard communicates with its engine through a named pipe restricted to the current Windows user. The pipe is not intended to accept remote connections.

The inherited engine previously contained an unfinished ZeroMQ packet-reader path. Sanctum Edition removes that reader and its native networking libraries. Normal Sanctum parsing uses direct local-process memory access and does not open the historical ZeroMQ connection.

## External communication

KParser checks the project's public GitHub Releases API for updates when it starts. This check can be disabled under **Options > Check for updates on startup**, and a manual check remains available under **Help > Check for Updates**. Update packages are downloaded only after the user chooses to install one. GitHub receives the normal connection information associated with an HTTPS request, such as the user's IP address and the KParser user-agent string.

No dedicated analytics or telemetry client is included. The inherited ParserCore source retains a legacy Google AJAX translation compatibility helper, but its historical embedded key has been removed and the no-key path returns without making a request. Maintainers should remove the obsolete helper entirely if no compatibility consumer remains.

## Sharing diagnostics

Parse databases, CSV exports, screenshots, and memory reports can contain character names, linkshell chat, and other player information. Sanitize them before attaching them to a public issue. Never upload account credentials, server credentials, complete process dumps, or another player's private messages without permission.

The **Tools > Diagnostics** report shortens paths below the current Windows user profile before copying them. It intentionally retains character names and local process IDs when available because they can be necessary to diagnose player registration and client selection. Application error logs remain local unless the user chooses to share them and may contain exception messages or paths supplied by Windows and third-party components.

## Game and server rules

Process-memory readers and automated input can be restricted by a game's terms or a server's rules. Users are responsible for confirming that their intended use is permitted in their jurisdiction and on the server they use.
