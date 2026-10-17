---
set: varve-shacl-and-varve-shacl-sparql
namespace: varve
adr: 0148
decisions:
  - key: ShaclCoreAtLayerThree
    statement: "Varve.Shacl, SHACL Core, sits at layer 3 and references Varve.Rdf, Varve.Iri and Varve.Xsd only"
  - key: ShaclSparqlAtLayerFour
    statement: "Varve.Shacl.Sparql, SHACL-SPARQL, sits at layer 4, references Varve.Shacl and the evaluator, and never Varve.Store"
  - key: ExtensionContractInShaclCore
    statement: "Constraints SHACL Core does not define are added through IShaclExtension and IShaclConstraint, defined in Varve.Shacl over Varve.Rdf types only"
  - key: ExtensionsComposedByCaller
    statement: "An extension is passed in ShaclOptions.Extensions by the caller; there is no registry, and a shapes graph needing an absent extension is refused"
---

The rulings of [ADR 0148](../adr/0148-varve-shacl-and-varve-shacl-sparql.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
