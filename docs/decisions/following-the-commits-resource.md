---
set: following-the-commits-resource
namespace: varve
adr: 0129
decisions:
  - key: FollowRangesThenTail
    statement: "FollowAsync reads history as paged delta ranges up to the head the status reports, then the live tail as server-sent events resumed by Last-Event-ID"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: CheckpointAfterHandler
    statement: "The checkpoint is saved only after the handler returns, so delivery is at least once and a crash between them delivers the commit again"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: CheckpointIsTheCallers
    statement: "IFollowCheckpoint has LoadAsync and SaveAsync and the caller implements it; FileFollowCheckpoint replaces its file atomically"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: FollowRidesOutTransientFailures
    statement: "A dropped connection, a shutdown or error event, 429, 503 and a 5xx before the first byte are retried with backoff on the client's TimeProvider; other 4xx and a malformed body end the follow with the problem"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
---

The rulings of [ADR 0129](../adr/0129-following-the-commits-resource.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Client and integration of #93
(ADR 0066); every citation is `CS0618` until the maintainer accepts them.
