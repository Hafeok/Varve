---
set: sorted-runs-for-the-default-projection-and-checkpoints
namespace: varve
adr: 0041
decisions:
  - key: SixKeyOrders
    statement: "A quad key is four 64-bit ids in one of six orders, SPOG, POSG, OSPG, GSPO, GPOS and GOSP, so every combination of bound positions and every graph mode is a prefix range, with the default graph as id 0"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: RunsAreImmutable
    statement: "A run holds sorted asserted and retracted key arrays per order and is never modified, and a commit adds one run with no object per quad"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: ProjectionStateIsAnImmutableVersion
    statement: "The default projection's state is an immutable version of its position and runs, published by one reference write"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: TieredRunMerging
    statement: "Runs are merged in tiers so a version holds O(log n) runs, and a merge into the oldest run drops its retractions"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: NewestRunDecides
    statement: "A scan merges the runs' ranges, and for equal keys the newest run decides"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: PinCapturesTheVersion
    statement: "Pin() captures the current version by reference, so later commits leave it untouched"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: CheckpointIsOneMergedRun
    statement: "A checkpoint is the runs at P merged to one and written as one derived blob: a versioned header with the position, commit P's header hash and dictionary watermarks, the six key arrays and the dictionary entries"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: CheckpointScannedInPlace
    statement: "A checkpoint is scanned in place by the same code as a live run, with no restore step"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: CheckpointNamesItsCommit
    statement: "A checkpoint whose recorded header hash differs from the log's at its position is ignored as a cache miss"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: RebuildFromNewestCheckpoint
    statement: "The default projection rebuilds from the newest valid checkpoint as its base run and applies the tail, or from the empty run"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
---

The rulings of [ADR 0041](../adr/0041-sorted-runs-for-the-default-projection-and-checkpoints.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
