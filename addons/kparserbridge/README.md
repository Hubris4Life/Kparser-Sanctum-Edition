# KParserBridge

KParserBridge is an optional Ashita v4 addon for KParser's **Other** server
profile. It observes incoming entity and party updates, reads Ashita's current
party-to-pet relationship, and writes a local mapping file. KParser uses that
file to attribute pet damage to an owner while retaining the Pets filter.

It does not alter chat text or nameplates, send game commands, inject outgoing
packets, or communicate over the network.

Load it with `/addon load kparserbridge`. Available commands are
`/kparserbridge status`, `rescan`, `mappings`, `on`, and `off`.

The mapping file is `data/pet_mappings.tsv`. If the same visible pet name maps
to more than one owner, KParser treats it as ambiguous and leaves that pet as a
separate combatant rather than risking incorrect attribution.
