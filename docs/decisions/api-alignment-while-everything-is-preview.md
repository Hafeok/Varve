---
set: api-alignment-while-everything-is-preview
namespace: varve
adr: 0118
decisions:
  - key: StateIsAResource
    statement: "GET and PUT /datasets/{name}/state replace POST open and close; PUT of open or closed is idempotent and answers 204 whether or not anything changed"
  - key: SettingsAreAResource
    statement: "GET /datasets/{name}/settings answers the settings with the position as ETag; PUT replaces and PATCH merges under If-Match, the Settings commit being the effect; POST settings is removed"
  - key: CommitsAreTheResource
    statement: "GET /datasets/{name}/commits with from and to is the range in the delta format, GET /datasets/{name}/commits/{position} is one commit, and Accept text/event-stream on an open range tails live; /feed is removed and /diff stays"
  - key: PutDatasetIsIdempotent
    statement: "PUT /datasets/{name} with the same body again is 204 and with a different body is 409 dataset-exists"
  - key: OldPathsRemovedNotAliased
    statement: "The old paths are removed, not aliased, and the change is listed under breaking changes in the release descriptor's summary"
  - key: HorizonStays404
    statement: "A read below the archive horizon stays 404 below-archive-horizon as ADR 0096 decided, because RFC 9110 reserves 410 for conditions likely to be permanent and archiving is undecided"
---

The rulings of [ADR 0118](../adr/0118-api-alignment-while-everything-is-preview.md), filed unaccepted by milestone Operability of #12
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
