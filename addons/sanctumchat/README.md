# SanctumChat

`sanctumchat` is an Ashita v4 addon for Sanctum. It expands the server's short
pet aliases into authoritative owner and pet names before the combat message
is added to the FFXI chat log, and applies the same readable form to the pet's
in-game nameplate.

The original FFXI spawn packet only carries a 15-character name. SanctumChat
uses an experimental 25-character client nameplate limit. Combinations that
exceed it retain the owner's possessive prefix and abbreviate Beastmaster-style
descriptors while preserving the pet's final name, such as `Nazgul's L. Lars`
or `Nazgul's C. Chucky`. Chat and KParser still receive the complete,
unabbreviated name.

For example:

    Garuda@Nazgul hits Shinryu for 300 points of damage.

becomes:

    Nazgul's Garuda hits Shinryu for 300 points of damage.

The server supplies the relationship. The addon does not guess ownership from
the visible pet name.

SanctumChat registers once after loading and once after zoning. The server
pushes new mappings when pets spawn; no recurring chat command is sent. Use
`/sanctumchat sync` only when a manual refresh is needed.

## Install and load

1. Copy the entire `sanctumchat` folder into Ashita v4's `addons` folder.
2. In game, run `/addon load sanctumchat`.
3. Run `/sanctumchat status` to confirm that it says `registered`.
4. Summon a pet or run `/sanctumchat sync` while an alliance pet is active.
5. Run `/sanctumchat mappings` to inspect the relationship learned from the
   server.

Add `/addon load sanctumchat` to the appropriate Ashita boot script after the
prototype has been validated.

## Commands

- `/sanctumchat status`
- `/sanctumchat sync`
- `/sanctumchat mappings`
- `/sanctumchat on`
- `/sanctumchat off`

## Prototype test objective

This version deliberately does not send mappings directly to KParser. The test
is successful only if KParser's existing memory reader receives the expanded
`Owner's Pet` chat text that Ashita places in the game chat log.
