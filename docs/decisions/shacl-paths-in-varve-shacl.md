---
set: shacl-paths-in-varve-shacl
namespace: varve
adr: 0152
decisions:
  - key: ShaclPathsInShaclCore
    statement: "Varve.Shacl evaluates SHACL paths over IQuadSource as sets, closures by breadth-first search with a visited set per focus node"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-18T09:57:00Z
  - key: PathsAgreeByDifferentialProperty
    statement: "SHACL paths and the evaluator's property paths are held to the same answers at every focus node by a differential property in a test project, except in the one case SPARQL 1.1's algebra decides differently, a zero-length step through a sequence from a node the graph does not hold, which it excludes by name"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-18T09:57:00Z
---

The rulings of [ADR 0152](../adr/0152-shacl-paths-in-varve-shacl.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
