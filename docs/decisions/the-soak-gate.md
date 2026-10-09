---
set: the-soak-gate
namespace: varve
adr: 0082
decisions:
  - key: FlatWithinABand
    statement: "The 1.0 soak gate is the one-hour soak with the checkpoint policy on, its working set less the dataset's own (its runs' and checkpoints' directories and its commit table) over the last fifty minutes within plus or minus a quarter of its median and its last ten minutes within a tenth of minutes ten to twenty, with handles and derived files bounded"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-06T07:00:00Z
  - key: JudgedUnderTheShippedConfiguration
    statement: "The soak gate is judged under the runtime configuration Varve.Server ships, workstation concurrent GC with ConserveMemory 5 and no heap hard limit of its own, the default-runtime figures reported beside it; amended 2026-10-09 under ADR 0110"
---

The rulings of [ADR 0082](../adr/0082-the-soak-gate.md), filed unaccepted by session
6c of #10 (ADR 0066).
