---
set: a-minimal-public-shacl-api
namespace: varve
adr: 0155
decisions:
  - key: MinimalShaclSurface
    statement: "Varve.Shacl's public surface is ShapesGraph, ShaclValidator, the report, ShaclOptions, ShaclProblem and the extension contract"
  - key: ShapesModelInternal
    statement: "The shapes model is internal until the SHACL shapes model milestone, immutable over RdfTerms, and holds nothing from a parser or an evaluator"
---

The rulings of [ADR 0155](../adr/0155-a-minimal-public-shacl-api.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
