# 0016 — Projection contract, the synchronous default projection, and erasure in projections

## Status

Accepted. 2026-09-21.

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
§7, §8 and I8.

## Context

The architectural thesis in `docs/brief.md` is that everything queryable is a
projection over the log, and that projections may be dropped, rebuilt, lag, or
be maintained in the background. That is the whole value proposition — pluggable
full-text, vector, geospatial and application read models fed from one log — so
the contract they meet has to be small enough that someone will write one and
strict enough that what they write is correct.

The hard part is not the interface. It is delivery: what a projection is
promised, what it must promise back, and what happens at a crash.

## Decision

### The contract

A projection `π` is a state machine `(state, pos)` with `apply(commit)`:

- It receives **closed commits, in position order** (ADR 0013: never records,
  never an unclosed tail).
- **Delivery is at-least-once.** `apply` of a commit with `pos ≤ pos(π)` is a
  no-op, which makes the *effect* exactly-once.
- It **persists `pos(π)` atomically with its state**. Not beside it, not after
  it — atomically, or the two can disagree across a crash and neither
  idempotence nor rebuild equivalence holds.
- It **may be dropped and rebuilt** from position 0, or from a checkpoint it
  knows how to read.
- **I8, rebuild equivalence:** for every `P`, a projection rebuilt by replay to
  `P` is observationally equal to one maintained incrementally to `P`.

At-least-once plus idempotence-by-position is the whole delivery story. Nothing
in the contract requires a transaction spanning the log and the projection's
own storage.

### The default quad projection is synchronous; everything else lags

The default projection is updated within T1, before `Committed(P)` returns, so
that `Pin()` immediately afterwards observes `P`. Read-your-writes on the primary
index is what every caller assumes, and a store that quietly did not provide it
would produce a flaky test in every codebase that used it.

All other projections are asynchronous: `pos(π) ≤ readable head`, and a consumer
that needs to know whether a projection has caught up asks it.

**Synchronous means "before the call returns", not "atomically with the
append".** The log is the source of truth (§3). If the default projection's
update fails after the records are durable, the **commit stands** — it is in the
log — and the projection is behind, exactly as if it had crashed. It catches up
by replay from its persisted position, and `Pin()` waits for it to reach the
readable head. The alternative, treating a projection failure as a failed
commit, would make a derived artefact able to veto a durable fact, which
inverts the thesis.

*(§7 and T1 step 7 leave this implicit. It is recorded here as a decision and
raised as a proposed clarification to the specification rather than patched
into it.)*

### Subscriptions

`Subscribe(from: P, filter)` delivers closed commits after `P`, in order,
at-least-once. **The consumer owns its position.**

A filter restricts each delivered delta; commits whose filtered delta is empty
are skipped, and **the next delivered commit carries its true position** — so a
consumer that skips a thousand irrelevant commits still resumes correctly,
because the position it stores is a real one.

**`Erasure` commits are always delivered, regardless of filter.** A subscriber
that filtered one out would keep a key it was told to destroy, and a filter is
about relevance, not about permission.

### Erasure in projections

A projection that holds **plaintext derived from private terms** — a full-text
index is the obvious case — records the `KeyId` with each such entry, and purges
those entries when it applies an `Erasure` commit for that key.

Such state lives under `derived/` **and nowhere else** (I9). A projection that
cannot say which of its entries came from which key cannot participate in
erasure, and in a dataset with erasure mode on that is a defect in the
projection, not a limitation of the store.

## Alternatives considered

- **Exactly-once delivery**, via a transactional outbox or two-phase commit
  between the log and the projection's storage. The guarantee everyone wants.
  Rejected as unimplementable across the range the thesis requires: a projection
  may be a Lucene index, a vector store, a remote service, or IndexedDB in a
  browser tab, and none of those will join a distributed transaction with the
  log. At-least-once with idempotence-by-position gives the same *effect* and
  asks the projection only for something it can actually do.
- **All projections synchronous.** Simplest mental model, and read-your-writes
  everywhere. Rejected: write latency becomes the sum of every registered
  projection, and one slow or broken projection stalls the sequencer — which,
  given there is exactly one sequencer (ADR 0011), stalls the dataset.
- **All projections asynchronous, including the default.** Consistent, and it
  removes the special case. Rejected on ergonomics, which is a real criterion
  here: `Pin()` after `Committed(P)` not observing `P` is a footgun that would
  be rediscovered by every user, usually as an intermittently failing test.
- **Store the projection's position separately from its state**, for example in
  a shared table of positions. Much easier to implement. Rejected: a crash
  between the two writes gives either a replayed commit against state that
  already has it — fine only if the projection is idempotent for a reason other
  than position, which it is not — or a skipped commit, which is silent
  corruption. Atomicity here is the one thing the contract insists on.
- **Handle erasure by dropping and rebuilding every projection.** Correct,
  simple, and requires no `KeyId` tagging at all. Rejected on two counts: a
  full rebuild of a large index is an outage, and — more seriously — the
  plaintext remains on disk for the whole rebuild, which is precisely the window
  I9 exists to close. Tagging is the targeted alternative and the cost is one
  identifier per derived entry.
- **Apply the subscription filter at the consumer.** Simpler feed. Rejected: the
  point of a filter is not shipping what is not wanted, especially to a remote
  subscriber.

## Consequences

**A full-text or vector projection over a dataset with erasure mode on must
carry a `KeyId` per entry.** That is a real constraint on what can be plugged in:
an index that cannot attribute its entries to a key cannot be used there.
Datasets with erasure mode off pay nothing.

**"Rebuildable" is load-bearing and needs the property test.** I8 is the
invariant that makes dropping a projection a safe operation, and a projection
whose incremental path and replay path diverge is broken in a way that only
shows up long after the divergence. §10 requires the test.

**The default projection is in the sequencer's critical path**, so its cost is
write latency for every commit. That is the price of read-your-writes and it is
paid by every writer, including bulk loads.

**A lagging projection is a normal state, not an error.** `pos(π) ≤ readable
head` is in the contract, so anything built on a projection has to be able to
ask how far behind it is and decide for itself. The store does not hide the lag.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0015). Touches
  **0011** (the synchronous default projection runs inside the sequencer, adding
  to the single-writer cost already named there), **0013** (projections see
  commits, never records, and never an unclosed tail), and **0015** (a projection
  may rebuild from a checkpoint, which is why a checkpoint is directly queryable
  rather than a restore format). No conflict with any.
- **Layer ownership.** The projection contract, the subscription feed and the
  registry of projections belong to `Varve.Store` at **layer 4**, free of SPARQL
  and SHACL per ADR 0005. A projection *implementation* that needs anything
  above layer 4 — full text, vector, a SHACL validation report as a queryable
  graph — is a **layer 5** integration. What a projection exposes to a reader is
  a quad source, whose contract is `Varve.Rdf` at **layer 1**.
- **Analyzer rule.** None new. The reserved **VARVE0005** (no mutable static
  state, no static registries) is the one that will matter here: a projection
  registry is exactly the shape that tends to become a static registry, and it
  must not.
- **Open questions owned.** None.
