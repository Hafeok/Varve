---
set: the-insert-data-fast-path
namespace: varve
adr: 0120
decisions:
  - key: DataOnlyRequestsGoStraightToTheCommitRequest
    statement: "A request of INSERT DATA and DELETE DATA alone with no expected position writes its ground quads to the CommitRequest as request terms with no staging view, no asserted or retracted set and no sort, one fresh blank node per label per request"
  - key: FastPathKeeps0057And0058
    statement: "The fast path keeps one request one commit and the effective delta because the sequencer normalises the chain against the head it meets, and no handle reaches the dictionary but through the commit"
  - key: DoneAtOrBelowPyoxigraph
    statement: "#35 is done when the median of INSERT DATA of 100,000 quads, parsing included, is at or below pyoxigraph's on the same machine in the same run; otherwise the report says where the time is and the issue stays open"
---

The rulings of [ADR 0120](../adr/0120-the-insert-data-fast-path.md), filed unaccepted by milestone Operability of #12
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
