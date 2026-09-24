# repo-standard: the first declaration for Hafeok/Varve

| | |
|---|---|
| **Issue** | [#23](https://github.com/Hafeok/Varve/issues/23) |
| **Date** | 2026-09-24 |
| **Tool** | Claude Code (local CLI) |
| **Model** | Claude Fable 5.1 (`claude-fable-5-1`) |
| **Branch** | `claude/epic-feynman-6deidm` |
| **ADR** | [0039](../adr/0039-repo-standard.md) |

Contemporaneous. Follows the "before the first apply" section of the
[2026-09-23 record](2026-09-23-issue-23-repo-standard.md).

## The prompt

> I'm trying to run the first export and check from a local machine, but it
> isn't working, something about the SDK. Then: help me with the steps before
> the first apply.

## The report

**The SDK.** `global.json` pins the 10.0.4xx feature band; the machine had
10.0.103, a lower band, so `dotnet` refused to load. Installed SDK 10.0.401
locally with Microsoft's install script. No change to the repository.

**Export and check** ran clean: `check` printed "No differences".

**The declaration**, `.github/repo-standard.yaml`, is the export with two
edits:

- `projects` removed, with a comment saying why: both boards are owned by a
  user account, which an App's installation token cannot reach.
- `secrets` lists `REPO_STANDARD_APP_CLIENT_ID` and
  `REPO_STANDARD_APP_PRIVATE_KEY`, added by the maintainer after creating and
  installing the App, so the first `check` does not report them as drift.

`check` printed "No differences" after each edit.

**Review of the bypass lists**, ids resolved through the API: RepositoryRole 5
is Admin; Integration 1236702 is the Claude GitHub App (ADR 0034's sandbox
exception); User 2194785 is the maintainer. Discussions are off, so no
categories were exported. Left as the current state, for the maintainer to
decide before merging: no required status checks on `trunk`, every security
option off, all three merge methods allowed, empty description and topics.

**Committed on the branch, not main**, so no apply is triggered; a
`workflow_dispatch` `plan` against the branch proves the App's token and
permissions before the file reaches main.
