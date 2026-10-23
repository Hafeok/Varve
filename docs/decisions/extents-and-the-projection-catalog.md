---
set: extents-and-the-projection-catalog
namespace: varve
adr: 0139
decisions:
  - key: ExtentPerOrderAndADictionaryExtent
    statement: "An extent set is one extent per index order and one dictionary extent, derived format 4, each with its statistics in its directory"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: ExtentsAreWrittenOnce
    statement: "An extent is written once under a name never reused, by create-if-absent on an object store, and never changed"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: CatalogNamesTheChain
    statement: "The catalog names the live sets and the position they reach; it is replaced whole after every set it names is written, and only then is a set it no longer names retired"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: CatalogUnderTheEpochFence
    statement: "On an object store the catalog is replaced by replace-if-unchanged against the version the writer holds, and a claim rewrites it under the new epoch, so a superseded writer's swap fails"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: DerivedFormat4IsRebuiltNotMigrated
    statement: "Derived format 4 is not read forever: extents are derived, and a change to their format is a rebuild from the log"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: Format3MigratesAsItRuns
    statement: "A derived format 3 directory opens as it is; its full checkpoints serve until dropped, and the first checkpoint written under format 4 starts the chain of sets"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: ExtentPagesFetchedOnAMiss
    statement: "On an object store an extent is read through the synchronous blob read from a local page cache; a page the cache does not hold is fetched by a ranged read on the miss, and the read waits for it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: EvictionNeverChangesAResult
    statement: "The page cache holds at most its configured bytes; it evicts the least recently read extent's pages, which are fetched again when next read"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: CatalogRecordsTheLogCursor
    statement: "The catalog records where the log continues after its position — the segment, the offset and the backend's slot there — and its sets' ends' commit index entries, so an open lists and reads only the log after it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: CommitLocationsBelowTheCatalogReadOnFirstUse
    statement: "An open that began at the catalog's cursor reads the commit index below it from the log's headers on the first read that needs it, and keeps it as a commit index blob in the local cache"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
  - key: FollowerAttachesTheCatalog
    statement: "A read-only open reads the catalog and replays the log after it, and never writes the catalog"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-17T00:00:00Z
---

The rulings of [ADR 0139](../adr/0139-extents-and-the-projection-catalog.md), filed unaccepted
by milestone Object storage of #92 (ADR 0066).
