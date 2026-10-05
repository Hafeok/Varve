# 0078 — Checkpoints streamed from the runs, and written by a policy

## Status

**Proposed — filed unaccepted by session 6c of #10, 2026-10-05** (ADR 0066).
Acceptance is the maintainer's act on the pull request.

Builds on ADR [0070](0070-the-storage-engine-is-our-own.md) (maintenance off
the sequencer) and ADR [0042](0042-subscriptions-pull-from-the-log.md)'s
amendment of 2026-10-02 (dataset-owned maintenance under an explicit option).
It supersedes nothing. It closes the checkpoint half of issue #61.

**Revisit condition:** a host for which a checkpoint at the head, written as a
merge of the projection's runs, costs more than replaying the log it saves —
the policy would then need a cost model, not two thresholds.

## Context

Milestone 6a wrote a checkpoint by reading the state at its position through
a view, collecting every quad into a list, building a run from it in memory —
six sorted arrays of 32-byte keys — and writing that. Its memory was seven
times the dataset. The 6a soak saw the managed heap peak in the samples where
a checkpoint was written, and issue #61 asked for the causes of the working
set's growth to be separated. The 6c ablation, the 6a code and workload for
twenty minutes each, separated them:

| Run | Working set, mean | Last ten minutes | Peak |
|---|---:|---:|---:|
| commits, pins and merges only | 188 MB | 178 MB | 252 MB |
| + checkpoints | 272 MB | 311 MB | 552 MB |
| + as-of reads, no checkpoints | 739 MB | 1,186 MB | 2,152 MB |
| everything, as 6a ran it | 354 MB | 449 MB | 1,387 MB |

Two causes: the checkpoint built in memory, and as-of reads, whose cost — by
R2 — is the log distance to the nearest checkpoint below the position, and
which, with no checkpoint, replay the whole log into memory on every read.
6a wrote checkpoints only when asked.

## Decision

### A checkpoint is a streaming merge of runs, written straight to its blob

The runs that make up a position are merged by ADR 0041's rule, one order at a
time, into the blob writer: the projection's own runs when the position is its
head, else the nearest checkpoint at or below it and the log tail after it as
one run. A memtable flush and a disk merge are the same merge (folding the
newest-decides rule over any number of runs, which composes over a chain of
exact deltas, ADR 0047). **The bound**, stated in the writer and the store's
documentation:

> one 4 KiB block per input section of the order being written, plus the
> writer's 64 KiB output buffer, plus the fences the derived directory needs —
> 40 bytes per 128 keys per section — which the loaded checkpoint holds in
> memory anyway; and, for a position below the head, the log tail, which an
> as-of read at that position would hold too.

### A checkpoint policy, under the maintenance option

`DatasetOptions.Checkpoints` is a `CheckpointPolicy`: a checkpoint at the head
every `EveryCommits` commits or every `EveryLogBytes` bytes of log since the
newest, whichever comes first, keeping the newest `Keep` (zero keeps all).
`Never` is the default, so nothing changes for a dataset that does not ask.
It is maintenance: the background task writes it when
`DatasetOptions.Maintenance` is `Background`, `Dataset.MaintainAsync` when it
is `Off` — the browser's default, which ADR 0084 keeps. A commit never writes
one and never waits for one. Each commit's cumulative log bytes are kept
beside its position, from the writer or, at open, from the scan.

R2 is now a property: an as-of read reads **exactly** the records between the
nearest checkpoint at or below its position and the position — tested over
random histories and policies by counting the bytes the log store returns.

### A run is deleted once nothing reads it

A run a merge or flush retires is deleted only when its last reader lets go:
a pinned or as-of view still reading it keeps it, and the first maintenance
round after the view is disposed deletes it. Run names carry a sequence
number and are never reused, so a deferred delete never removes a newer run;
a crash before it happens leaves a run no state names, which open deletes.
6a deleted at once and relied on the file system letting an open file be
read after its name was gone, which ADR 0071's Windows amendment had to work
around; the test now holds a pin across a flush and a merge and finds the
runs it reads still listed until it is disposed.

## Alternatives considered

- **Checkpoints on a timer.** What the soak did. A timer knows nothing of how
  far the log has run; a quiet dataset writes checkpoints of nothing, a busy
  one lets as-of distance grow without bound between ticks.
- **Checkpoint by quads changed.** Closer to the cost of a read than commits
  are, but not to the cost of opening, which reads log bytes. Log bytes are
  both; commits are the operator's unit. Both are offered.
- **Bound as-of memory instead,** by merging the tail into a run on disk per
  read. Moves the cost from memory to I/O per read and keeps it proportional
  to the distance; the policy bounds the distance itself.

## Consequences

- A checkpoint costs what a disk merge costs: sequential reads of the runs, a
  sequential write.
- With a policy, an as-of read and an open cost at most the policy's distance;
  without one, they cost what they cost in 6a.
- A view held for a long time keeps the runs it reads on disk, as it already
  kept them open.

## Checks

- **Checked against the accepted ADRs** (0001–0077). Touches **0015** (R2's
  cost is now bounded by policy), **0041** and **0070** (the same merge, now
  streamed), **0042** (maintenance under its explicit option), **0071**
  (deletion while a reader holds a blob is no longer relied on). No conflict.
- **Layer ownership.** `Varve.Store`, layer 4. Public: `CheckpointPolicy`,
  `DatasetOptions.Checkpoints`.
- **Analyzer rule.** None.
- **Open questions owned.** None.
