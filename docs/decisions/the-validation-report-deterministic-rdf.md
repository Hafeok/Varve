---
set: the-validation-report-deterministic-rdf
namespace: varve
adr: 0154
decisions:
  - key: ReportIsAValue
    statement: "A validation report is an immutable value read as a summary, as typed results, or as RDF, each without the others"
  - key: DataBlankNodesKeepLabels
    statement: "A blank node of the data keeps the label its source gives it in the report; only the report's own nodes are labelled, by RDFC-1.0 with data nodes held fixed"
  - key: ResultsInAStatedOrder
    statement: "Results are ordered by focus node, path, value, shape, constraint, component, severity and message by their canonical forms, and two equal results are kept as two"
---

The rulings of [ADR 0154](../adr/0154-the-validation-report-deterministic-rdf.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
