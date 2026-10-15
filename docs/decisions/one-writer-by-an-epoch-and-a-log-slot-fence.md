---
set: one-writer-by-an-epoch-and-a-log-slot-fence
namespace: varve
adr: 0136
decisions:
  - key: EpochInTheWriterRecord
    statement: "The writer epoch, its holder and the epoch history are a writer object beside log/ and derived/, replaced only by replace-if-unchanged; log/MANIFEST stays the format's manifest, written once"
  - key: WriterOpenClaimsTheNextEpoch
    statement: "An open for write claims epoch n+1 against the writer record that shows n, adopts every chunk in log order, and creates the next slot as a fence chunk carrying n+1; a read-only open never claims"
  - key: FenceIsTheNextSlot
    statement: "The fence is the slot after the last existing one in log order, the first slot of the next segment after a seal, so the only new create a superseded writer can make is the fence slot, which exists"
  - key: FenceChunksAreKept
    statement: "A fence chunk is never deleted, by consolidation or anything else; it is one small object per epoch"
  - key: SupersededWriterStops
    statement: "A writer whose create finds a slot holding other bytes stops accepting writes, fails the batch with writer-superseded, stays open read-only and never claims again; only a new process start claims"
  - key: ShutdownAbdicates
    statement: "A graceful close releases the epoch in the writer record with the reason shutdown, so the next writer's claim follows a release rather than superseding"
  - key: OperatorTakesOverByEpoch
    statement: "varve lease --take claims n+1 only if the writer record still shows the epoch named, with no time-based check, and records the trigger operator"
  - key: EveryClaimIsRecorded
    statement: "Every claim is recorded in the epoch history with its holder, its fence slot and its trigger, restart or operator, and a release with its reason; no commit is written for it"
  - key: NoLeaseNoTimer
    statement: "Nothing is renewed and nothing is decided from elapsed time; a superseded writer learns of it at its next create"
  - key: WriterSupersededProblem
    statement: "writer-superseded is a problem type with status 503, and a superseded dataset reports superseded on /health/ready"
---

The rulings of [ADR 0136](../adr/0136-one-writer-by-an-epoch-and-a-log-slot-fence.md), filed unaccepted
by milestone Object storage of #92 (ADR 0066).
