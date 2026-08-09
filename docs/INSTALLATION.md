# Installation and use

## Supported environment

KParser - Sanctum Edition is a Windows application intended for the Sanctum-supported XiLoader/FFXI client environment. Other private servers and retail FFXI may use different processes, chat layouts, offsets, and message rules.

## Setup edition

1. Download the setup executable from a tagged GitHub release.
2. Verify the published checksum when one is supplied.
3. Run the installer and choose whether to create a desktop shortcut.
4. Start XiLoader/FFXI and fully log in a character.
5. Start KParser at the same Windows privilege level as the game.
6. Confirm that the parser status becomes active and that memory detection identifies the client.

The setup edition installs for the current Windows user and creates a normal uninstaller.

## Portable edition

1. Download the portable executable from the same tagged release.
2. Place it in a user-writable folder.
3. Run it directly.

The portable application extracts its matching engine below:

    %LOCALAPPDATA%\KParser Sanctum Modern\PortableEngine

Do not replace extracted engine files with files from another release.

## Basic operation

- Start begins accepting and committing new parser data.
- Stop pauses new parsing without deleting the current results.
- Reset archives the active parse and starts a fresh database.
- Detect Memory scans the fully logged-in client when automatic detection is unavailable.
- Live Monitor opens the overlay-style running or current-fight display.

Fight history excludes monsters for which neither the player nor a party member recorded damage.

## Local data

KParser stores settings, databases, archives, and saved player builds under:

    %LOCALAPPDATA%\KParser Sanctum Modern

Uninstalling the application may intentionally leave user data in that location. Back up that folder before deleting it if saved comparisons or parses are important.

## Troubleshooting memory detection

- Make sure a character is fully logged in.
- Run KParser and XiLoader/FFXI at the same privilege level.
- Close obsolete ChatlogMemloc utilities; Sanctum Edition performs its own detection.
- Retry Detect Memory after zoning if the client was still loading.
- Confirm that the game process is named xiloader, pol, or ffximain as expected by the installed version.
- If the client was updated, report the exact release and process information without publishing a memory dump.

## Party-chat summaries

The party-summary action activates the game window and sends Unicode keyboard input only after the user presses the button. If Windows prevents activation, the text is copied for manual pasting instead. Review the text before sending and follow your server's chat rules.
