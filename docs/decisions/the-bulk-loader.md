---
set: the-bulk-loader
namespace: varve
adr: 0081
decisions:
  - key: NewTermsByContentHash
    statement: "A bulk load refers to a new term by a 120-bit hash of its key in a segment that sorts as its final id will, gives new terms their final ids in that order, and refuses the load when two different keys hash alike"
  - key: BulkMemoryIsBounded
    statement: "A bulk load holds at most BulkLoadOptions.MemoryBytes and fixed read buffers, whatever the size of its input: operations and new terms are sorted outside memory in runs spilled to derived/bulk/, which open deletes"
  - key: BulkLoadHoldsTheSequencer
    statement: "A bulk load holds the dataset's sequencer from BeginBulkLoadAsync until it commits or is disposed, with the memtable flushed first, and its delta becomes a disk run of the projection"
  - key: BulkValidatorsReadSources
    statement: "A validator of a bulk commit is given the delta as quad sources over its run on disk through ICommitValidator.Validate(IQuadSource, BulkDelta), whose default reads the delta into memory"
  - key: BulkLoadNeedsAThread
    statement: "The call that fills a bulk load's sort buffer writes it before returning, so a host that cannot block a thread, the browser, cannot bulk-load"
  - key: RecoveryIsBoundedToo
    statement: "Opening after a crash during or just after a bulk load holds at most a bound of any one commit's body, verifying a larger one as it passes, and adopts a delta run that continues the loaded index rather than replaying the load from the log"
---

The rulings of [ADR 0081](../adr/0081-the-bulk-loader.md), filed unaccepted by session
6c of #10 (ADR 0066).
