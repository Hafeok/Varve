---
set: storage-contract-members-and-the-memory-backend
namespace: varve
adr: 0040
decisions:
  - key: OnlyTheNewestSegmentUnsealed
    statement: "Segments are numbered by the backend in ascending order, and only the newest may be unsealed or appended to"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: DetachAbsentUntilArchive
    statement: "Detach is absent from the storage contract until archive exists"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: RecordNeverSpansSegments
    statement: "The store seals the active segment when an append would exceed the segment size, so a record never spans two segments"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: MemoryStorageLivesInStore
    statement: "MemoryStorage is a public backend in Varve.Store with durability None"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
  - key: SecondBackendInTheTests
    statement: "Varve.Store.Tests carries a second storage backend written against public members only and runs the contract tests against it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-23T00:00:00Z
---

The rulings of [ADR 0040](../adr/0040-storage-contract-members-and-the-memory-backend.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Moved to a later set: the members' parameter and return types are superseded by ADR 0065's
wrapper types, in its set. Their number and meaning are these.

Moved to ADR 0071's set (2026-10-02): `StorageContractMembers` and `ReadBytesAreImmutable`,
which it supersedes for the derived store and restates for the log.
