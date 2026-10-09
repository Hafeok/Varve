# The Varve vocabulary: `https://w3id.org/varve/ns#`

Every term Varve coins, with its meaning and where it appears (ADR 0092
point 6). The prefix is `varve:`; `https://w3id.org/varve/ns` redirects
here, and `https://w3id.org/varve/problems/<type>` redirects to the problem
type's page under [`docs/problems/`](problems/README.md), which this page
does not repeat.

The terms appear in one document: **the service description** a dataset
answers to `GET /datasets/{name}/sparql` (or `GET /datasets/{name}/`) with
an RDF `Accept`, as `sparql-service-description` triples about the endpoint
plus the extensions below (`src/Varve.Protocol/Endpoints/ServiceDescriptionEndpoint.cs`;
the shapes in `tests/fixtures/service-description/shapes.ttl` are what the
conformance harness checks it against). The change feed itself carries no
IRI of this namespace: its records are the keywords of
[`docs/spec/change-feed.md`](spec/change-feed.md), and the description names
its format by media type (`varve:feedFormat`).

## Classes

| Term | Meaning | Where |
|---|---|---|
| `varve:EventSourcedService` | the endpoint is a Varve dataset: an append-only log of commits with a readable position, time travel by position or time, a commits resource and a diff; the subject carrying every property below | `rdf:type` of the `sd:Service` |

## The position and the headers

| Term | Meaning | Where |
|---|---|---|
| `varve:position` | the dataset's head position at the moment the description was written, an `xsd:integer`; `0` is the empty dataset | on the service; every response carries the position it describes in `Varve-Position` and as its `ETag` |
| `varve:positionHeader` | the name of the header that carries a response's position: `Varve-Position` | on the service, a literal |
| `varve:asOfHeader` | the name of the request header that selects a past position or time: `Varve-As-Of` | on the service, a literal |
| `varve:asOfSelector` | a selector form `Varve-As-Of` accepts; the service lists each it supports | on the service, one triple per selector |
| `varve:PositionSelector` | the selector `position:<n>`: a closed position of this dataset | a value of `varve:asOfSelector` |
| `varve:TimeSelector` | the selector `time:<RFC 3339>`: the greatest position whose commit timestamp is at or before the instant | a value of `varve:asOfSelector` |
| `varve:conditionalRequest` | a conditional-request header the service honours: `If-Match` on a write is its expected position (`412 precondition-failed` when the head moved), `If-None-Match` on a read is the position the client holds (`304` when unchanged) | on the service, one literal per header |

## The resources of a dataset (ADR 0118)

Each is the absolute IRI of the resource, under the dataset's base.

| Term | Meaning | Where |
|---|---|---|
| `varve:graphStore` | the Graph Store Protocol endpoint, `…/graphs` | on the service |
| `varve:commits` | the commits resource, `…/commits`: a range by position, a live tail as server-sent events, and `…/commits/{position}` for one commit | on the service |
| `varve:state` | the dataset's state resource, `…/state`: `{"state":"open"}` or `"closed"`, `GET` and idempotent `PUT` (ADR 0106) | on the service |
| `varve:diff` | the diff, `…/diff?from=&to=`: the effective change between two positions in the delta format | on the service |
| `varve:status` | the dataset's status, `…/status`: head, projection position and lag, checkpoints, settings | on the service |
| `varve:settings` | the dataset's settings resource, `…/settings`: `GET` with an `ETag`, `PUT` whole, `PATCH` as a merge patch, under `If-Match` | on the service |
| `varve:checkpoints` | the checkpoints resource, `…/checkpoints`: `GET` lists, `POST` takes one at a position | on the service |
| `varve:feedFormat` | a media type the commits resource and the diff write: `application/vnd.varve.delta; version=1` (the delta format) and `text/event-stream` (the live tail's framing); one triple per format | on the service, literals |

## Features

| Term | Meaning | Where |
|---|---|---|
| `varve:VersionParameter` | the endpoint takes the `version` parameter and the `version=` media-type parameter of the SPARQL 1.2 Protocol draft (`1.1`, `1.2-basic`, `1.2`), the parameter winning over a `VERSION` declaration (ADR 0092 point 4) | a value of `sd:feature`; `sd:BasicFederatedQuery` beside it when the host answers `SERVICE` (ADR 0104) |

## The limits a client should know (ADR 0114)

Each an `xsd:integer`, the server's effective value.

| Term | Meaning | Where |
|---|---|---|
| `varve:resultSizeCap` | the most bytes one read answers before it is cut (`result-too-large`): `Varve:Limits:ResultSizeCap` | on the service |
| `varve:maxAsOfDistance` | how many positions below the head a `Varve-As-Of` read may go when no checkpoint is at or below the position (`as-of-distance-exceeded`): `Varve:Limits:MaxAsOfDistance` | on the service |
| `varve:commitsPageSize` | how many commits a `GET …/commits` range answers before a `Link rel="next"`: `Varve:Limits:CommitsPageSize` | on the service |

## Names that share the prefix but are not terms

- **The authorisation policies** `varve:read`, `varve:write`, `varve:admin`
  and `varve:server-admin` (`DatasetPermissions`) are ASP.NET Core policy
  names a host registers (ADR 0091); they are strings, not IRIs, and no
  document carries them.
- **The problem types** are IRIs under `https://w3id.org/varve/problems/`,
  one page each in [`docs/problems/`](problems/README.md) (ADR 0119).
- **The media type** `application/vnd.varve.delta` is a vendor tree media
  type, not an IRI; `docs/spec/change-feed.md` defines it.

## Changes

A term is added by the ADR that needs it and listed here in the same
change; a term is never redefined, and one no longer written stays here
marked as such, so that an old description still reads. `varve:changeFeed`
was the commits resource's name until ADR 0118 renamed it `varve:commits`
(2026-10-09); nothing writes the old term.
