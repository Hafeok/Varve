---
set: the-shared-commit-index
namespace: varve
adr: 0161
decisions:
  - key: CommitIndexBesideEachSet
    statement: "Each extent set carries the commit index of its window as a ninth blob, commits, of kind 4 in its existing layout; a merged set's is its inputs' concatenated"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
  - key: ColdStartReadsNoLogBelowTheCatalog
    statement: "An open that began at the catalog's cursor reads the commit index below it from the sets' commits blobs, each checked against the catalog's pin at its end, and reads the log's headers only for a set that has none"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
  - key: NoDerivedFormatChange
    statement: "The shared commit index changes no derived format: it is an optional blob of a kind format 4 defines, which a set written without it does not need"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
---

The rulings of [ADR 0161](../adr/0161-the-shared-commit-index.md), filed
unaccepted by milestone Extent maintenance of #110 (ADR 0066).
