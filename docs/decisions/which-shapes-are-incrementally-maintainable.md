---
set: which-shapes-are-incrementally-maintainable
namespace: varve
adr: 0171
decisions:
  - key: AffectedByFootprint
    statement: "A delta affects a shape and focus node when the triples the pair's validation may read meet the assertions in the post-state or the retractions in the pre-state, or when the node entered or left the shape's targets; every other pair keeps its results"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
  - key: AnchorsInBothStates
    statement: "Affected nodes are found by reverse traversal from each changed triple, assertions in the post-state and retractions in the pre-state, and erring wide is sound"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
  - key: AffectedSetsAreALeastFixpoint
    statement: "The affected sets per shape are the least solution of their local anchors and the reverse value-node map along every reference of the dependency graph, polarity ignored"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
  - key: ThreeClasses
    statement: "A shape is incremental, scoped or full by the worst of its targets, path, components and the shapes it reaches, per the table of ADR 0171"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
  - key: SparqlByJoinGraph
    statement: "A SPARQL-based constraint is scoped when every pattern of its algebra is joined to $this, and full otherwise"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
  - key: ClassificationIsStatic
    statement: "The classification is computed from the shapes graph alone and served with the binding"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
---

The rulings of [ADR 0171](../adr/0171-which-shapes-are-incrementally-maintainable.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 2a (#138, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
