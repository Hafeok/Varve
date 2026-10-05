# 0083 — Replica bootstrap is a copy of files

## Status

**Proposed — filed unaccepted by session 6c of #10, 2026-10-05** (ADR 0066).
Acceptance is the maintainer's act on the pull request.

Decides how a second dataset is started from a first, before milestone 7's
protocol exists. It supersedes nothing.

**Revisit condition:** archive (T3), which lets the log's prefix stay behind;
or a replica protocol at milestone 7.

## Context

The specification's environment assumption (§2) is that a dataset directory
is copied by tools the store does not know. ADR 0072 made a copy taken while
the store writes open safely. What is missing is a copy that is exactly one
position, and fast to open: a copy of `log/` alone replays the whole log.

## Decision

`Dataset.ShipAsync(IStorage target, Position position)` copies, into empty
storage, through the storage contract:

1. the manifest;
2. every segment before the one the position's commit closes in, whole —
   trailer and all — and sealed as it is;
3. that segment, cut just after the commit's closing record, unsealed;
4. the newest checkpoint at or below the position, written first if there is
   none.

The target opens at exactly that position — nothing after it was copied —
and replays only the log after the checkpoint; its `log/` is a byte prefix of
the source's, so its chain verifies as the source's does, and it continues as
a dataset of its own. **There is no protocol**, and nothing is shared: a
later commit on either side is a divergence ADR 0014 detects if the two are
ever compared.

The log is shipped from genesis because opening verifies the chain from it
(I6). Leaving the prefix behind is archive (T3), which the revisit condition
names.

## Alternatives considered

- **A copy of the directory by the host.** Works (ADR 0072), lands at
  whatever position the copy caught, and replays everything without a
  checkpoint.
- **Shipping only the tail after the checkpoint.** The replica could not verify
  its chain from genesis; that is archive.

## Consequences

- A replica is as large as the source's log up to the position, plus one
  checkpoint.
- Property: for arbitrary histories on records of 64 bytes and segments of
  1 KiB, a replica shipped at any position, to memory or to files, opens at it,
  equals the source read as of it, holds a byte prefix of its log, and commits.

## Checks

- **Checked against the accepted ADRs** (0001–0077) and specification 1.5.
  Touches **0014** (divergence), **0015** (the checkpoint), **0072** (the walk
  that opens the cut segment). No conflict.
- **Layer ownership.** `Varve.Store`, layer 4. Public: `Dataset.ShipAsync`.
- **Analyzer rule.** None.
- **Open questions owned.** None.
