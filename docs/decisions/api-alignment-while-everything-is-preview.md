---
set: api-alignment-while-everything-is-preview
namespace: varve
adr: 0118
decisions:
  - key: CommitsAreTheResource
    statement: "GET /datasets/{name}/commits with from and to is the range in the delta format, GET /datasets/{name}/commits/{position} is one commit, and Accept text/event-stream on an open range tails live; /feed is removed and /diff stays"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: OldPathsRemovedNotAliased
    statement: "The old paths are removed, not aliased, and the change is listed under breaking changes in the release descriptor's summary"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
  - key: HorizonStays404
    statement: "A read below the archive horizon stays 404 below-archive-horizon as ADR 0096 decided, because RFC 9110 reserves 410 for conditions likely to be permanent and archiving is undecided"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-10T00:00:00Z
---

The rulings of [ADR 0118](../adr/0118-api-alignment-while-everything-is-preview.md), filed unaccepted by milestone Operability of #12
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.

Moved to a later set: `StateIsAResource`, `SettingsAreAResource` and `PutDatasetIsIdempotent` are superseded by ADR 0163, which moves those resources under `/manage/`, in its set.
