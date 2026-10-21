# 0138 — Validity intervals in the default projection's checkpoints

## Status

**Accepted — filed unaccepted by milestone Object storage of #92, 2026-10-15**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Asked for
by the Object storage addendum (requirement 9: "validity intervals replace
point-in-time runs … so it gets its own ADR"); its scope was agreed on the
plan, 2026-10-15.

**Amended 2026-10-19** (ADR 0068), by milestone Extent maintenance of #110,
unaccepted until the maintainer accepts it (ADR 0066): its consequence that
the chain grows by a set per checkpoint until Extent maintenance, and the
checkpoint budget it gave the operator guide, are replaced by the writer's
background merges ([ADR 0158](0158-background-extent-merges.md),
[ADR 0159](0159-the-extent-merge-policy.md)).

**Supersedes in part** [ADR 0015](0015-checkpoints-and-reads.md): its section
"No temporal index in the default projection" and its first alternative, the
temporal index, rejected there. Every other ruling of 0015 stands: the log is
kept, a checkpoint is a fold of the log and nothing else (I7), dropping one
loses nothing, pinned and as-of reads are different mechanisms, and the
archive horizon fails loudly.

Records decision set
[`validity-intervals-in-the-default-projection`](../decisions/validity-intervals-in-the-default-projection.md).

## Context

ADR 0015 served an as-of read at `P` as the greatest checkpoint `Q ≤ P` with
`net(L(Q..P])` overlaid, so its cost was the log distance `P − Q`. A checkpoint
was a full materialisation of the state at its position. That made the
operator's lever a trade between space — every checkpoint a full copy, so
`Keep` bounded them — and as-of cost.

0015 considered and rejected the alternative of giving every quad its
`[from, to)` positions. Its reasons, which this ADR takes over as its own
constraints:

1. **It taxes every ordinary query.** Every row grows, every key gains a
   range, every scan filters, and reading the present pays for history.
2. **It fights immutable sorted runs.** A retraction becomes an update in
   place of an existing row, where the store's runs are append-only and
   immutable.
3. **History worth querying routinely belongs in the data.** A bitemporal
   model is the application's tool where it knows which facts have histories.

The Object storage milestone changes the weight on the other side. At billions
of quads a full checkpoint per position is not affordable in space, and a cold
start or a new follower that restores one is not affordable in time (the
addendum's "Why", and ADR 0140's T7). What scales is what Azure Data Explorer
does: immutable extents over windows of time, each written once, the newest
appended. A window's extent is a delta; the question is only what it carries
so that a read inside the window needs no log.

## Decision

### A checkpoint is a chain of extent sets

A checkpoint at `P` is the chain of **extent sets** covering `(0, e₁]`,
`(e₁, e₂]`, …, `(eₖ₋₁, P]` (ADR 0139 gives their format and the catalog that
names the chain). A set holds every interval of every quad whose assertion or
retraction falls in its window:

- an **open** interval: asserted in the window and still asserted at its end,
  not asserted at its start;
- a **closing** interval: asserted at the window's start, retracted in it,
  not asserted at its end;
- every **other** interval — asserted and retracted inside the window, or
  belonging to a quad asserted at both ends whose assertion changed inside —
  is a **row within**.

A quad asserted throughout the window, untouched, has no row. So a set's size
is the window's changes, not the state, and the chain's total is the log's
changes: no position is materialised twice.

### Each set reads as an exact delta

The open intervals' keys are the set's **asserted section** and the closing
intervals' keys its **retracted section**, in every one of the six orders
(ADR 0041). That is exactly `net(L(eᵢ₋₁..eᵢ])` — an exact delta against the
sets before it (I2) — so a chain is read the way the store reads any list of
runs: the merging scan, the newest run deciding a key. The positions of open
and closing intervals, and the rows within, are separate sections that a scan
of the present never opens.

That answers 0015's first constraint: **a head read scans keys only.** The
default projection's live index is unchanged — runs and a memtable over the
chain as its base (ADR 0070) — and its rows carry no positions. The positions
are paid for by as-of reads, which read them, and by the checkpoint's writer,
which writes them once.

It answers the second: **nothing is updated in place.** A retraction of a quad
an older set asserted is a closing row in the newer set; the older set never
changes. Sets are written once and never modified (ADR 0139).

The third stands as it was: a bitemporal model in the data remains the tool
for history that applications query as data. What this ADR makes cheap is the
store's own as-of read, which the specification already promises (R2).

### As-of reads

For a closed position `P`:

- **At or below the newest set's end:** the sets whose windows end at or below
  `P`, and — when `P` falls strictly inside a set's window `(a, e]` — that set
  **narrowed** to `(a, P]`: its rows restricted to the narrower window and
  classified again, which is exactly `net(L(a..P])`. No log is read. The cost
  is the containing set's size, not the distance from a checkpoint.
- **Above it:** the chain, and the log from its end to `P` overlaid, as 0015
  had it with the chain for `K_Q`.

R2's promise is unchanged and strengthened: the cost is bounded by the log
distance to the newest checkpoint, and is zero log below it.

### Checkpoints as operations

- **At a position above the newest set's end:** one set for the window from
  that end, read from the log.
- **At a position inside a set's window:** that set is replaced by two, split
  at the position, each read from the log over its window.
- **Dropping a checkpoint** merges its set into the next — the older set's open
  intervals and the newer's closing intervals for the same quad are one
  interval — or, for the newest, drops the set. Neither changes a result at any
  position: a merged set read at a position inside it narrows to the same
  delta the two sets gave apart.

So `Keep` still bounds the number of checkpoints, and the policy's meaning
(ADR 0078) is unchanged; what it no longer bounds is space, which the chain
keeps proportional to the log's changes.

### The graph-version index and the bulk loader

- **The graph-version index** (ADR 0125) is written beside each set, at the
  set's end, as it was beside each full checkpoint. A merged set carries the
  newer set's.
- **The bulk loader** is unchanged. Its commit is in the log like any other;
  the next checkpoint reads it from there into a set (ADR 0081's runs stay the
  projection's, not the checkpoint's).

### I7

`K_P = fold(L[1..P])` holds of a chain as of a full checkpoint: each set is
`net(L(eᵢ₋₁..eᵢ])` and the chain composes them (specification §6, ADR 0047).
A set is derived data: one that does not verify against the log is a cache
miss that ends the chain there (ADR 0072).

## Alternatives considered

- **Keep full checkpoints and add an interval index beside them.** Two
  persisted forms of one thing, each written at every checkpoint. Rejected:
  the chain is both, at the cost of one.
- **Positions inline in the key sections** (a 48-byte row instead of a 32-byte
  key). Rejected for 0015's first reason: every scan of the present would read
  and skip them.
- **A set per commit.** As-of is a lookup at any position, but a set per
  commit is a run per commit, and the chain is the log again in another
  layout. The window is the policy's to choose.
- **Narrow by replaying the log inside the window**, keeping only end-of-window
  state in a set. That is 0015's overlay with a smaller distance; it keeps the
  log on every read path and does not let a follower serve history without the
  log's bodies.

## Consequences

- **Space per checkpoint is the window's changes.** A policy that checkpoints
  often costs little; one that keeps many costs nothing extra.
- **The chain grows by a set per checkpoint**, and a head read merges one more
  run. Merging sets in the background, so the chain stays short, is milestone
  Extent maintenance's (ADR 0139); until then, `Keep` merges by dropping, and
  the operator guide gives a checkpoint budget from the measured read latency
  after 10, 50 and 100 checkpoints.
- **A format-3 dataset's full checkpoints keep serving** until dropped; the
  first checkpoint written under this release starts the chain (ADR 0139).
- `MaintenanceTests`' R2 property is restated: below the newest checkpoint an
  as-of read reads no log; above it, exactly the tail.

## Checks

- An extent-backed view equals the reference model at every position, through
  checkpoints written above the newest and inside sets, and drops in any
  order; and again after opening from the log and the extents alone
  (`ExtentTests`).
- Dropping every checkpoint but the newest merges the chain into one set that
  reads the same at every position.
- I7 for one set beside the log (`BehaviourTests.a_checkpoint_plus_the_tail_equals_full_replay`).
- R2 as restated (`MaintenanceTests.an_as_of_read_reads_the_log_only_past_the_newest_checkpoint`).

## Amendment, 2026-10-19 — the chain is bounded by background merges

Filed by milestone Extent maintenance of #110, unaccepted until the
maintainer accepts it (ADR 0066).

The writer now merges adjacent sets in the background
([ADR 0158](0158-background-extent-merges.md)) under a size-tiered policy
with a stated bound ([ADR 0159](0159-the-extent-merge-policy.md)). A merge is
this ADR's drop folded over `F` adjacent sets: the older windows' open
intervals and the newer ones' closing intervals for one quad are one
interval, and every other interval is kept. So no merge changes a result at
any position, and none coalesces history or drops a retracted interval. The
operator guide's checkpoint budget is removed. `Keep` keeps its meaning (at
most `Keep` sets, the oldest dropped by merging into the next). A
checkpoint's end inside a merged window is no longer a set's end, so an
as-of read there narrows the larger, capped set.
