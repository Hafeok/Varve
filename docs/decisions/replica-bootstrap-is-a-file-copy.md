---
set: replica-bootstrap-is-a-file-copy
namespace: varve
adr: 0083
decisions:
  - key: ShipACheckpointAndTheLog
    statement: "A replica is bootstrapped by copying files: the manifest, the log up to the end of the shipped position's commit, and the newest checkpoint at or below it, which the replica opens at exactly that position, replaying only the log after the checkpoint; there is no protocol"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T07:00:00Z
---

The rulings of [ADR 0083](../adr/0083-replica-bootstrap-is-a-file-copy.md), filed unaccepted by session
6c of #10 (ADR 0066).
