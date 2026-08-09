# Publishing this folder on GitHub

This folder is staged as a clean source repository. It intentionally has no Git remote and contains no generated installer payloads or parse data.

## Choose the repository relationship

The source was based on https://github.com/poroburu/kparser. A GitHub fork gives the clearest visible lineage, but a new repository is also workable if README.md, NOTICE.md, LICENSE, and the source history information remain intact.

If creating a new repository, a descriptive name is:

    KParser-Sanctum

Use a description similar to:

    GPL-licensed Sanctum-compatible derivative of KParser with a modern WPF dashboard and integrated memory detection.

Do not describe the entire application as merely inspired by KParser.

## Before the first push

1. Choose the public maintainer name or organization to use for new-code copyright notices.
2. Review every file in MODIFICATIONS.md and add dated notices to materially modified inherited files.
3. Confirm that no generated executables, databases, memory reports, logs, credentials, or personal paths are staged.
4. Review THIRD-PARTY-NOTICES.md and resolve its release blockers before uploading installers.
5. Build from this folder and verify the instructions in BUILDING.md on a clean environment.

## Suggested GitHub settings

- Enable Issues and private vulnerability reporting.
- Disable blank issues so the privacy-aware templates are used.
- Protect the default branch and require pull-request review when multiple maintainers are present.
- Enable dependency and secret scanning where available.
- Add topics such as ffxi, parser, combat-parser, wpf, and gpl.
- Do not enable Git LFS unless a legitimate source asset exceeds GitHub's normal file limit.

## First release

Publishing the source repository does not automatically clear every bundled dependency for binary redistribution. The current SQL Server Compact, Visual C++ runtime, ZedGraph, clrzmq, libzmq, and .NET notices must be completed as described in THIRD-PARTY-NOTICES.md before attaching the setup or portable executable.

When those items are complete, follow RELEASING.md and attach the binaries to a versioned GitHub release rather than committing them to the default branch.
