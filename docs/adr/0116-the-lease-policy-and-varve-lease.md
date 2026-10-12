# 0116 — The lease at start: a bounded wait, never a forced takeover; `varve lease`

## Status

**Accepted — filed unaccepted by milestone Operability of #12, 2026-10-09**
(ADR 0066). Decided by the maintainer on the Operability plan ("your
refinement"). Acceptance is the maintainer's act on the pull request.
**Refines [0075](0075-one-process-per-dataset-by-an-os-lease.md)**: the lock
stays the OS's; this ADR says what a start does when it is held and what an
operator may do by hand.

## Context

ADR 0075 holds `derived/LOCK` with `FileShare.None`, which the operating
system releases when the process ends however it ends, and concludes that a
stale lease does not exist and no takeover interval is needed. That is true
on a local filesystem. Two situations still face an operator with a lock that
refuses:

- **a predecessor still draining**: a rolling restart starts the new process
  while the old one finishes its last commits (ADR 0101's shutdown), and the
  new one is refused for a few seconds;
- **a filesystem whose locks do not hold**: a volume moved between hosts
  while a process on the old host still runs, or a network mount that
  answers `ENOLCK` and refuses every lock, so that opening fails for ever.

The Operability brief asked for a takeover after a stated interval. A forced
takeover on a local filesystem is unsafe: deleting a held `LOCK` on Unix
leaves the holder's `flock` on the old inode, and the next open creates a new
inode and takes a second lock, so two processes hold one dataset (ADR 0014's
divergence, by design).

## Decision

1. **At start, a refused lease is waited for, not taken.** The server retries
   the open every second for `Varve:Lease:WaitFor` (default 30 s), logging
   at each retry who holds it from `derived/LOCK.owner` (process id, machine,
   since when; ADR 0075). When the wait ends, the dataset is `failed` with
   the holder named as its reason, which readiness and `GET /datasets`
   report (ADR 0106), and the server starts for the others; a configured
   dataset that does not open still refuses the start, as 0101 decided.
2. **Nothing ever forces a held lock.** The bound is a wait because a lock
   the OS still holds has a live holder, and the only safe outcome is to let
   it finish. An interval after which the lock is broken automatically would
   be a timer that decides the holder is dead when it is slow.
3. **`varve lease <dir>`** prints the owner file and whether the lock is
   held, trying to take it and releasing it at once; exit 0 free, 1 held.
4. **`varve lease --break <dir>`** is for the operator who knows the process
   is dead and whose filesystem did not release the lock. It takes the lock
   to prove it free; when it can, it removes `LOCK.owner` and reports whose it
   was, and exits 0; when the lock is held it refuses, names the holder and
   exits 1, so that `--break` can never let a second process in on a
   filesystem whose locks work. On a filesystem where `TryLock` fails for
   every caller, the command says so and names the filesystem as the
   problem: the dataset must move to one that locks.
5. **The operator guide** has a page for it: what the lease is, what a
   refused start logs, the wait, when `--break` applies and when it does
   not.

## Alternatives considered

- **A heartbeat in `LOCK.owner` and a takeover when it goes stale.** A
  periodic write to every open dataset for a case the OS already handles on
  a local filesystem, and a timer that can be wrong under load on a network
  one.
- **Takeover after the interval, as the brief first said.** Rejected in
  point 2 for the two-holders failure.
- **No wait at all** (0075 as it stands). Correct and inconvenient: every
  rolling restart is a race the new process loses once.

## Consequences

- `Varve.Server`'s `OpenDatasets` gains the wait; the CLI gains `lease`.
  `Varve.Store` exposes nothing new: the owner file is read by path, as
  `DatasetLeasedException` already reads it.
- `Varve:Lease:WaitFor` joins the configuration and the guide.

## Checks

- **Checked against the accepted ADRs** (0001–0109) and specification 1.6.
  Touches **0075** (refined), **0101** and **0106** (what a failed open
  reports). No conflict.
- **Layer ownership.** `Varve.Server`, layer 6.
- **Analyzer rule.** None.
- **Open questions owned.** None.
