---
set: concurrency-single-sequencer
namespace: varve
adr: 0011
decisions:
  - key: OneSequencerPerDataset
    statement: "One sequencer per dataset processes commit requests one at a time and assigns dense, ascending positions (I1)"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: MonotoneTimestamps
    statement: "The sequencer assigns each commit's timestamp as max(clock, ts(head)), so timestamps are monotone even across a clock that steps backwards (I5)"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: OptionalExpectedPosition
    statement: "An expected position is optional per request, and a request whose expected position differs from the readable head is rejected with Conflict(head) and changes nothing"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: NoCallerHeldWriteLock
    statement: "Serialisation is a property of the sequencer, and a caller never holds a write lock across a decision"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ConflictIsANormalAnswer
    statement: "Conflict(head) is a normal, retryable answer that carries the current head, not an error"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: OncePerCommitWorkInSequencer
    statement: "Everything that must happen exactly once per commit, from position and timestamp assignment to normalisation, validation and the synchronous default projection, happens in the sequencer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: NoAmbientClockOrRandomness
    statement: "Varve.Store reads no ambient clock and no ambient random source, enforced by a banned-symbols list scoped to the deterministic projects"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
---

The rulings of [ADR 0011](../adr/0011-concurrency-single-sequencer.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Moved to a later set: expressing the transaction contract's results over BCL primitives is
superseded by ADR 0065, whose wrapper types are in its set.
