---
set: resident-memory
namespace: varve
adr: 0240
decisions:
  - key: NothingIsResidentPerCommit
    statement: "What a dataset holds in memory is bounded by its data or by its configuration: the key directories of its runs and of its sets' asserted and retracted sections by the data, and the memtable, the commit cache and the seek directory cache by their options; no structure grows with the number of commits"
  - key: SeekDirectoriesAreReadOnDemand
    statement: "A format-5 extent's seek directory, its rows within's fences, block starts and block checks and its three columns' checks, is verified when its set is opened and not held; a read that needs it takes it from a cache bounded in bytes, the least recently used out first, and holds it until it ends"
  - key: ASeekDirectoryIsCheckedWhenRead
    statement: "A seek directory read after its extent's open is checked, slice by slice, against a check of its words taken while the open verified the directory's hash; a mismatch is damage, answered as ADR 0213 answers damage met in a read"
  - key: TimestampsAreSoughtInTheEntries
    statement: "A commit index blob's position at a timestamp is found by binary search of its entries, read through the blob; the blob's fences are verified when it is opened and not held"
---

The rulings of [ADR 0240](../adr/0240-resident-memory-is-bounded-by-data-and-configuration.md),
filed unaccepted by #222 (ADR 0066).
