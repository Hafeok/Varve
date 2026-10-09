---
set: a-write-over-http-is-one-commit
namespace: varve
adr: 0094
decisions:
  - key: WriteIsAtMostOneCommit
    statement: "Every write request is at most one commit: an update through the executor with no retries, a Graph Store PUT or DELETE pinned and expecting the pin, a POST asserting its body"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: AgentIsIssuerAndSubjectIri
    statement: "The commit agent is the IRI of the token's issuer, a hash, and the subject percent-encoded to RFC 3986 unreserved characters, the subject being oid for Entra and sub otherwise"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: AnonymousHasNoAgent
    statement: "In anonymous mode a commit has no agent"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: CauseIsTheRequestId
    statement: "The commit cause is the request's TraceIdentifier as an xsd:string literal, echoed as Varve-Request-Id"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: PositionOnEveryLogResponse
    statement: "Every response that touched the log carries Varve-Position and an ETag of that position"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: OutcomeStatusCodes
    statement: "Committed and NoChange are 204, or 201 for a created graph; Conflict 409; Rejected 422 with the report; Unavailable 503 with Retry-After"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: IfMatchIsTheExpectedPosition
    statement: "If-Match carries the expected position: a mismatch with the head is 412 before anything is read, and a match becomes the commit's expected position"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: UpdateExpectedPosition
    statement: "Varve.Sparql.Store's UpdateOptions gains ExpectedPosition, which returns Conflict without evaluation when the pin is elsewhere"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: TraceIdIsNeverTheCause
    statement: "W3C Trace Context is honoured and propagated and never written into the log; the cause stays the server-minted request id because a trace id is client-chosen and spans several requests; amended 2026-10-09 under ADR 0119"
---

The rulings of [ADR 0094](../adr/0094-a-write-over-http-is-one-commit.md), filed unaccepted by milestone 7a of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
