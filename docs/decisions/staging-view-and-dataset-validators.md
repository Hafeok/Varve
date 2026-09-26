---
set: staging-view-and-dataset-validators
namespace: varve
adr: 0058
decisions:
  - key: DatasetBoundValidators
    statement: "DatasetOptions.Validators run on every Data commit in order, before the request's own validators, both seeing the overlay and the delta, and either may reject"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: ValidatorBindingNotInTheLog
    statement: "Binding validators to a dataset is an option of the open dataset, never a fact in the log"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: StagingViewOverAPin
    statement: "DatasetView.Stage() returns a StagingView that reads what the view reads and gives provisional handles, interned by value, for terms it does not hold"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: StagingRefusesBlankTerms
    statement: "Stage refuses a blank node term and a triple term containing one, and StageBlank gives a fresh provisional blank node"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: ToRequestTermMapping
    statement: "ToRequestTerm maps a view's handle to RequestTerm.Existing and a provisional handle to its term, a provisional blank node taking a label unique within the staging view"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: StagingHandlesReachTheLogOnlyByCommit
    statement: "A staging handle reaches the dictionary or the log only through the commit that maps it, being drawn from a range the dictionary never issues"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
---

The rulings of [ADR 0058](../adr/0058-staging-view-and-dataset-validators.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).
