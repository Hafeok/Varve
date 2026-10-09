# Cloud sessions sign with the maintainer's agent key

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#79](https://github.com/Hafeok/Varve/issues/79), opened by this session |
| **Date** | 2026-10-09 |
| **Tool** | Claude Code 2.1.295, a local session in WSL on the maintainer's machine |
| **Model** | `claude-fable-5-1`, from the session's own metadata |
| **Session identifier** | `d17ad5ac-d36a-45eb-a1cb-086a7cf025e3` |
| **Branch** | `ci/cloud-session-signing`, from `main` at b8bf904 |

## The prompts

The session began on 2026-10-05 as the maintainer's set-up of his own commit
signing (an OpenPGP key whose subkeys live on a hardware token, two addresses,
the backup in his password manager), which touched nothing in this repository.
The prompts below are the ones that led to this record.

> Great - so my setup works but i want to have my own keys for my claude code cloud setup. So i need a ssh key for the agents running there on the email claude+emil@okkels-klein.dk - the email is already verified on github

> you can use the pass-cli to save the key somewhere not on this machine

> create a new PR for Varve with this config

## The report

### Outside the repository, on 2026-10-09

- An ed25519 SSH key was generated for the agent identity
  `claude+emil@okkels-klein.dk`, without a passphrase, since a session cannot
  type one. Fingerprint `SHA256:xDu5EyJd05ROum1g+8Aj1Up3MjE6RbHnI70/xNHljDY`.
- Its public half is registered on the maintainer's GitHub account as a
  **signing** key, titled "Claude Code cloud agents (claude+emil)". The address
  is verified on that account, which is what GitHub needs to report a
  signature verified.
- The private half is stored in the maintainer's password manager and was
  deleted from the machine that generated it. It reaches a cloud session only
  through the cloud environment's variable `CLAUDE_GIT_SIGNING_KEY`, which the
  maintainer sets.

### In the repository

- `.claude/settings.json` registers a `SessionStart` hook.
- `.claude/hooks/cloud-git-identity.sh` runs only where `CLAUDE_CODE_REMOTE`
  is `true` and the variable is set. It writes the key, confirms it parses,
  and configures git: author and committer `Claude (Emil)
  <claude+emil@okkels-klein.dk>`, which is the agent `eng/identities.json`
  already lists, SSH signing on for commits and tags. Without the variable, or
  without `ssh-keygen`, it says so and leaves the session unsigned rather
  than failing it. A local session is untouched (ADR 0034 point 6).

### Against the accepted decisions

ADR 0034 point 4 exempts cloud sessions from the signature requirement, and
its one important rejection is registering **the platform's** key: not
generated for the project, not rotatable, shared across sessions. The key here
is the opposite on each count, and the ADR's **revisit condition** names it:
*if a route appears by which a sandbox commit is signed by a key the project
controls, this exception is superseded*, by a successor that removes the App's
bypass from ruleset 2.

This pull request does not file that successor. ADR 0034 was written from
measurement, not assumption, and so far nothing has been measured: the hook
has not yet run in a cloud session, and `ssh-keygen`'s presence in the sandbox
is assumed from its Ubuntu base, not confirmed. The order is: land the hook;
make one cloud commit; read `commit.verification` on it; then the ADR, with
the evidence in its context. When it comes, it also changes `GOVERNANCE.md`,
`CONTRIBUTING.md` (the exception paragraph and the `git config` it carries),
`AGENTS.md` ("Cloud AI sessions are exempt by ruleset bypass"), and ruleset 2.

### What was not done

- No ADR, decision set, rule or gate changed.
- The repository holds no key material: the hook reads the environment.
- Nothing checks that the variable is set; a cloud session without it is as
  unsigned as before, and visibly so in GitHub.

### Commits

One commit, authored as the agent and signed off by the maintainer, carrying
this record and the two files above.

## Follow-up, the same day and session: the setup script

After the hook landed (e07eabf), the maintainer ran a cloud session and found
what the hook's guard is for.

### The prompt

> the cloud sandbox for claude is missing  ssh-keygen, which the new signing hook requires. - i think we should create a setup script for Varve for Claude cloud environments

### The report

**Measured.** The sandbox has no `ssh-keygen`, so git cannot make an SSH
signature there, and the hook left the session unsigned and said so. The
platform's documentation, read for this: a cloud environment has a **setup
script**, configured in the environment dialog rather than in the repository,
which runs as root on the fresh Ubuntu 24.04 VM before Claude Code launches,
once per environment cache (about seven days, rebuilt when the field changes).
It must exit zero or the session does not start, and finish within about five
minutes or nothing is cached. The same page lists the pre-installed toolchains
and says in words that the .NET SDK is not among them.

**Added.** `.claude/cloud-setup.sh`, the setup script, kept in the repository
as the one source. It installs `openssh-client` and the SDK that `global.json`
pins (10.0.401, the same exact version the devcontainer pins), each step
guarded so that a failed install is reported rather than fatal. The WebAssembly
workloads are left out on the time budget, and the file says so. The hook's
message for a missing `ssh-keygen` now names the script.

**Installing it** is the maintainer's step: set the environment's *Setup
script* field to

```
curl -fsSL https://raw.githubusercontent.com/Hafeok/Varve/main/.claude/cloud-setup.sh | bash
```

`raw.githubusercontent.com` is on the platform's default allowlist.

**Not measured, and what the next cloud session measures.** Whether the
proxy admits `dot.net` and the SDK's download host, which the allowlist does
not name; whether the install fits the five-minute budget; and, once
`CLAUDE_GIT_SIGNING_KEY` is set, whether the commit comes back
`verified: true`. If the SDK download is refused, the fallback is Ubuntu's
`dotnet-sdk` package, whose feature band must then be checked against
`global.json`.

### Commits

One further commit, same shape as the first.
