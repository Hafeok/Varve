---
set: shacl-paths-in-varve-shacl
namespace: varve
adr: 0152
decisions:
  - key: ShaclPathsInShaclCore
    statement: "Varve.Shacl evaluates SHACL paths over IQuadSource as sets, closures by breadth-first search with a visited set per focus node"
  - key: PathsAgreeByDifferentialProperty
    statement: "SHACL paths and the evaluator's property paths are held to the same answers by a differential property in a test project"
---

The rulings of [ADR 0152](../adr/0152-shacl-paths-in-varve-shacl.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
