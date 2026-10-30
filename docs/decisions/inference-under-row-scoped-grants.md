---
set: inference-under-row-scoped-grants
namespace: varve
adr: 0238
decisions:
  - key: TierOneOverTheRowScope
    statement: "Under a row restriction over an inference binding's data graphs, tier 1 rewriting is served over the row-scoped source and is exact"
  - key: ClosureRefusedToRowRestricted
    statement: "The materialised closure, its views and reserved graphs, the regime it serves and explain are refused to a row-restricted caller as 403 entailment-not-available, never filtered"
  - key: CrossGraphRefusedOverClassifiedGraphs
    statement: "A cross-graph inference binding meeting a classified graph set is refused at apply while a read grant with subject terms covers it"
---

The rulings of [ADR 0238](../adr/0238-inference-under-row-scoped-grants.md), filed unaccepted by milestone 9, part 1 ([#202](https://github.com/Hafeok/Varve/issues/202), under #14)
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
