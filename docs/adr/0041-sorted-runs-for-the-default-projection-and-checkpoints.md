# 0041 — Sorted runs: one representation for the default projection and for checkpoints

## Status

**Accepted.** 2026-09-23.

Decides the in-memory representation of the default quad projection and of a
checkpoint (spec T2, R1, R2; ADR 0015). No byte is frozen: a checkpoint is
derived data, droppable without loss, and its encoding carries a version byte.
The durable layout is milestone 6's.

## Context

Four requirements pull on the default projection at once.

- **R1: a pinned read is stable** regardless of later commits, and is cheap —
  it has the lifetime of one operation.
- **Constraint 5: a scan allocates nothing per quad**, and the milestone asks
  that a commit's index update allocate nothing per quad either.
- **T2: a checkpoint is immutable, directly queryable sorted runs**, including
  the dictionary. "Directly queryable" is the load-bearing phrase in ADR 0015: it
  is scanned where it is, not restored.
- **R2: as-of is `Overlay(K_Q, net(L(Q..P]))`**, so whatever a checkpoint is,
  the overlay must be able to stand on it.

A mutable ordered index — a B-tree, a sorted set — meets the scan requirement and
fails R1 unless readers lock or the tree is copied on write. Copy-on-write
allocates a path per inserted quad, which fails the second requirement.

## Decision

### A quad key, six orders

A quad is four 64-bit ids (ADR 0012); a **quad key** is those four ids permuted
into one of six orders — `SPOG`, `POSG`, `OSPG`, `GSPO`, `GPOS`, `GOSP` — and
compared lexicographically. The six are what give every combination of bound
positions a prefix range. The default graph is id 0, so `DefaultGraph` is the
prefix `g = 0` and `AnyNamed` the range `g ≥ 1` in the graph-first orders:
the four graph modes of ADR 0022's amendment are ranges, not filters.

### A run is immutable

A **run** holds, for each order, a sorted array of asserted keys and a sorted
array of retracted keys. It is never modified after it is built.

- **A commit adds one run** built from its effective delta: a constant number of
  arrays, `O(N)` bytes, **no object per quad**.
- **The projection's state is an immutable version**: its position and its list
  of runs, oldest first. Applying a commit builds a new version and publishes it
  with one reference write, which is what "`pos` persisted atomically with
  state" means in memory.
- **Runs are merged in tiers**: after each append, the newest two are merged
  while the newer is at least a quarter the size of the older. Sizes grow
  geometrically, so a version holds `O(log n)` runs. A merge into the oldest run
  drops retractions, because there is nothing older for them to cancel.
- **A scan merges the runs' ranges**; for equal keys **the newest run decides**,
  and the quad is emitted when that run asserted it. This is exact whether or not
  the deltas are effective, so the merge does not lean on I2 for correctness.

### Pin is taking the current version

`Pin()` captures the current version — one reference. Later commits publish new
versions and leave the pinned one untouched. R1 holds by construction and a pin
costs one small object.

### A checkpoint is the runs at `P`, merged to one, stored as bytes

`CheckpointAsync(P)` materialises `G_P`, sorts it into the six orders, and
writes one blob to the derived store: a header (version byte, position, the
header hash of commit `P`, dictionary watermarks), the six sorted key arrays,
and the dictionary entries up to the watermark in the log's term encoding (ADR
0045).

**It is scanned in place.** The key arrays are reinterpreted from the blob's
bytes as quad keys (`MemoryMarshal.Cast`) and walked by the same scan code as a
live run. There is no restore step; the memory backend returns a slice of its
own storage, so loading a checkpoint copies nothing (ADR 0040).

**A checkpoint names the commit it materialises.** On open, a checkpoint whose
recorded header hash differs from the log's at that position is ignored — a
`derived/` copied beside a different log is a cache miss, never a wrong answer.

### As-of and rebuild stand on it

- **As-of `P`** is `QuadOverlay(K_Q, net(L(Q..P]))` for the newest checkpoint
  `Q ≤ P`, or the empty run — the layer 1 overlay of ADR 0017, not a second
  implementation.
- **Rebuilding the default projection** loads the newest valid checkpoint as the
  base run and applies the tail commit by commit, or starts from the empty run.

## Alternatives considered

- **A mutable sorted index with readers under a lock.** Simplest, and scans are
  one array walk. Rejected by R1: a pinned read would hold the lock for the
  lifetime of a query, or see later commits.
- **A persistent (copy-on-write) B-tree.** Pin is free and state is always one
  structure. Rejected on the allocation requirement: an insert copies a path, so
  a commit of `N` scattered quads allocates `O(N log n)` nodes.
- **Serve pinned reads as an overlay of the inverse deltas on the live head.**
  No versioning at all. Rejected: a pin's cost grows with every commit after it,
  and the live structure has to be safe to read while the sequencer writes it.
- **Three orders plus filtering**, halving index size. Tempting given ADR
  0012's revisit condition is about index size. Rejected for now: patterns with
  two bound positions that no order serves as a prefix become filtered scans,
  and a benchmark of the six-order layout is what the revisit condition asks
  for. The order set is not frozen; the benchmark reports bytes per quad for it.
- **A checkpoint as in-process objects** rather than bytes. No encoding to
  write. Rejected: the derived store would then be exercised by nothing, the
  checkpoint could not outlive the `Dataset` object, and "directly queryable"
  would be true only of memory — which is where it is least interesting.

## Consequences

- **Index size is `6 × 32 = 192` bytes per quad** in the base run, plus the
  dictionary. This is the number ADR 0012's revisit condition is judged
  against, and the milestone benchmark reports it.
- **Scan cost grows with the number of runs**, which the tiering bounds to
  `O(log n)`. A projection that has just absorbed many small commits scans more
  runs until the next merge.
- **A merge allocates the merged run's arrays.** It is `O(size)` bytes in a
  constant number of arrays, amortised `O(log n)` per quad over time — measured
  and reported apart from the per-commit cost, because it is not per commit.
- **The scan code runs over spans**, so a checkpoint in a blob and a run in
  managed arrays are the same thing to it. That is what makes the file backend
  at milestone 6 a question of paging rather than of a second index.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0018, 0021–0040).
  Touches **0012** (32-byte keys; the index size its revisit condition asks
  about), **0015** (checkpoints are derived, immutable, directly queryable, and
  droppable; no compaction of the *log* — merging runs is derived data),
  **0016** (the default projection's position is published atomically with its
  state), **0017** (as-of uses the layer 1 overlay), **0022** (graph modes as
  ranges), and **0040** (bytes may be held, so a checkpoint is scanned in place).
  No conflict with any.
- **Layer ownership.** Runs, versions and checkpoints are `Varve.Store`,
  **layer 4**, and internal: none of this is public API.
- **Analyzer rule.** None. Scan and merge members are marked `[HotPath]` (ADR
  0026) for VARVE0006.
- **Open questions owned.** None.
