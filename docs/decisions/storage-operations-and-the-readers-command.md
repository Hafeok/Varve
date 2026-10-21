---
set: storage-operations-and-the-readers-command
namespace: varve
adr: 0162
decisions:
  - key: StorageReportsSetsAndRetention
    statement: "varve storage reports a dataset's extent sets per permutation, the bytes the store holds for extents no catalog names, and the bytes each reader record retains"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
  - key: ReadersListAndForget
    statement: "varve storage readers list shows every reader record and what it retains; forget removes one, guarded by the interval it was listed with, under the storage identity's write access"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
  - key: ReadinessReportsRetentionNeverUnready
    statement: "Readiness reports the retained bytes and the reader records behind them, and never turns unready because of them"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
  - key: ReadCommandsMayReadUnannounced
    statement: "The command line's read commands take --unannounced for an identity that can only read; such a read writes no record"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T18:03:53Z
---

The rulings of [ADR 0162](../adr/0162-storage-operations-and-the-readers-command.md),
filed unaccepted by milestone Extent maintenance of #110 (ADR 0066).
