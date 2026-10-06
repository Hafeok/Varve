---
set: checkpoints-streamed-and-written-by-policy
namespace: varve
adr: 0078
decisions:
  - key: CheckpointsAreStreamedMerges
    statement: "A checkpoint, a memtable flush and a disk merge are a streaming merge of runs into the blob writer, holding a block per input section of the order being written, the output buffer and the directory's fences, never the state they materialise"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T07:00:00Z
  - key: CheckpointPolicyIsMaintenance
    statement: "DatasetOptions.Checkpoints writes a checkpoint at the head every so many commits or bytes of log since the newest, keeping the newest so many, as maintenance and never in a commit; Never is the default"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T07:00:00Z
  - key: RunsDeletedWhenUnread
    statement: "A run the projection retires is deleted only once its last reader has let go, by the next maintenance round, and run names are never reused"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T07:00:00Z
---

The rulings of [ADR 0078](../adr/0078-checkpoints-streamed-and-written-by-policy.md), filed unaccepted by session
6c of #10 (ADR 0066).
