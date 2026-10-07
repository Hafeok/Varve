---
set: datasets-are-the-routing-unit
namespace: varve
adr: 0093
decisions:
  - key: DatasetRoutes
    statement: "One server hosts many datasets, each under /datasets/{name}/ with its sparql, graphs, feed, diff and status endpoints"
  - key: DatasetNameIsAPathSegment
    statement: "A dataset name is 1 to 63 characters of [A-Za-z0-9._-] starting with a letter or digit, and an invalid or unknown name is the same 404"
  - key: NameIsTheHostsIdIsTheStores
    statement: "The name is the host's and maps to a directory under the configured root; DatasetId stays the store's"
  - key: DatasetsCreatedByConfiguration
    statement: "Datasets are created by configuration or the admin API, never by a request to a protocol endpoint"
  - key: DirectGraphIdentificationByRequestAddress
    statement: "Direct graph identification takes the request's own scheme, host and path, behind a proxy only through configured forwarded headers"
---

The rulings of [ADR 0093](../adr/0093-datasets-are-the-routing-unit.md), filed unaccepted by milestone 7a of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
