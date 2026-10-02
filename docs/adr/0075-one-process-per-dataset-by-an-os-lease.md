# 0075 — One process per dataset directory, held by an exclusive handle

## Status

**Proposed — filed unaccepted by session 6a of #10, 2026-10-02** (ADR 0066).
The maintainer chose this shape over a renewed timestamp on the 6a plan.

## Context

ADR 0011 puts one sequencer in front of a dataset. Within a process `Dataset`
enforces it; across processes nothing did, because nothing was on disk. Two
processes appending to one segment file interleave their records — not a fork
the header chain would report (ADR 0014), but corruption inside one segment.
The CLI and a server pointed at one directory is the ordinary way to get there.

The brief proposed a lease file with an owner id and a renewed timestamp, taken
over when stale. That needs two things the store does not have and should not
grow: a heartbeat task the store owns (the shape ADR 0042 declined), and an
interval after which a live but paused process — a long GC pause, a laptop
asleep — is declared dead while it may still write. A takeover interval trades
one failure for the other.

## Decision

**`FileStorage.OpenAsync` takes `derived/LOCK` with `FileShare.None` and holds
the handle until `DisposeAsync`.**

- On Unix .NET implements `FileShare.None` with `flock(LOCK_EX)`; on Windows it
  is a share mode. Either way **the operating system releases it when the
  process ends**, however it ends. A stale lease does not exist, so there is no
  takeover interval to state.
- **The file's contents are for the message only**: an owner string the host
  may set, the process id, and the time the lease was taken from the injected
  clock. A second opener is refused with `DatasetLeasedException` quoting them.
- **The lease lives under `derived/`**, which is excluded from version control
  by the `.gitignore` the store writes there, so a copy never carries a lease.
- **A lease is per directory, not per `FileStorage`**: a second `OpenAsync` of
  the same directory in the same process is refused too.

## Alternatives considered

- **A renewed timestamp with takeover when stale** (the brief's). Rejected
  above: a heartbeat the store owns, and two writers whenever a live process
  outlasts the interval.
- **Lock `log/MANIFEST`** instead of a separate file. On Windows that blocks
  `git add` and backup tools from reading the manifest, which §2 assumes they
  do.
- **No lease; document "one process".** The corruption it prevents is silent.

## Consequences

- **Network file systems are where this is weak**: `flock` over NFS is
  emulated or advisory depending on the server, and SMB share modes depend on
  the client. A dataset on a network share is not a supported deployment for a
  writer, and the documentation says so.
- **A dataset opened read-only by a second process** is not possible in 6a;
  every open takes the lease. A read-only open is a later addition.
- **Deleting `derived/` while a dataset is open** unlinks the lease on Unix; a
  second process could then lease a fresh `derived/`. That is operator error,
  named rather than prevented.

## Checks

- **Checked against the accepted ADRs** (0001–0069). Touches **0011** (one
  sequencer, now across processes), **0042** (no heartbeat task), and **0074**
  (the lease is under `derived/`, never a secret). No conflict with any.
- **Layer ownership.** `Varve.Store`, **layer 4**.
- **Analyzer rule.** None.
- **Open questions owned.** None.
