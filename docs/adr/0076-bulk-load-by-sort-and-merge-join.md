# 0076 — Bulk load and I2: sort into a run, merge-join, one multi-record commit (Q2)

## Status

**Proposed — filed unaccepted by session 6a of #10, 2026-10-02** (ADR 0066).

Closes the specification's **Q2**, owned by ADR
[0013](0013-records-commits-and-bulk-load.md) and due at milestone 6. Decided
here so that format version 1 (ADR 0072) carries what a bulk commit needs;
**implemented in 6c**.

## Context

Q2: "normalising a multi-billion-quad commit needs an index lookup per quad.
Loading into an empty dataset is trivial; loading into a populated one needs a
stated strategy." T1 step 3 computes the effective delta against `G_head` (ADR
0010), and the ordinary path does it with one `Contains` per quad against the
projection — a binary search per run per quad, random I/O on disk, and the
pending map in memory.

The default projection is sorted runs (ADR 0041, 0070). A load's input, once
sorted the same way, can meet them in one sequential pass.

## Decision

**A bulk load is one commit (ADR 0013), computed by sort and merge-join.**

1. **Resolve and sort.** Input quads are resolved to ids as they arrive — new
   terms are allocated provisionally, as in T1 step 2 — and sorted in `SPOG`
   order by an **external sort** that spills sorted runs to `derived/`
   (`derived/bulk/…`, removed when the load ends), merged into one run of the
   request's net operations. A later operation on the same quad wins, as in T1.
2. **Merge-join.** The request run is merged with the pinned state's runs in
   `SPOG` order — the same newest-decides merge the cursor does — to produce the
   effective delta: an assert of an absent quad, a retract of a present one.
   **No per-quad index lookup.** The cost is sequential in the size of the input
   and of the state's `SPOG` order.
3. **Commit.** The delta is written as **one multi-record commit**: allocation
   chunks, then assert chunks, then retract chunks, each with its own count
   (ADR 0072), with the content hash computed incrementally and the commit
   header in the closing record. Atomicity is the closing flag, as for any
   commit; a crash loses the load (ADR 0013).
4. **Apply.** The delta becomes a disk run directly — it is already sorted in
   one order and is sorted into the other five by the same external sort —
   rather than passing through the memtable.

**Dictionary allocations are only those the delta reaches** (I3): terms
provisionally allocated for quads that turn out to be redundant are dropped in
step 2 before any id is final, as `Resolver.Finalise` does for small commits.

## Alternatives considered

- **An index lookup per quad** — the ordinary path at scale. Random reads per
  quad against every run; the thing Q2 says does not scale.
- **Bulk load as a sequence of commits.** Rejected by ADR 0013 already.
- **Skip normalisation for loads into an empty dataset only.** Correct and
  trivial for that case, which the merge-join also handles (the state's runs are
  empty) with no special path.
- **A Bloom filter per run** to avoid most lookups. Saves reads for absent
  quads only, and a load into a populated dataset is the case where many quads
  are present.

## Consequences

- **Format version 1 already carries it**: chunked bodies with per-chunk
  counts, 64-bit counts, an incremental content hash. 6c adds no format.
- **A bulk load occupies the sequencer for its duration** (ADR 0013's stated
  cost), and its temporary runs occupy `derived/`.
- **The commit can be larger than memory**, and the writer uses bounded
  memory: what it holds is the external sort's merge buffers.

## Checks

- **Checked against the accepted ADRs** (0001–0069). Closes **Q2** for **0013**;
  touches **0010** (the effective delta, computed differently and equal),
  **0041** and **0070** (the merge is the cursor's merge), **0072** (chunks),
  and **0077** (validators over the same run). No conflict with any.
- **Layer ownership.** `Varve.Store`, **layer 4**.
- **Analyzer rule.** None.
- **Open questions owned.** Closes **Q2**.
