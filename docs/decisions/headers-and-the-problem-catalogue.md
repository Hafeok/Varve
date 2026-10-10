---
set: headers-and-the-problem-catalogue
namespace: varve
adr: 0119
decisions:
  - key: VaryOnEveryDatasetResponse
    statement: "Every dataset response carries Vary: Accept, Varve-As-Of, Authorization, problems included"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: CacheControlByReadKind
    statement: "An as-of read at a closed position is private, max-age=31536000, immutable; a head read is no-cache with its ETag; a live tail and the admin endpoints are no-store"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: LastModifiedIsTheCommitTimestamp
    statement: "Last-Modified is the resolved position's commit timestamp and If-Modified-Since is honoured before any pin is taken"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: LinkServiceDescAndNext
    statement: "Every dataset response carries Link rel=service-desc, and a bounded commits range cut by the page size carries Link rel=next"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: TraceContextNeverTheCause
    statement: "traceparent is honoured and propagated and never written into the log; cause stays the server-minted request id, because a trace id is client-chosen and spans several requests"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: BearerErrorCodesPerRfc6750
    statement: "401 carries WWW-Authenticate: Bearer error=invalid_token and 403 error=insufficient_scope, verified against what JwtBearer emits, with short descriptions that leak nothing; every 405 carries Allow"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: ProblemCatalogueFixesTitleStatusAndMembers
    statement: "ProblemCatalogue fixes each problem type's title, status and extension members, the writer emits only from it, instance is the request id, and every non-2xx is application/problem+json with 401 deliberately thin"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: OnePagePerProblemType
    statement: "Each problem type has a page in docs/problems/ stating its title, status, members and when it is emitted, which its IRI under https://w3id.org/varve/problems/ resolves to"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: ProblemShapeBoundaryPrimitives
    statement: "ProblemShape.Status is the int status code HttpResponse.StatusCode takes, Title and Name are the display text and page name a person reads, and ProblemCatalogue.Write takes the request id and the detail as the strings the response carries; none is compared or routed on, so they stay primitives at the HTTP boundary"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: CatalogueAndImmutabilityTested
    statement: "A test enumerates every problem the server can emit and asserts it is in the catalogue with exactly its declared members and its page; a property asserts two as-of reads at one closed position return byte-identical bodies and validators"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
---

The rulings of [ADR 0119](../adr/0119-headers-and-the-problem-catalogue.md), filed unaccepted by milestone Operability of #12
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
