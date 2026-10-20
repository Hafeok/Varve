---
set: varve-management-and-the-vocabulary
namespace: varve
adr: 0166
decisions:
  - key: ManagementPackageAtLayerFour
    statement: "Varve.Management, at layer 4, references Varve.Iri, Varve.Rdf and Varve.Turtle, reads declarations from any IQuadSource and never sees a dataset"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: ManagementModelNamespace
    statement: "Varve.Management's value types live in Varve.Management.Model, the domain model namespace of the package (ADR 0069)"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: ManagementSurfaceCarriesDeclaredValues
    statement: "Varve.Management's surface carries a declaration's and a token's values as the types they are stated in: dataset and instance names, claim types and values, agent IRIs, templates, prefixes, N-Quads lines and messages as string, a flag as bool, a saved plan as UTF-8 bytes; a position of the system dataset is ManagementPosition"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: ManagementGraphNames
    statement: "The system dataset's graphs are varve:manage/fleet, varve:manage/dataset/{enc(name)} and varve:manage/bootstrap, the last written only by the system writer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: ManagementVocabulary
    statement: "Declarations use the varve: terms of ADR 0166 for datasets, settings, placement, grants, principals, graph templates, bindings and the standby opt-in, and no other varve: term"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: TemplatesOverTheCaller
    statement: "A graph template is RFC 6570 level 1 over sub, agent and single-valued claims, percent-encoded, expanding to an absolute IRI matched exactly or as a prefix; a caller without the variable gets nothing from the grant"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: BindingsReservedForErasure
    statement: "varve:ClassifierBinding and varve:KeyStoreBinding are reserved for #14 and refused, with no properties defined"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: DeclarationsHaveNoBlankNodes
    statement: "A declaration with a blank node is refused, so that applying it twice leaves the second plan empty"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: CodeValidatorNamesFocusAndPath
    statement: "DeclarationValidator refuses what ADR 0166 lists, each finding naming its focus node and path, as 422 declaration-invalid over HTTP, in the CLI before a plan and on every commit to the system dataset"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: GrantTablePerPosition
    statement: "A GrantTable is compiled once per applied management position and read by a request with one volatile read; readable is the union of read and write grants, writable of write grants, admin and operator dataset-wide and fleet-wide"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
  - key: PlanIsCanonicalDiff
    statement: "A plan is the per-graph diff of canonical N-Quads after RDFC-1.0, with the system position and each owned graph's version, saved as JSON; applying it replaces each owned graph exactly"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-19T00:00:00Z
---

The rulings of [ADR 0166](../adr/0166-varve-management-and-the-vocabulary.md), one line each.
Filed unaccepted by milestone Management plane of #105 (ADR 0066); every citation is `CS0618`
until the maintainer accepts them.
