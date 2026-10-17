---
set: shacl-inputs-failures-and-instances
namespace: varve
adr: 0151
decisions:
  - key: ShapesAndDataAreQuadSources
    statement: "The shapes graph and the data graph are graph patterns of one or two IQuadSources, read only through the quad source contract"
  - key: ShapeConstantsLookedUpOnly
    statement: "A shape constant is resolved in the data source by TryInternalise only; a term the source does not hold matches nothing, and validation never adds a term to a source"
  - key: ShaclInstancesWithoutEntailment
    statement: "A SHACL instance is found by rdf:type/rdfs:subClassOf* in the data graph; any sh:entailment but simple entailment is a failure"
  - key: ImportsNotFollowed
    statement: "owl:imports in a shapes graph is not followed, and the report states each import as varve:importNotFollowed"
  - key: IllFormedShapesRefused
    statement: "A shapes graph with an ill-formed shape is refused before validation with a problem naming the shape and the rule"
  - key: FailuresAreProblems
    statement: "A failure, including a pattern that fails to compile or exceeds ShaclOptions.RegexTimeout, ends validation with problems and never with a report"
---

The rulings of [ADR 0151](../adr/0151-shacl-inputs-failures-and-instances.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
