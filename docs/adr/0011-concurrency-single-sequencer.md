# 0011 — Concurrency: one sequencer, optional expected position

## Status

Accepted. 2026-09-21. **Superseded in part by
[0065](0065-wrapper-types-and-the-store-log-namespace.md)** (2026-09-25): the
transaction contract's results are no longer expressed over BCL primitives; a
position is a `Position` and a timestamp a `CommitTimestamp`. The sequencer,
the optional expected position and I1/I5 stand.

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
§5 T1, and I1 and I5.

## Context

`docs/brief.md` lists "Single writer per dataset versus optimistic concurrency
with expected position" as a tension to be resolved by ADR.

The two are usually presented as alternatives. They are not: the first is about
who may append, the second is about what a caller may assert about what it read.
A store can have both, and the specification does.

The forces are narrow and unusually clear. Positions must be dense and
assigned in order (I1), because everything downstream — as-of reads, diff,
subscription resumption, projection positions — treats a position as a total
order with no gaps. Timestamps must be monotone (I5), because as-of-by-timestamp
resolves to a position. Both are trivial to guarantee with one assigning agent
and genuinely hard with several.

## Decision

**One sequencer per dataset.** It processes commit requests one at a time. It
assigns positions (dense, ascending), assigns timestamps as
`max(clock, ts(head))` so that I5 holds even across a clock that steps
backwards, normalises the delta against the pinned state, runs the validators,
appends, makes durable, and closes.

**An expected position is optional, per request.** When supplied and different
from the readable head, the request is rejected with `Conflict(head)` and no
state changes. When omitted, the request commits against whatever the head is.

This is optimistic concurrency where the caller wants it and last-writer-wins
where it does not. The caller that read at position `P`, decided something, and
now wants to write on the assumption that nothing has changed says `P`; the
caller appending an observation that does not depend on what it read says
nothing.

**Serialisation is a property of the sequencer, not of a lock the caller
holds.** A caller never holds a write lock across a decision. This is what keeps
`Conflict` a cheap, retryable answer rather than a timeout.

## Alternatives considered

- **Multi-writer with a consensus protocol** (Raft, or a shared-log service).
  The right answer for a distributed store, and it does not need one:
  constraint 3 says the same core runs embedded, as a server, and in a browser,
  and two of those three have exactly one process. Building consensus into
  layer 4 would make the embedded and browser cases pay for it forever.
  Replication is log shipping (the brief), which needs a single writer at the
  source and no consensus at the replica.
- **Mandatory expected position on every commit.** Safer by default, and it
  would make every write an explicit statement about what it read. Rejected: it
  makes append-only workloads — ingest, change capture, anything that is not
  read-modify-write — carry a retry loop for a conflict that cannot matter to
  them. The caller knows which kind of write it is making; the store does not.
- **A lock the caller holds across read and write.** The familiar transaction
  shape. Rejected: it makes a slow or crashed caller a liveness problem for the
  dataset, and in a browser host there is no supervisor to break the lock. The
  expected position gives the same guarantee with no lease to expire.
- **Serialising on the storage layer instead** (append with compare-and-swap on
  the file length or a generation number). Attractive because it needs no
  sequencer object. Rejected: it cannot assign a monotone timestamp, cannot run
  a validator over the pending overlay, and cannot normalise the delta against
  the state it is appending to — all three need to happen inside whatever is
  serialised, not after it.
- **Allow non-dense positions**, for example a timestamp or a hash as the
  position. Rejected by what depends on I1: a subscriber resuming at `P` must be
  able to ask for "everything after `P`" without a range scan over a sparse
  space, and `net(L(Q..P])` must be a finite composition.

## Consequences

**Write throughput per dataset is one sequencer's throughput.** This is a real
ceiling and the specification does not hide it. Concurrency comes from batching
within a commit and from separate datasets, not from parallel writers into one.
The bulk-load path (ADR 0013) exists partly because of this ceiling.

**`Conflict(head)` is a normal answer, not an error.** It carries the current
head, so a caller can re-read, re-decide and retry without a second round trip
to find out where it is.

**Clock monotonicity is enforced, not assumed.** `max(clock, ts(head))` means a
backwards step in the system clock produces a run of equal timestamps rather
than a broken I5. It also means a timestamp is not a reliable wall-clock reading
during such a run, which is the correct trade: as-of-by-timestamp resolves to
*the greatest position at or before `t`*, and that stays well-defined.

**The sequencer is the natural home for anything that must happen exactly once
per commit** — position assignment, timestamp assignment, normalisation,
validation, the synchronous default projection. That concentration is deliberate
and it is also a single point of failure worth naming: everything in T1 steps 1
to 7 happens in one place, and a defect there is a defect in every write.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0010). Touches
  **0005**, whose transaction contract this is the execution model for, and
  **0010**, whose normalisation is T1 step 3 and therefore runs inside the
  serialised region. No conflict with any.
- **Layer ownership.** The sequencer and the expected-position protocol belong
  to `Varve.Store` at **layer 4**. `Conflict`, `NoChange` and `Committed` are
  results of the transaction contract, expressed over BCL primitives and
  `Varve.Rdf` types only, which satisfies the principle the reserved VARVE0007
  will enforce.
- **Analyzer rule.** None new. One thing worth flagging for a later milestone
  rather than a rule now: the determinism property test in §10 requires the
  sequencer's clock and randomness to be injected, which is a **banned-symbols**
  entry (`DateTime.Now`, `DateTimeOffset.Now`, `Guid.NewGuid`, `Random`) scoped
  to the `Varve.Store` project rather than a `VARVE` id. ADR 0004 prefers the
  off-the-shelf mechanism where it suffices, and here it does. Due when the
  project exists, at milestone 4.
- **Open questions owned.** None.
