# The Varve vocabulary: `https://w3id.org/varve/ns#`

Every term Varve coins, with its meaning and where it appears (ADR 0092
point 6). The prefix is `varve:`; `https://w3id.org/varve/ns` redirects
here, and `https://w3id.org/varve/problems/<type>` redirects to the problem
type's page under [`docs/problems/`](problems/README.md), which this page
does not repeat.

The terms appear in two places. Since ADR 0130, the second is **system graphs**:
graphs named under this namespace that Varve's own components write as
ordinary data, under ordinary grants, queryable and time-travellable like any
graph (below). The first is **the service description** a dataset
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
| `varve:conditionalRequest` | a conditional-request header the service honours: `If-Match` on a write is its expected position (`412 precondition-failed` when the head moved), or on a Graph Store write the graph's version; `If-None-Match` on a read is the position or graph version the client holds (`304` when unchanged); `Varve-If-Graphs-Match` on an update names the graphs it read and their versions, `<iri>=<n>, default=<n>` (ADR 0126) | on the service, one literal per header |
| `varve:graphVersionETag` | `true`: a Graph Store response's `ETag` is the graph's version, the position of the last commit that touched it, and a write to one graph conflicts only with commits that touched it (ADRs 0125, 0126) | on the service, an `xsd:boolean` |

## The resources of a dataset (ADR 0118)

Each is the absolute IRI of the resource, under the dataset's base.

| Term | Meaning | Where |
|---|---|---|
| `varve:graphStore` | the Graph Store Protocol endpoint, `…/graphs` | on the service |
| `varve:commits` | the commits resource, `…/commits`: a range by position, a live tail as server-sent events, and `…/commits/{position}` for one commit | on the service |
| `varve:state` | the dataset's state resource on the management plane, `…/manage/datasets/{name}/state`: `{"state":"open"}` or `"closed"`, `GET` and idempotent `PUT`, a declaration every instance converges to (ADRs 0106, 0163) | on the service |
| `varve:diff` | the diff, `…/diff?from=&to=`: the effective change between two positions in the delta format | on the service |
| `varve:status` | the dataset's status on the management plane, `…/manage/datasets/{name}/status`: head, projection position and lag, checkpoints, settings (ADR 0163) | on the service |
| `varve:settings` | the dataset's declared settings on the management plane, `…/manage/datasets/{name}/settings`: `GET` as JSON with the declaration graph's version as `ETag`; `PUT` and `PATCH` take TriG, under `If-Match` (ADR 0163) | on the service |
| `varve:events` | the events resource, `…/events` (ADR 0143): the dataset's domain events as canonical CloudEvents, a range as `application/cloudevents-batch+json` or an open range as server-sent events | on the service |
| `varve:eventTypes` | the event types resource, `…/events/types` (ADR 0144), with each type's schemas by version and revision under it and the AsyncAPI document beside it, `…/events/asyncapi` | on the service |
| `varve:eventCatalogue` | an event catalogue graph the caller reads (ADR 0144); one triple per graph | on the service |
| `varve:checkpoints` | the checkpoints resource on the management plane, `…/manage/datasets/{name}/checkpoints`: `GET` lists, `POST` takes one at a position, by the dataset's writer (ADRs 0163, 0168) | on the service |
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

## System graphs (ADR 0130)

A graph named under `https://w3id.org/varve/ns#` is a system graph: Varve's
components write it, and it is ordinary data under ordinary grants. One rule
is special since ADR 0130's amendment of 2026-10-15: an event catalogue graph
is governed by its source graph, read with its read and written with its
write.

| Term | Meaning | Where |
|---|---|---|
| `varve:setup` | the graph a dataset's setup records its applied migrations in, one node each | a graph name; written by `Varve.Protocol.Client`'s `ApplyAsync` |
| `varve:Migration` | a migration of a dataset's setup that has been applied; the commit that asserted its node is the one that applied it, and the node is `https://w3id.org/varve/ns#setup/migration/{id}` | `rdf:type` of a node in `varve:setup` |
| `varve:migrationId` | the migration's id, as its setup names it, an `xsd:string` | on a `varve:Migration` |

## The event catalogue (ADR 0144)

Declarations of domain events, in catalogue graphs
`https://w3id.org/varve/ns#events-catalogue/{enc(G)}`, one per source graph
G: `enc(G)` is `default` for the default graph, or G's IRI percent-encoded as
one segment. Every node is an IRI. `docs/spec/events.md` §3.2 gives each
term's count and value. The Management plane (#105) takes the same terms over
as management declarations.

| Term | Meaning | Where |
|---|---|---|
| `varve:events-catalogue/…` | a catalogue graph, governed by its source graph | a graph name |
| `varve:EventType` | an event type's declaration | `rdf:type` of a type node |
| `varve:eventName` | the short name, the CloudEvents `type`, unique in the dataset | on a type node, an `xsd:string` |
| `varve:eventClass` | the class whose assertion in the source graph is the event, matched exactly | on a type node, an IRI |
| `varve:eventVersion` | the declared version, an `xsd:integer` of at least 1 | on a type node |
| `varve:eventSelect` | the `SELECT` over the event node, `?event` bound, whose projection is the fields | on a type node, an `xsd:string` |
| `varve:eventSubject` | the projected variable whose value is the CloudEvents `subject` | on a type node, an `xsd:string`, optional |
| `varve:eventRetired` | `true`: the type renders no new event; a declaration is retired, never removed | on a type node, optional |
| `varve:eventField` | a field of the type | on a type node, a field node |
| `varve:EventField` | a field's declaration | `rdf:type` of a field node |
| `varve:fieldName` | the `data` member and the projected variable | on a field node, an `xsd:string` |
| `varve:fieldDatatype` | the field's datatype, which fixes its JSON type | on a field node, an IRI |
| `varve:fieldRequired` | whether every event carries the field | on a field node, an `xsd:boolean` |
| `varve:fieldAs` | `"string"`: the member is always a JSON string | on a field node, optional |
| `varve:fieldKeepLanguage` | `true`: a language-tagged value is an object with its language and direction | on a field node, optional |

## Management declarations (ADR 0166)

The fleet's system dataset holds the management plane's declarations (ADR
0163): one graph per dataset, `varve:manage/dataset/{name}`, the fleet's
graph `varve:manage/fleet`, and `varve:manage/bootstrap`, which only the
system writer records. Every node is an IRI. The code validator in
`Varve.Management` refuses anything else, by focus node and path.

| Term | Meaning | Where |
|---|---|---|
| `varve:manage/dataset/{name}` | a dataset's declaration graph | a graph name in the system dataset |
| `varve:manage/fleet` | the fleet's declarations: its operators | a graph name in the system dataset |
| `varve:manage/bootstrap` | the bootstrap operators the first system writer recorded | a graph name in the system dataset |
| `varve:Dataset` | a dataset's declaration | `rdf:type` of the dataset node |
| `varve:datasetName` | the dataset's name, equal to its graph's `{name}` | on the dataset node, an `xsd:string` |
| `varve:storage` | `varve:File`, `varve:Memory` or `varve:ObjectStorage` | on the dataset node |
| `varve:placedOn` | the instance that writes the dataset; the system writer's when absent (ADR 0163 point 12) | on the dataset node, an `xsd:string` |
| `varve:defaultAccessScope` | `varve:AllHistory` or `varve:Current`, which the dataset's writer converges its settings to (ADR 0021) | on the dataset node |
| `varve:automaticHandover` | the standby opt-in, recorded for Replication | on the dataset node, an `xsd:boolean` |
| `varve:closed` | the dataset's declared state: every instance closes a closed dataset, and only a closed one is deleted | on the dataset node, an `xsd:boolean` |
| `varve:Grant` | a grant | `rdf:type` of a grant node |
| `varve:grantee` | the one principal a grant names | on a grant |
| `varve:permission` | `varve:read`, `varve:write`, `varve:admin`, or `varve:operator` in the fleet graph alone | on a grant |
| `varve:allGraphs` | `true`: every graph | on a read or write grant |
| `varve:graph` | a graph the grant reaches, or `varve:defaultGraph` | on a grant, repeatable |
| `varve:graphPrefix` | every named graph whose IRI starts with it | on a grant, an `xsd:string`, repeatable |
| `varve:graphTemplate` | a `varve:GraphTemplate` expanded for the caller | on a grant, repeatable |
| `varve:GraphTemplate` | an RFC 6570 level 1 template over `{sub}`, `{agent}` and `{claim.<name>}` | `rdf:type` of a template node |
| `varve:template` | the template | on a template node, an `xsd:string` |
| `varve:match` | `varve:Exact` or `varve:Prefix` | on a template node |
| `varve:Agent` | a principal that is the agent IRI itself, `<issuer>#<subject>` | `rdf:type` of a principal |
| `varve:Role` | a principal matched against the server's role claim | `rdf:type` of a principal |
| `varve:Group` | a principal matched against `varve:claimType`'s claim, `groups` by default | `rdf:type` of a principal |
| `varve:claimValue` | the claim value a role or group matches | on a principal, an `xsd:string` |
| `varve:claimType` | the claim a group's value is in | on a group, an `xsd:string` |
| `varve:ShapesBinding`, `varve:TBoxBinding` | a binding of a graph of shapes or of a TBox, recorded for SHACL and Inference | `rdf:type` of a binding node |
| `varve:boundGraph` | the graph a binding names | on a binding node |
| `varve:bootstrapOperator` | an agent IRI or role value the system writer recorded as a bootstrap operator | in `varve:manage/bootstrap` |
| `varve:managementPosition` | the datatype of the management position a commit records as an attachment, `"p"^^varve:managementPosition` (ADR 0165) | a datatype IRI, in a commit's header |

Reserved, and refused until their milestones: `varve:Binding` (abstract),
`varve:ClassifierBinding`, `varve:KeyStoreBinding` and `varve:erasureMode`
(#14). The event catalogue's terms join the management graphs in part 2 of
the milestone (ADR 0167); until then they are refused there.

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
(2026-10-09); nothing writes the old term. `varve:state`, `varve:status`,
`varve:settings` and `varve:checkpoints` keep their meaning and name the
management plane's resources since ADR 0163 (2026-10-19).
