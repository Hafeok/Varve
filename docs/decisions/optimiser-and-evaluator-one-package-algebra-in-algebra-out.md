---
set: optimiser-and-evaluator-one-package-algebra-in-algebra-out
namespace: varve
adr: 0048
decisions:
  - key: SparqlPackagesOneLayerApart
    statement: "Varve.Sparql at layer 2 owns the algebra, the parser and the serialiser, and Varve.Sparql.Evaluation at layer 3 owns the evaluator over IQuadSource and the optimiser"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: SparqlPackageReferences
    statement: "Varve.Sparql references Varve.Rdf and Varve.Iri, and Varve.Sparql.Evaluation references Varve.Sparql, Varve.Rdf, Varve.Xsd and Varve.Iri"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: OptimiserIsAlgebraToAlgebra
    statement: "The optimiser is a function from algebra to algebra with no plan type, applied by default and skippable by a caller"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: AlgebraNodesAreSealedRecords
    statement: "The algebra's node types are sealed, immutable C# records with SourceSpan excluded from equality, the one stated exception to records in shipped code"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
  - key: RewritingByTypeSwitch
    statement: "The optimiser rewrites through an abstract rewriter with one virtual method per node type and pattern matching, never reflection"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-24T00:00:00Z
---

The rulings of [ADR 0048](../adr/0048-optimiser-and-evaluator-one-package-algebra-in-algebra-out.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

`SparqlPackagesOneLayerApart` is the answer to ADR 0003's open question 2, which 0048 closed.
