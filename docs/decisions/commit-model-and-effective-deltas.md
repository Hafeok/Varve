---
set: commit-model-and-effective-deltas
namespace: varve
adr: 0010
decisions:
  - key: EffectiveDeltaNotRequest
    statement: "The log records the effective delta against the pinned state, not the request"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: EffectiveDeltaInvariant
    statement: "A commit asserts nothing already present, retracts nothing absent, and never both asserts and retracts one quad"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: NetOfOrderedOperations
    statement: "The sequencer applies a request's operations in order to an overlay on the pinned state and commits the net result"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: EmptyDeltaNoCommit
    statement: "An empty effective delta produces no commit but NoChange(head), with provisional term ids discarded; a Data commit's delta is never empty, Erasure excepted"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: RejectedRequestLeavesNoTrace
    statement: "A rejected or empty request leaves no commit, no dictionary allocation and no gap in positions"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ReplayIsOverCommits
    statement: "Replay is over commits, never over requests, because a commit's delta depends on the state it was applied to"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
---

The rulings of [ADR 0010](../adr/0010-commit-model-and-effective-deltas.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
