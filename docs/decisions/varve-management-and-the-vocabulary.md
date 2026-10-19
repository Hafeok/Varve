---
set: varve-management-and-the-vocabulary
namespace: varve
adr: 0166
decisions:
  - key: ManagementPackageAtLayerFour
    statement: "Varve.Management, at layer 4, references Varve.Iri, Varve.Rdf and Varve.Turtle, reads declarations from any IQuadSource and never sees a dataset"
  - key: ManagementGraphNames
    statement: "The system dataset's graphs are varve:manage/fleet, varve:manage/dataset/{enc(name)} and varve:manage/bootstrap, the last written only by the system writer"
  - key: ManagementVocabulary
    statement: "Declarations use the varve: terms of ADR 0166 for datasets, settings, placement, grants, principals, graph templates, bindings and the standby opt-in, and no other varve: term"
  - key: TemplatesOverTheCaller
    statement: "A graph template is RFC 6570 level 1 over sub, agent and single-valued claims, percent-encoded, expanding to an absolute IRI matched exactly or as a prefix; a caller without the variable gets nothing from the grant"
  - key: BindingsReservedForErasure
    statement: "varve:ClassifierBinding and varve:KeyStoreBinding are reserved for #14 and refused, with no properties defined"
  - key: DeclarationsHaveNoBlankNodes
    statement: "A declaration with a blank node is refused, so that applying it twice leaves the second plan empty"
  - key: CodeValidatorNamesFocusAndPath
    statement: "DeclarationValidator refuses what ADR 0166 lists, each finding naming its focus node and path, as 422 declaration-invalid over HTTP, in the CLI before a plan and on every commit to the system dataset"
  - key: GrantTablePerPosition
    statement: "A GrantTable is compiled once per applied management position and read by a request with one volatile read; readable is the union of read and write grants, writable of write grants, admin and operator dataset-wide and fleet-wide"
  - key: PlanIsCanonicalDiff
    statement: "A plan is the per-graph diff of canonical N-Quads after RDFC-1.0, with the system position and each owned graph's version, saved as JSON; applying it replaces each owned graph exactly"
---

The rulings of [ADR 0166](../adr/0166-varve-management-and-the-vocabulary.md), one line each.
Filed unaccepted by milestone Management plane of #105 (ADR 0066); every citation is `CS0618`
until the maintainer accepts them.
