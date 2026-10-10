---
set: the-soak-gate
namespace: varve
adr: 0082
decisions:
  - key: JudgedUnderTheShippedConfiguration
    statement: "The soak gate is judged under the runtime configuration Varve.Server ships, workstation concurrent GC with ConserveMemory 5 and no heap hard limit of its own, the default-runtime figures reported beside it; amended 2026-10-09 under ADR 0110"
---

The rulings of [ADR 0082](../adr/0082-the-soak-gate.md), filed unaccepted by session
6c of #10 (ADR 0066).
