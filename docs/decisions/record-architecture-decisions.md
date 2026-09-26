---
set: record-architecture-decisions
namespace: varve
adr: 0001
decisions:
  - key: AdrsNumberedNeverReused
    statement: "ADRs live in docs/adr/NNNN-kebab-title.md, numbered from 0001 in order, never reused and never renumbered"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: AdrFiveSections
    statement: "Every ADR has Status, Context, Decision, Alternatives considered and Consequences, in that order, and an ADR with no alternatives is a note, not a decision"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: AdrStatusValues
    statement: "An ADR's Status is Proposed, Accepted, Superseded by NNNN or Rejected, with the date it reached that status"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: AcceptedWhenSettledOrAgreed
    statement: "An ADR is Accepted when docs/brief.md already settles the matter or the project owner has agreed it, and Proposed otherwise"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: AcceptedAdrNotEdited
    statement: "An accepted ADR is not edited: a change of decision is a new ADR naming the one it supersedes, and only the superseded ADR's Status line changes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: MeaningChangeIsSupersession
    statement: "Fixing a broken link or a typo that changes no meaning is not an edit; anything that changes meaning is a supersession, and doubt counts as a supersession"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: OpenQuestionsAreArtefacts
    statement: "An open question in an ADR is a first-class artefact, written under the decision it affects and never resolved in passing"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
  - key: BindingDecisionsAreEnforced
    statement: "A decision that binds code is enforced or it does not bind, and an ADR that can become an analyzer rule names the rule that enforces it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-20T00:00:00Z
---

The rulings of [ADR 0001](../adr/0001-record-architecture-decisions.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

GOVERNANCE.md's **dated amendment** inside an accepted ADR is practised throughout
`docs/adr/` but decided by no ADR; ADR 0001 permits only a Status-line edit. Recorded in the
session 1 report of #43 as a finding, not filed as a decision here.
