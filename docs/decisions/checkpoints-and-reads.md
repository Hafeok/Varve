---
set: checkpoints-and-reads
namespace: varve
adr: 0015
decisions:
  - key: NoDestructiveCompaction
    statement: "The log before a checkpoint is retained, and no feature may depend on removing bytes from it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: CheckpointIsFoldOfLog
    statement: "A checkpoint at P is a fold of L[1..P] and nothing else (I7): immutable, directly queryable sorted runs under derived/, never a log entry"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: CheckpointsAnyPositionAnyPolicy
    statement: "A checkpoint may be created at any closed position, by any policy, in the background"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: DroppingCheckpointLosesNothing
    statement: "Dropping a checkpoint loses nothing: it is a cache whose miss is slower, never wrong"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: PinnedReadIsASnapshot
    statement: "Pin() returns a quad source over the readable head at the moment of the call, stable until released: an engine snapshot, not time travel"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: PinLivesForOneOperation
    statement: "A pinned read has the lifetime of one operation, because while it is held nothing it reads can be dropped or archived"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: AsOfReadFromNearestCheckpoint
    statement: "An as-of read at a closed position P is Overlay(K_Q, net(L(Q..P])) for the greatest checkpoint Q at or below P, at cost proportional to P minus Q"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: DiffFromTheLogAlone
    statement: "Diff(P1, P2) is net(L(P1..P2]), computed from the log alone with no state lookup"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: NoTemporalIndex
    statement: "The default projection stores the current state only, and history that needs routine querying belongs in the graph as data"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: BelowArchiveHorizonFailsLoudly
    statement: "An as-of read or diff below the archive horizon without the archive attached fails explicitly and never returns a partial answer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
---

The rulings of [ADR 0015](../adr/0015-checkpoints-and-reads.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
