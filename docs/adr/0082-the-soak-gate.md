# 0082 — The soak gate: a flat working set within a stated band

## Status

**Proposed — filed unaccepted by session 6c of #10, 2026-10-05** (ADR 0066).
Acceptance is the maintainer's act on the pull request.

States the 1.0 gate that issue #61 blocks (issue #15), and how it is measured,
so that "flat" is a number.

**Revisit condition:** a host or workload the soak does not represent being
named for 1.0.

## Context

6a ran a one-hour soak and found the working set not flat (median 168 → 730
MB, peaks 2.8 GB), and did not separate its causes. The maintainer opened
#61, blocking the 1.0 soak gate, and asked 6c to separate them and state the
gate as a flat working set within a stated band. Two things make "flat" need
a definition: the 6a workload's dataset grows for the whole hour (its
vocabulary allows ten million quads), and a store whose dataset grows uses
more of something — at least the fences and the commit table.

## Decision

**The soak** is `--soak 60` of `tests/Varve.Benchmarks` with the checkpoint
policy on (`--policy`: every 1,000 commits, keeping 12, so that the as-of
reads at up to 10,000 positions back have a checkpoint within 1,000): one
writer committing batches of 50 with 25 ms between, two readers pinning and
scanning and reading as of a position, background maintenance, a memtable of
20,000. It samples every 30 seconds after a full collection, with a one-second
sampler of the peak between samples.

**The gate**: over the last 50 minutes, every 30-second sample of the working
set lies within ±25% of their median, and the median of the last ten minutes
is within 10% of that of minutes 10 to 20; open file handles and `derived/`
files stay within fixed bounds. What grows with the dataset is reported per
quad and per commit beside it, and is not counted against the band only where
it is the dataset's own: the run fences (40 bytes per 128 keys per section)
and the table of commit positions (one entry per commit).

The gate is run and reported at each milestone that touches the store; it is
not in CI.

## Alternatives considered

- **Bound the working set absolutely.** Machine- and runtime-dependent; a
  band around the run's own median is not.
- **A shrinking vocabulary so that the dataset reaches a steady state.**
  Hides growth that is the store's, not the dataset's.

## Consequences

- The 6c soak report states the band and whether it held, with the ablations
  that separate the causes.

## Checks

- **Checked against the accepted ADRs** (0001–0077). Touches **0070** and
  **0078** (what the soak exercises). No conflict.
- **Layer ownership.** None; a host's measurement.
- **Analyzer rule.** None.
- **Open questions owned.** None.
