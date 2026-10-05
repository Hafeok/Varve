---
set: the-soak-gate
namespace: varve
adr: 0082
decisions:
  - key: FlatWithinABand
    statement: "The 1.0 soak gate is the one-hour soak with the checkpoint policy on, its working set over the last fifty minutes within plus or minus a quarter of its median and its last ten minutes within a tenth of minutes ten to twenty, with handles and derived files bounded"
---

The rulings of [ADR 0082](../adr/0082-the-soak-gate.md), filed unaccepted by session
6c of #10 (ADR 0066).
