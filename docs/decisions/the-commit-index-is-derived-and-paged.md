---
set: the-commit-index-is-derived-and-paged
namespace: varve
adr: 0089
decisions:
  - key: CommitEntriesInDerived
    statement: "The store keeps an eighty-byte entry per closed commit in blobs of derived/index/commits/, in blocks of 128 with each block's first timestamp as its fence, read through the synchronous blob read, a cache miss when another version, kind or dataset, damaged, or not ending at its end hash"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-07T00:00:00Z
  - key: NewestEntriesInMemory
    statement: "The commit index holds the newest DatasetOptions.CommitCache entries in memory, 4,096 by default, and maintenance writes the oldest half out when twice that is held and merges the newest two blobs while the newer is as large as the older"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-07T00:00:00Z
  - key: CapturedVersionsStayReadable
    statement: "A captured commit index version resolves every position up to its head: blobs a newer version merged away are deleted only once no reader holds them, and a reader that finds one closed reads the current version, capping a position found by timestamp at its own head"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-07T00:00:00Z
  - key: IndexBuiltAgainstTheLog
    statement: "Opening checks the commit index's blobs entry by entry against the log as its walk passes each commit and rebuilds from the first that disagrees, holding no more of the index than the cache, and deletes only the blobs it listed and could not use"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-07T00:00:00Z
---

The rulings of [ADR 0089](../adr/0089-the-commit-index-is-derived-and-paged.md),
filed unaccepted by the commit-index slice of #61 (ADR 0066).
