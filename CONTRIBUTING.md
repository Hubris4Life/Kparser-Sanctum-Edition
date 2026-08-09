# Contributing

Thank you for helping improve KParser - Sanctum Edition.

## Before opening a change

1. Search existing issues for the same behavior.
2. Reproduce parser defects using a sanitized log, a reproducible test encounter, or a narrowly described client state.
3. Separate observed log damage from calculated estimates such as DoT damage.
4. Do not submit Square Enix game files, private credentials, player-identifying parse databases, or server secrets.

## Development expectations

- Build the affected modern and/or legacy projects.
- Test memory discovery with the supported Sanctum client build.
- Verify report totals against the original chat log and known combat actions.
- Check that running totals, individual fights, and current-fight views agree.
- Confirm the live monitor does not duplicate events.
- Test both dark and light themes when changing visual controls.
- Keep the 32-bit legacy engine separate from the 64-bit interface.
- Preserve original copyright and license notices in inherited files.
- Add a dated modification notice to materially changed inherited files.
- Update CHANGELOG.md when behavior visible to users changes.

## Pull requests

Keep each pull request focused. Explain the user-visible problem, the chosen fix, how it was tested, and any remaining uncertainty. Include screenshots for visual changes and sanitized sample data for parser changes when practical.

By contributing, you agree that your contribution may be distributed under GPL-2.0-or-later with the rest of this project.
