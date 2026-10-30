---
set: rows-on-every-surface
namespace: varve
adr: 0237
decisions:
  - key: QueriesAndGraphStoreOverTheRowScope
    statement: "Queries, GRAPH ?g, dataset clauses, property paths and Graph Store GET evaluate over the row-scoped source, and a readable graph whose quads are all hidden is empty, not 404"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: FeedAndDiffDeliverTheView
    statement: "The feed and the diff deliver the delta of the caller's view, including quads whose visibility changed by reclassification, skipping commits whose delivered delta is empty with true positions"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: CommitsJudgedOnBothSides
    statement: "The commits resource judges additions by the classification at the commit and retractions by the one before it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: ReportWithheldFromRowRestricted
    statement: "A validation report graph, and its commit attachment, are withheld whole from a caller whose subject set is not all for any classifier binding of the dataset"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: AsOfReadsClassifyAtThePosition
    statement: "An as-of read uses the classification at its position and the grants at the management position it is served under, with rule forms over the data at the position"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: FollowerEnforcesTheSameRows
    statement: "A tier-1 follower keeps its own classification projection and enforces the same rows at its reported position"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
---

The rulings of [ADR 0237](../adr/0237-rows-on-every-surface.md), filed unaccepted by milestone 9, part 1 ([#202](https://github.com/Hafeok/Varve/issues/202), under #14)
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
