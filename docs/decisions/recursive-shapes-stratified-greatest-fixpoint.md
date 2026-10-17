---
set: recursive-shapes-stratified-greatest-fixpoint
namespace: varve
adr: 0150
decisions:
  - key: RecursionByStratifiedGreatestFixpoint
    statement: "A recursive shapes graph means the greatest fixpoint of each stratum's one-round operator, the strata taken in order, edges of SHACL's refers-to relation labelled by polarity"
  - key: UnstratifiableShapesRefused
    statement: "A shapes graph with a cycle through a negative or target edge is refused before validation, statically, with the cycle named"
  - key: LocalFixpointByIteration
    statement: "A conformance check inside a cyclic component is answered by iterating to the greatest fixpoint over the pairs reachable from it, starting from true"
---

The rulings of [ADR 0150](../adr/0150-recursive-shapes-stratified-greatest-fixpoint.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
