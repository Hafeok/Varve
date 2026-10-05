---
set: bulk-load-by-sort-and-merge-join
namespace: varve
adr: 0076
decisions:
  - key: BulkLoadSortsAndMergeJoins
    statement: "A bulk load sorts its resolved input into a run by an external sort spilling to derived/ and computes its effective delta by a streaming merge-join against the pinned state's runs, with no index lookup per quad"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: BulkLoadCommitsOnceByChunks
    statement: "A bulk load commits its effective delta as one multi-record commit of chunks with their own counts and an incrementally computed content hash"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
---

The rulings of [ADR 0076](../adr/0076-bulk-load-by-sort-and-merge-join.md), filed unaccepted by session
6a of #10 (ADR 0066). Implemented in 6c.
