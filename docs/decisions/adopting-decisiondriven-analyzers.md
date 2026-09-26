---
set: adopting-decisiondriven-analyzers
namespace: varve
adr: 0062
decisions:
  - key: TwoAnalyzerPackages
    statement: "Generic rules come from the DecisionDriven.Analyzers package, adopted by configuration and never by changing it, and Varve.Analyzers keeps only rules that know a Varve fact"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: VarveIdReservations
    statement: "ADR 0004's reservation table is retired: VARVE0001 and VARVE0002 are retired for ever, and VARVE0003 to VARVE0008 are released to their DD successors or renumbered"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: RetiredRulePagesStay
    statement: "A retired rule's page stays as a stub naming what replaced it, so the id still resolves"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: NoSuppressionOfDdOrVarveRules
    statement: "A DD or VARVE rule is never suppressed by pragma, SuppressMessage or an editorconfig downgrade, and a DesignDecision citing a filed decision is the only exception path"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: LayerExceptionEscape
    statement: "A reference the layer rule forbids is allowed only by a DesignDecision on the referencing symbol citing a filed, accepted decision"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-26T00:00:00Z
  - key: EveryAdrIsADecisionSet
    statement: "Every ADR is enumerated into docs/decisions as an interim set file in ledger namespace varve, one key per ruling in force, with its acceptance transcribed from the ADR"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: SupersededRulingMovesUnderItsKey
    statement: "A ruling a later ADR supersedes appears once, in the superseding set under the same key and with that ADR's acceptance"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: AcceptanceTranscription
    statement: "A transcribed acceptance is mailto:emil@okkels-klein.dk at the ADR's date, or at a dated amendment's own date for a ruling the amendment changed"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: DecisionSetsCheckedInTheBuild
    statement: "eng/decision-sets.cs checks the set files' front matter in the build job until the analyzer package's generator reads them"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
---

The rulings of [ADR 0062](../adr/0062-adopting-decisiondriven-analyzers.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Received from earlier sets: `VarveIdReservations` from 0004's, `LayerExceptionEscape` from 0003's.
`NoSuppressionOfDdOrVarveRules` is the superseded half of 0004's suppression policy, whose other
half stays there as `SuppressionCitesAdr`. `LayerExceptionEscape` carries 2026-09-26, the date
the maintainer decided it on the pull request, before the ADR merged.
