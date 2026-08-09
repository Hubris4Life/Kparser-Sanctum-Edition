# Architecture

## Process boundary

    FFXI client / XiLoader
            |
            | local process discovery and chat-memory reading
            v
    Modified KParser engine (x86, .NET Framework 3.5)
            |
            | current-user named pipe, protocol version 1
            v
    Sanctum dashboard (x64, .NET 10 WPF)

The legacy process remains responsible for memory detection, chat parsing, SQL Server Compact compatibility, and aggregation of raw events. The modern interface receives immutable report snapshots. This keeps UI rendering from blocking chat capture and avoids loading 32-bit SQL Server Compact into the 64-bit interface.

## Engine lifecycle

The modern interface starts its bundled engine without displaying the legacy window. It supplies its process ID so the engine can exit if its owning interface terminates unexpectedly. A normal application shutdown asks the engine to close cleanly.

Portable builds extract a versioned engine payload under:

    %LOCALAPPDATA%\KParser Sanctum Modern\PortableEngine

Installed builds keep the engine beside the dashboard in the application directory.

## Bridge contract

The named pipe is:

    KParser.Sanctum.Modern.v1

Its access list is restricted to the Windows user that launched KParser. Requests are newline-delimited JSON, limited to 4 KiB; responses are limited to 1 MiB.

Snapshot requests select a report, combatant scope, display mode, and fight scope. Fight scopes include running total, current fight, exact battle ID, and per-monster aggregate. The engine advertises recent eligible fights and column metadata alongside report rows.

The bridge accepts only these state-changing commands:

- start
- stop
- reset
- detect
- shutdown

It does not provide arbitrary command or code execution.

## Refresh behavior

The compact live monitor requests a lightweight snapshot once per second. The full dashboard normally refreshes less frequently while the monitor is open. Unchanged snapshots are cached and existing rows are updated in place to reduce UI churn.

The parser's safe-message commit interval is separate from its action-join window. The shorter commit interval improves visible responsiveness without intentionally splitting related combat events.

## Storage

Runtime data is stored below:

    %LOCALAPPDATA%\KParser Sanctum Modern

This includes integrated parse databases, reset archives, application settings, player labels, and saved build-comparison snapshots. Exported files are written only to the user-selected destination.

## Trust boundaries

Security-sensitive boundaries are process selection, process-memory access, named-pipe authorization, engine extraction, local database loading, export paths, and requested party-chat keyboard input. Changes in these areas require focused review and testing.
