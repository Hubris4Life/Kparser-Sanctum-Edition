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

The inherited engine also contains an optional ZeroMQ packet-reader path that connects only to localhost port 43350. Normal Sanctum memory parsing does not require a public network listener.

## External communication

No dedicated analytics or telemetry client was identified in the current source review. The inherited ParserCore source retains a legacy Google AJAX translation compatibility helper, but its historical embedded key has been removed and the no-key path returns without making a request. Maintainers should remove the obsolete helper entirely if no compatibility consumer remains.

## Sharing diagnostics

Parse databases, CSV exports, screenshots, and memory reports can contain character names, linkshell chat, and other player information. Sanitize them before attaching them to a public issue. Never upload account credentials, server credentials, complete process dumps, or another player's private messages without permission.

## Game and server rules

Process-memory readers and automated input can be restricted by a game's terms or a server's rules. Users are responsible for confirming that their intended use is permitted in their jurisdiction and on the server they use.
