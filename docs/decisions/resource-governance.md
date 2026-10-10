---
set: resource-governance
namespace: varve
adr: 0114
decisions:
  - key: EveryLimitHasAProblemTypeADefaultAndAPage
    statement: "Every limit has a default, a problem type under https://w3id.org/varve/problems/, a line in the operator guide, and a property that hitting it produces its problem type and never a timeout or a disconnect"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: ConcurrentReadsBoundedWithAQueue
    statement: "Varve:Limits:MaxConcurrentReads reads run at once and Varve:Limits:ReadQueueLength wait; the next is 503 server-busy with Retry-After, through the shared framework's concurrency limiter on the read endpoints"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: MemoryBoundByCountingNotByTheCollector
    statement: "Varve:Limits:MaxQueryMemory is EvaluationOptions.MemoryBudget, charged by the join, sort, group and distinct tables as they grow; over it is 422 memory-limit-exceeded with limit and actual, or ADR 0095's trailer after the first byte"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: AsOfDistanceBoundedBeforeTheViewOpens
    statement: "Varve:Limits:MaxAsOfDistance bounds an as-of position's distance from the nearest checkpoint at or below it; beyond it is 422 as-of-distance-exceeded, decided from Dataset.Checkpoints before any view is opened, so an as-of read never scans an unbounded log"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: LiveTailsBoundedPerClient
    statement: "Varve:Limits:MaxLiveTailsPerClient bounds the open live tails of one client, the token's subject or the remote address; the next is 429 too-many-live-tails"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: CommitsRangePaged
    statement: "A bounded commits range serves at most Varve:Limits:CommitsPageSize commits and carries Link rel=next when the range holds more, decided before the first byte"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: ServiceDescriptionAdvertisesClientFacingLimits
    statement: "The service description advertises varve:resultSizeCap, varve:maxAsOfDistance and varve:commitsPageSize"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
---

The rulings of [ADR 0114](../adr/0114-resource-governance.md), filed unaccepted by milestone Operability of #12
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
