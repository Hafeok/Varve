---
set: the-storage-engine-is-our-own
namespace: varve
adr: 0070
decisions:
  - key: LogIsASegmentWriter
    statement: "log/ is written by an append-only segment writer of Varve's own, with no keys, no index and no compaction"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: DerivedIsSortedRunsOnDisk
    statement: "derived/ holds the default projection as ADR 0041's immutable sorted runs: the newest in memory as a memtable, and older ones as disk runs written by a memtable flush or a tier merge"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: TheLogIsTheWriteAheadLog
    statement: "derived/ has no write-ahead log of its own and recovers by replaying the log from the projection's persisted position"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
  - key: NoManagedEngineAdopted
    statement: "No managed storage engine is adopted, ZoneTree for owning its file I/O and FASTER for its hash model among them"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-05T07:32:09Z
---

The rulings of [ADR 0070](../adr/0070-the-storage-engine-is-our-own.md), filed unaccepted by session
6a of #10 (ADR 0066). The revisit condition is not a ruling and is not enumerated.

`MaintenanceOffTheSequencer` is superseded in part by ADR 0241 (2026-11-10,
#227) and moves to its set, [`memory-bounds`](memory-bounds.md), under its
key, restated: maintenance still runs off the sequencer, and a commit waits
for a flush only once the memtable holds twice its limit. ADR 0070's revisit
condition, a memtable that grows without bound, was met.
