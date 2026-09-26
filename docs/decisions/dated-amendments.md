---
set: dated-amendments
namespace: varve
adr: 0068
decisions:
  - key: AcceptedAdrNotEdited
    statement: "An accepted ADR's text is never edited or deleted: a change of decision is a superseding ADR, and additions are dated amendment blocks beside the text"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-26T00:00:00Z
  - key: DatedAmendmentsAddOnly
    statement: "A dated amendment is an add-only block headed with its date that adds detail, records evidence, corrects the ADR's reasoning or states a needed consequence"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-26T00:00:00Z
  - key: ChangeOfMeaningIsSupersession
    statement: "An amendment never changes what its ADR decided; a change of meaning is a superseding ADR, and doubt counts as a change of meaning"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-26T00:00:00Z
  - key: StatusLineNamesAmendments
    statement: "An ADR's Status line names each of its amendments and each ADR superseding it, with dates"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-26T00:00:00Z
  - key: AmendmentRulingsCarryTheirDate
    statement: "A ruling an amendment adds or changes enters the ledger in the amended ADR's set with the amendment's date"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-26T00:00:00Z
  - key: EarlierAmendmentsRecognised
    statement: "The dated amendments made before this ADR are recognised as they stand"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-26T00:00:00Z
---

The rulings of [ADR 0068](../adr/0068-dated-amendments.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

`AcceptedAdrNotEdited` is ADR 0001's rule as this ADR amends it, moved here from 0001's set.
