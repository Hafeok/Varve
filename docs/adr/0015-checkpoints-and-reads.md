# 0015 — Checkpoints, pinned reads, as-of reads, archive horizon

## Status

Accepted. 2026-09-21.

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
T2, T3, R1, R2, R3 and I7.

## Context

`docs/brief.md` lists as a tension: "Log growth, compaction, and snapshotting,
and what time travel guarantees survive compaction."

**This ADR departs from the brief in one respect, and the departure is the
point.** The brief says "A read is pinned to a log position. Snapshot isolation
is therefore a property of the model, not a feature to add," and separately
lists time travel as a thing the log buys. Read together, those merge two
mechanisms into one word. The specification separates them, and where they
disagree the specification takes precedence:

- a **pinned read** is a short-lived engine snapshot with the lifetime of one
  operation — a query, an update's `WHERE` evaluation, a validation run;
- an **as-of read** is time travel: any closed position, served from the nearest
  checkpoint plus a delta overlay.

They have different lifetimes, different costs and different failure modes, and
conflating them leads directly to expecting one to be as cheap as the other.

The second half of the brief's tension — what survives compaction — dissolves
once compaction is not a thing that happens.

## Decision

### There is no compaction in the destructive sense

The log before a checkpoint is **retained**. No feature may depend on removing
bytes from it (§2). What an event store would normally get from compaction —
bounded read cost — comes from checkpoints instead, and the history stays.

### Checkpoints

`K_P` is a materialisation of the state at position `P` — quads and dictionary —
as **immutable, directly queryable sorted runs**. It is derived data under
`derived/`, never a log entry.

- **I7:** `K_P = fold(L[1..P])`. A checkpoint is a fold of the log and nothing
  else, so it carries no information the log does not.
- Created at **any closed position, by any policy, in the background**.
- **Dropping a checkpoint loses nothing.** It is a cache whose miss is slower,
  never wrong.
- *Directly queryable* is the load-bearing word. A checkpoint is not a backup
  format that must be restored before use; a read at that position scans it.

### Reads

- **R1 Pinned.** `Pin()` returns a quad source over the readable head at the
  moment of the call, stable until released regardless of later commits. Engine
  snapshot. Not time travel.
- **R2 As-of.** For any closed `P` at or above the archive horizon, a quad
  source over `G_P`, served as `Overlay(K_Q, net(L(Q..P]))` for the greatest
  checkpoint `Q ≤ P`, or `K_0 = ∅`. **Cost is proportional to the log distance
  `P − Q`, not to the size of the dataset.**
- **R3 Diff.** `Diff(P₁, P₂) = net(L(P₁..P₂])`, computed from the log alone, with
  no state lookup — which is exactly what ADR 0010's effective-delta invariant
  buys.

### No temporal index in the default projection

The default quad projection stores the current state. It does not carry validity
ranges per quad, and as-of reads are not served from an index.

**History that needs routine querying belongs in the graph as data.** If an
application wants to ask "who was the owner on 3 March" as an ordinary query,
that is a modelling decision — bitemporal predicates in the data — not a reason
for every quad in the store to carry two positions and every scan to filter on
them.

### Archive horizon

`T3 Archive` moves `L[1..H]` and checkpoints below `H` to cold storage. It is a
later feature, and one constraint is fixed now: **an as-of read or diff below
`H` without the archive attached fails with an explicit error. It never returns a
partial answer.** A silently truncated history is worse than no history,
because it is indistinguishable from a true one.

## Alternatives considered

- **A temporal index in the default projection** — every quad carrying
  `[from, to)` positions, so an as-of read is a filtered scan at any position
  with no overlay. Genuinely O(1) in log distance, which is the one thing the
  chosen design is not. Rejected, and it is worth being precise about why. It
  taxes every ordinary query: every row grows, every index key gains a range,
  every scan filters, and the store's normal workload — reading the present —
  pays for a capability most reads never use. It also fights immutable sorted
  runs, because a retraction becomes an update-in-place of an existing row
  rather than an append, which is the property that makes checkpoints cheap and
  directly queryable in the first place. And the capability it buys is one a
  bitemporal data model supplies at the level where the application already
  knows which facts have histories worth querying.
- **Destructive compaction** — fold the log up to `H` into a snapshot and delete
  what was folded. The standard event-store answer, and it bounds space as well
  as read cost. Rejected twice over: §2 says the log is never rewritten, because
  copies exist that the store cannot reach; and it destroys as-of below `H`
  outright, which is the brief's own question answered by giving up the thing
  being asked about. Checkpoints give the read benefit with none of the
  destruction; the archive gives the space benefit while keeping the bytes and
  failing honestly when they are detached.
- **A checkpoint per commit.** As-of becomes a single scan at any position.
  Rejected on space: a full materialisation per commit is quadratic in the
  obvious way.
- **No checkpoints — always fold from position 0.** Simplest, and correct.
  Rejected as a design rather than as an implementation stage: it is fine for
  the first million commits and fatal after, and building it as the only path
  would mean the overlay was never exercised.
- **Serving as-of reads from a pinned read taken in the past.** Conflates the two
  mechanisms in the other direction: it would make time travel depend on someone
  having held a handle, which is not a property of the log.

## Consequences

**Checkpoint policy becomes the operational lever, with a stated meaning.** As-of
cost is the log distance to the nearest checkpoint. That is a number an operator
can reason about, and it is the reason a checkpoint may be created at any closed
position by any policy rather than on a fixed schedule.

**Space grows without bound, deliberately.** The log is kept for ever and
checkpoints are added to it. That is the price of never destroying history, and
it is charged honestly rather than hidden. The archive is the pressure valve,
and it is later work; the only thing fixed now is that it fails loudly.

**A held pin is a resource.** While a pinned read is open, the engine cannot
release what it is reading — a checkpoint cannot be dropped, segments cannot be
archived. Pins have the lifetime of one operation for that reason, and a long-held
pin is a leak with visible consequences rather than a clever way to get time
travel.

**I7 has to be tested, not assumed.** §10 requires that a checkpoint at `P` plus
the log tail to `Q` equals full replay to `Q`. A checkpoint that silently
disagrees with the log is the failure mode that would make the whole design
unsound, and it is invisible without that test.

**"There is no compaction" is a claim the storage engine must not quietly
break.** A managed LSM engine that merges and drops superseded records is doing
exactly the right thing for `derived/` and exactly the wrong thing for `log/`.
This is the main constraint the storage engine research note carries into
milestone 6.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0014). Touches
  **0010** (R3 computes from the log alone only because the recorded delta is
  the effective one), **0013** (checkpoints are taken at closed positions, and
  are derived data so a discarded tail cannot corrupt one), and **0017** (the
  overlay R2 is built from). No conflict with any.
- **Layer ownership.** Checkpoints, the archive horizon, `Pin`, as-of and `Diff`
  are owned by `Varve.Store` at **layer 4**. Each returns a **quad source**,
  whose contract is `Varve.Rdf` at **layer 1** — so a caller of an as-of read
  holds a layer 1 type and needs no store concept to consume it. The overlay
  itself is layer 1 and is ADR 0017's to place.
- **Analyzer rule.** None.
- **Open questions owned.** None. Q7's question about whether an access request
  reads `G_head` or every quad ever asserted is adjacent to as-of reads but is a
  data-protection decision, and ADR 0023 owns it.
