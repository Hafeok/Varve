---
set: commit-time-gating-under-a-shapes-binding
namespace: varve
adr: 0169
decisions:
  - key: ShaclStoreAtLayerFive
    statement: "Varve.Shacl.Store, layer 5, implements the pre-commit validator contract over Varve.Shacl and Varve.Shacl.Sparql; Varve.Store learns nothing about SHACL and a binding reaches the package as a value"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
  - key: ValidatorReadsOverlayAndPinnedBase
    statement: "A shapes binding's validator reads the shapes graph as of the overlay, the data graph as the union of the bound graphs without the shapes graph and the varve: graphs, and the pinned position as the overlay's base, and nothing else"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
  - key: RejectRefusesNewViolations
    statement: "A reject binding refuses a commit when, for its affected focus nodes, the post-state has a sh:Violation result whose identity (focus node, source shape, component, path, value) is not in the pinned position's report; violations never grow"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
  - key: ReportAndOffModes
    statement: "A report binding accepts the commit with its attachment, and an off binding is recorded only: nothing runs and nothing is attached"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
  - key: FullShapesRefusedAtApplyTime
    statement: "A reject binding whose shapes include a full shape is refused at apply time unless it declares varve:allowFullEvaluation, and any binding not off is refused when its shapes graph is refused; the check runs above the store, before the management commit"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
  - key: ShapesChangesNeverRefusedForExistingData
    statement: "A commit that changes the shapes graph is never refused because of existing data; data changed in the same commit is compared before and after under the new shapes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
  - key: FailingClosed
    statement: "A reject binding whose shapes classify as full without the flag refuses every data commit under it with binding-not-incremental until the binding or the shapes are fixed"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
  - key: NonDeterministicFunctionsRefused
    statement: "In a binding not off, a SPARQL-based constraint calling NOW(), RAND(), UUID() or STRUUID(), or BNODE() whose value can reach a result, is refused with the shape and the function named"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
  - key: OneAttachmentPerBinding
    statement: "A dataset's bindings are validated in the order of their IRIs, the first refusal wins, and an accepted commit records one attachment per binding through ValidationVerdict.Accept with a list"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
  - key: ShapesBindingProblemTypes
    statement: "A refused commit or binding is a 422 problem of type shapes-violated, binding-not-incremental or shapes-invalid, reported by the validator as a problem type and rdf:JSON members"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-21T00:00:00Z
---

The rulings of [ADR 0169](../adr/0169-commit-time-gating-under-a-shapes-binding.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 2a (#138, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
