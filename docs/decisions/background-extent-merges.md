---
set: background-extent-merges
namespace: varve
adr: 0158
decisions:
  - key: OnlyTheWriterMerges
    statement: "Only the dataset's writer merges extent sets, in the dataset's maintenance off the sequencer; there is no separate merge worker"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
  - key: MergeWritesThenSwapsOnce
    statement: "A merge writes its set by create-if-absent, then swaps the catalog once by replace-if-unchanged under the epoch fence; a superseded writer's swap is refused and its set is never named"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
  - key: MergeNeverBlocksACommitOrACheckpoint
    statement: "A merge reads and writes holding neither the sequencer nor the checkpoint gate, and takes the gate only for its swap"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
  - key: MergeRebasesOnTheCatalog
    statement: "A merge's swap replaces its input sets only if every one of them is still in the catalog, contiguous and in order, keeps every set added since, and otherwise is abandoned"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
  - key: MergeIsAPureFunctionOfItsInputs
    statement: "A merged set is a pure function of its input sets: it preserves every validity interval exactly, coalesces no history and drops no retracted interval"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
  - key: MergeReplacesThePrefixOfTheProjection
    statement: "When the default projection's runs hold a merge's input sets as its base, the swap replaces them with the merged set under the sequencer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
  - key: ATakenNameTakesTheNextSequence
    statement: "A set whose name a create-if-absent finds taken with other bytes is written again under the next sequence; a superseded writer's creates never fail the writer's checkpoint or merge"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
  - key: UploadsStagedThroughTheCache
    statement: "A derived blob bound for the object store is staged in a file in the cache directory and uploaded from it, never held whole in memory"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
  - key: MergeRefusedPastTheSinglePutLimit
    statement: "A merge whose output could exceed the store's single-request upload limit is refused before it writes anything; multipart upload is a later decision"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
---

The rulings of [ADR 0158](../adr/0158-background-extent-merges.md), filed
unaccepted by milestone Extent maintenance of #110 (ADR 0066).
