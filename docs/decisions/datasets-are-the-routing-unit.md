---
set: datasets-are-the-routing-unit
namespace: varve
adr: 0093
decisions:
  - key: DatasetRoutes
    statement: "One server hosts many datasets, each under /datasets/{name}/ with its sparql, graphs, feed, diff and status endpoints"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: DatasetNameIsAPathSegment
    statement: "A dataset name is 1 to 63 characters of [A-Za-z0-9._-] starting with a letter or digit, and an invalid or unknown name is the same 404"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: NameIsTheHostsIdIsTheStores
    statement: "The name is the host's and maps to a directory under the configured root; DatasetId stays the store's"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: DatasetsCreatedByConfiguration
    statement: "Datasets are created by configuration or the admin API, never by a request to a protocol endpoint"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: DirectGraphIdentificationByRequestAddress
    statement: "Direct graph identification takes the request's own scheme, host and path, behind a proxy only through configured forwarded headers"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
---

The rulings of [ADR 0093](../adr/0093-datasets-are-the-routing-unit.md), filed unaccepted by milestone 7a of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
