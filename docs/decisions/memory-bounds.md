---
set: memory-bounds
namespace: varve
adr: 0244
decisions:
  - key: MaintenanceOffTheSequencer
    statement: "Memtable flushes, disk merges and checkpoint writes are maintenance, run off the sequencer on a task the dataset owns when DatasetOptions.Maintenance says so, or by Dataset.MaintainAsync; maintenance blocks a commit only as ACommitWaitsForTheFlush says, at twice the memtable's limit; moved from ADR 0070 by ADR 0244's supersession in part, restated"
  - key: ACommitWaitsForTheFlush
    statement: "With background maintenance, a commit that arrives while the memtable holds twice DatasetOptions.MemtableLimit or more waits, before it queues, for maintenance's next step; it does not wait while spills fail, while maintenance has failed or cannot shrink the memtable, or once the dataset is closing, and no timer is involved"
  - key: TheOpenStaysWithinTheWritersBound
    statement: "Opening a dataset replays the log above derived/ through the memtable's limit, flushing it as maintenance would wherever the dataset runs its own maintenance and is not a follower; it keeps the commit bodies its scan read only up to 8 MiB of log, reads the rest from the log as it applies them, lets each go once applied, and nothing of the scan outlives the open"
  - key: ACheckpointHoldsBuffersNotItsWindow
    statement: "A checkpoint written from the log holds its sorts' buffers and at most about 16 MiB of its window's terms, which it writes to spills as term sections and reads back from them when it writes the set's dictionary"
  - key: ACheckpointReleasesWhatItMakesUnnecessary
    statement: "When a checkpoint is published, a dataset that runs its own maintenance flushes its memtable to a run in the round that follows, whatever it holds, so that what the checkpoint covers is no longer held in memory"
  - key: TheDefaultMemtableFollowsTheHeapLimit
    statement: "Varve.Server's default memtable limit is the smaller of 1,000,000 entries and a sixteenth of the collector's heap limit at 215 bytes an entry, never less than 10,000; a value an operator sets is kept as set"
  - key: TheServerReleasesMemoryAfterACheckpoint
    statement: "Once a checkpoint a caller asked for has been taken, Varve.Server lets the dataset's maintenance finish and, if the dataset then takes no commit for five seconds, asks the collector for one aggressive, compacting collection, unless Varve:Store:ReleaseMemoryAfterCheckpoint is false; the checkpoint's answer does not wait for it, no runtime setting releases an idle server's memory, and the store itself never forces a collection"
---

The rulings of [ADR 0244](../adr/0244-memory-bounds-for-writing-checkpointing-and-reopening.md),
filed unaccepted by #227 (ADR 0066).

`MaintenanceOffTheSequencer` moves here from ADR 0070's set under its key,
restated (ADR 0068 point 4): ADR 0070's revisit condition, a memtable that
grows without bound, was met.
