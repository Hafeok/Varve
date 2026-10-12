---
set: time-travel-over-http
namespace: varve
adr: 0096
decisions:
  - key: AsOfHeader
    statement: "A read may carry Varve-As-Of as position:n or time:RFC 3339 normalised to UTC with at most seven fractional digits; on a write it is 400"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: AsOfTimeResolvesByI5
    statement: "An as-of time resolves to the greatest position whose timestamp is at or before it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: UnresolvableAsOfIs404
    statement: "A time before the first commit, a position after the head and a position below the archive horizon are 404, each with its own problem type"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: ETagIsThePosition
    statement: "Every response that touched a dataset carries Varve-Position and a strong ETag of the head or the resolved position, with Vary on Accept and Varve-As-Of"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: IfNoneMatchBeforePin
    statement: "If-None-Match on a read is answered with 304 before any pin is taken when it equals the position the read would describe"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: TimeTravelAdvertised
    statement: "The service description advertises the headers, both selector forms and the conditional requests"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: CacheHeadersByReadKind
    statement: "An as-of read at a closed position is private, max-age=31536000, immutable, a head read no-cache with its ETag, Last-Modified is the commit timestamp and If-Modified-Since is honoured before any pin; 410 below the horizon was reversed and 404 stands; amended 2026-10-09 under ADR 0119"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
---

The rulings of [ADR 0096](../adr/0096-time-travel-over-http.md), filed unaccepted by milestone 7a of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
