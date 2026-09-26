---
set: sparql-update-one-request-one-commit
namespace: varve
adr: 0057
decisions:
  - key: UpdateOverChainedOverlays
    statement: "Each update operation is evaluated in order against the overlay of the pinned staging view and the deltas before it, each delta exact against that source"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: UpdateIsAtMostOneCommit
    statement: "An update request is one commit of its composed delta with the expected position P, or none when its net effect is empty"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: PinReleasedBeforeSubmit
    statement: "The update's pin is released before the composed delta is submitted"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: FailingOperationThrowsBeforeSubmit
    statement: "A failing update operation throws before the submit, releasing the pin, and nothing reaches the log or the dictionary"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: UpdateConflictNotRetried
    statement: "A Conflict is returned, not retried, unless the caller sets ConflictRetries, each retry re-pinning and re-evaluating"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: GraphExistsIffItHoldsAQuad
    statement: "A named graph exists if and only if it holds a quad: CREATE has no effect and fails without SILENT on a non-empty graph, and DROP and CLEAR retract its quads"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: LoadThroughALoadSource
    statement: "LOAD resolves an IRI through ILoadSource, whose default refuses every IRI, and LOAD SILENT turns a failure into no effect"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: UpdateSingleEntryPoint
    statement: "SparqlUpdate.ExecuteAsync over a Dataset is the one entry point, with UpdateOptions, ILoadSource, LoadedDocument and SparqlUpdateException"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
---

The rulings of [ADR 0057](../adr/0057-sparql-update-one-request-one-commit.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

ADR 0005's `SparqlUpdateIsLayer5Integration` stands; this ADR decides what it left open, and
`UpdateIsAtMostOneCommit` states the empty case 0005 did not.
