# The cloud setup moves to the shared bootstrap

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#155](https://github.com/Hafeok/Varve/issues/155), opened by this session; [#88](https://github.com/Hafeok/Varve/issues/88) referenced, not closed |
| **Date** | 2026-10-26 |
| **Tool** | Claude Code 2.1.310, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01TGqZSSLXttAaWRkXq3caEc` |
| **Branch** | `claude/dreamy-euler-y9aml6`, from `main` at d77fa5e |

## The prompt

> Varve's cloud setup has moved to the shared bootstrap (github.com/Hafeok/claude-code-bootstrap, v3), which every cloud environment now runs. A v3 test session in this repo (2026-10-26) confirmed that the bootstrap covers everything below. Remove Varve's duplicate copy:
>
> 1. Delete .claude/cloud-setup.sh. The bootstrap installs ssh-keygen and the latest .NET 10 SDK (10.0.403), which global.json's latestFeature roll-forward accepts, and writes /etc/profile.d/dotnet.sh with the same DOTNET_ROOT and telemetry settings. Varve's script was downloading an unused 10.0.401 SDK (240 MB, about 19 s) in every session.
> 2. Delete .claude/hooks/cloud-git-identity.sh and remove its SessionStart entry from .claude/settings.json. If that leaves .claude/settings.json empty, delete the file. The bootstrap's user-level git-identity hook does the same thing and also writes allowed_signers. The repo hook only matched "startup", which is also the cause of #88; check whether #88 can be closed with a reference to the bootstrap.
> 3. Look through docs/ and AGENTS.md/CLAUDE.md for references to cloud-setup.sh, cloud-git-identity.sh or the curl one-liner, including docs/traceability/2026-10-09-issue-79-cloud-session-signing.md and ADR 0034. Leave historical records as they are. Where a doc describes current behaviour, update it to say the environment is provisioned by claude-code-bootstrap, and decide whether ADR 0034 needs an amendment note under this repo's ADR conventions.
> 4. Keep .devcontainer/ as it is: local setup isn't part of this change. Keep the sdk_version comment's three-way sync rule only where it still applies (global.json and devcontainer).
>
> Follow this repo's usual process for the change: an issue, a traceability record and the commit conventions. Open a PR.

## The report

### What this session's container showed

This session ran under the bootstrap, at commit d62318b in
`/opt/claude-code-bootstrap`. Its user-level `SessionStart` hook,
`hooks/session-start.sh`, has no matcher. It runs `git-identity.sh`, then
`dotnet-pin.sh`, and then **this repository's `.claude/cloud-setup.sh`** if
the file exists. Because the bootstrap still runs Varve's script, the container
held both SDKs, 10.0.401 and 10.0.403. Inside the checkout, `dotnet --version`
selected 10.0.403, which `global.json`'s `latestFeature` roll-forward allows
(same major, minor and feature band 4xx). `/etc/profile.d/dotnet.sh` held the
same three lines Varve's script writes. The repository's own hook ran as well,
and both hooks reported the same identity and fingerprint at start.

### Removed

- `.claude/cloud-setup.sh`. The bootstrap's cached modules provide everything
  it did. Its `dotnet-pin.sh` also installs exactly what `global.json` pins if
  an installed SDK ever stops satisfying it.
- `.claude/hooks/cloud-git-identity.sh`, and `.claude/settings.json`, whose
  only content was that hook's `SessionStart` entry. Nothing else is left in
  `.claude/`, so the directory goes too.

Nothing in `eng/`, `.github/` or the build reads `.claude/`.

### The documents

No document that describes current behaviour names either file or the
`curl` one-liner, so no current document needed to change:

- `AGENTS.md`, `CLAUDE.md`, `CONTRIBUTING.md`, `GOVERNANCE.md` and the rest
  of `docs/` outside `traceability/` do not mention them.
- The record for #79 (`2026-10-09-issue-79-cloud-session-signing.md`) and the
  record for milestone 7b (`2026-10-08-issue-11-milestone-7b.md`, whose session
  found that the hook could not sign without `ssh-keygen`) are historical, and are left as they are. This
  record is where a reader of #79's record finds out what came next.
- **ADR 0034 gets no amendment.** It never names the hook, the setup script or
  how a session gets its key. Its points are about which commits must be
  signed, the ruleset 2 bypass for the cloud sandbox, and why the platform's
  key is never registered, and moving the hook from this repository to the
  bootstrap changes none of them. Under ADR 0068, an amendment adds a dated,
  add-only ruling. With no change in what the ADR decides, there is nothing to
  rule on. The revisit condition is unchanged as well: #79's record already
  says the successor ADR that removes the bypass waits on a measured
  `commit.verification`, and that is still true.
- The devcontainer's SDK comment already speaks only of `global.json` and the
  devcontainer. The three-way rule was written only in the deleted script, so
  the two-way rule stays where it is and nothing was edited.

### #88: not closed

#88 asks the hook for four things. The bootstrap meets one fully and one in
part:

1. **Re-apply on every start, resume included.** Met. The bootstrap's hook has
   no matcher, so it runs for every `SessionStart` source, resume included,
   where Varve's ran only for `startup`.
2. **Win over the platform's rewrite.** Partly met. The bootstrap rewrites the
   identity and signing key in `~/.gitconfig` at every start. It never sets
   `gpg.ssh.program`, though, and in this container `~/.gitconfig` still held
   the platform's `/tmp/code-sign`. Git signed with `ssh-keygen` and the agent
   key here only because the platform's environment set
   `GIT_CONFIG_COUNT=2` with `gpg.ssh.program=ssh-keygen` and
   `user.signingkey=…/claude-signing.pub`. The bootstrap does not set those
   variables, and #88 saw an incomplete set of them break every git command.
3. **Prove it, then fail loudly.** Not met. The bootstrap signs no probe and
   checks none of the resolved values. Every failure is still a line on
   standard error.
4. **Done when …** Not shown. No restart and resume was observed here, and a
   broken `gpg.ssh.program` would not be reported at start.

So this change references #88 and does not close it. What remains belongs in
the bootstrap now, since that is where the hook lives. #88 can be closed by
reference once the bootstrap meets points 2 and 3, or moved to that repository.
This session's GitHub access covers only this repository, so it filed nothing
there.

### Commits

One commit, authored as the agent and signed off by the maintainer. It removes
the three files and adds this record. Its trailer closes #155, and its body
refers to #88 without closing it.
