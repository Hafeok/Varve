---
set: storage-abstraction
namespace: varve
adr: 0018
decisions:
  - key: SegmentStoreAndDerivedStore
    statement: "Storage is an append-only segment store for log/ and a derived blob store for derived/, asynchronous throughout"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ForbiddenOperationsAbsent
    statement: "Truncation, positional writes and deletion of a sealed segment are absent from the storage contract's type, not forbidden in prose"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: DurabilityDeclared
    statement: "A storage backend declares its durability as Synchronised, Committed or None, and the contract never treats them as equal"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: SegmentSizeIsAnInput
    statement: "Segment size is an input to the storage contract, not a constant compiled into a backend"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: NoKeysInDatasetDirectory
    statement: "No key material and nothing that must be deletable is in the dataset directory, and the file backend refuses a key store path inside it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: LogAndDerivedUnconfusable
    statement: "log/ and derived/ cannot be confused, so dropping everything derived is safe"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: DeterministicStorageBytes
    statement: "Nothing in the storage contract admits ambient time, ambient randomness or iteration-order dependence"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: DiscardedTailRepresentable
    statement: "The storage contract can represent a discarded unclosed tail"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: MemoryBackendIsReal
    statement: "The memory backend is a real backend with durability None, not a test double"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: BrowserBackendAtLayer5
    statement: "The memory and file backends may live in Varve.Store, and the browser backend is a separate layer 5 package"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
---

The rulings of [ADR 0018](../adr/0018-storage-abstraction.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

The members of the contract are fixed by ADR 0040, in its set. The revisit condition is not a
ruling and is not enumerated.
