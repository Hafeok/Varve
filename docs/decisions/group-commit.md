---
set: group-commit
namespace: varve
adr: 0124
decisions:
  - key: StepsPerRequestWritePerBatch
    statement: "Requests that queue while a write is in flight are taken in arrival order, each run through T1 steps 1 to 6 against the state including every earlier member of the batch, and written as one batch with one flush"
  - key: EachRequestKeepsItsCommit
    statement: "Each member of a batch keeps its own records, closing flag, position, header and header-chain link, and the bytes of log/ do not depend on where a batch began or ended"
  - key: AcknowledgedOnlyWhenTheBatchIsDurable
    statement: "No member of a batch is acknowledged before the flush that makes the batch durable returns, and a failed write fails every member with none acknowledged"
  - key: TimestampTakenAtStaging
    statement: "Each member's timestamp is taken when it is staged as max(clock, ts(previous member)), monotone within and across batches"
  - key: ProjectionFailureEndsTheBatch
    statement: "A default projection failure at member k writes members 1 to k, enters the failed state at k, and answers later members Unavailable because they were never written"
  - key: QueuedCancellationWithdraws
    statement: "A request cancelled while still queued is withdrawn and never written; once taken into a batch it is not withdrawn"
  - key: BoundedBatchesNoAddedIdleLatency
    statement: "A batch is bounded in records and bytes, and a request that finds the sequencer idle is written at once with no timer"
  - key: BatchBoundaryIsInNoContract
    statement: "What a caller can observe of group commit is latency only; the batch boundary is part of no result, feed, index, chain or file under log/"
  - key: BatchesStayWithinTheWriteWindow
    statement: "A batch of two or more commits never has more bytes of records than MaxBytes, which never exceeds the write window, and a member that would exceed it is deferred to lead the next batch before it reads the clock"
---

The rulings of [ADR 0124](../adr/0124-group-commit.md), filed unaccepted by milestone Commit path of #91
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
