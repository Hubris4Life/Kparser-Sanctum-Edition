# Security policy

## Supported versions

Only the latest published Sanctum Edition release receives security fixes. Preview builds may change without compatibility guarantees.

## Reporting a vulnerability

Do not open a public issue for a vulnerability that could expose player data, execute commands, cross the named-pipe security boundary, or access the wrong process. Use GitHub's private vulnerability-reporting feature after it is enabled for the repository.

If private reporting has not been enabled, contact the repository owner through a private channel listed on the owner's GitHub profile. Do not include real parse databases, memory dumps, access tokens, or private server credentials unless a secure exchange method has been agreed upon.

## Security-sensitive areas

- Process discovery, memory reading, and player-stat capture
- Named-pipe access control and message-size limits
- Engine extraction and executable launch paths
- CSV and parse-database file handling
- Party-chat keyboard injection
- Legacy Google translation requests
- Release packaging and third-party dependency integrity

Downloaded binaries should be obtained only from this repository's release page and should be matched to the published release checksum when checksums are provided.
