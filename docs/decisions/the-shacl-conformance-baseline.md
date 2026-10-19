---
set: the-shacl-conformance-baseline
namespace: varve
adr: 0149
decisions:
  - key: ShaclSuitesUnderRatchet
    statement: "The 2017 SHACL core and sparql suites and the SHACL 1.2 core and sparql Validate cases run from a pinned data-shapes submodule under the ratchet, every failure exempted with its reason"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-18T09:57:00Z
  - key: ShaclDraftsPinned
    statement: "SHACL 1.2 means the Core draft of 2026-10-14, the SPARQL draft of 2026-10-11 and data-shapes commit 4cd4698, revisited when 1.2 Core reaches Candidate Recommendation"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-18T09:57:00Z
  - key: ShaclTwelveWinsConflicts
    statement: "Where SHACL 2017 and the pinned 1.2 drafts conflict, Varve follows 1.2 and exempts the 2017 case with that reason"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-18T09:57:00Z
  - key: IncompleteDataLeavesResults
    statement: "Incomplete nodes (SHACL 1.2 Core 6.8.3) leave every validation result as it is, which the section requires; presentation is the caller's"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-18T09:57:00Z
---

The rulings of [ADR 0149](../adr/0149-the-shacl-conformance-baseline.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
