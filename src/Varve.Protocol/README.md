# Varve.Protocol

The SPARQL 1.1 Protocol, the Graph Store HTTP Protocol, the service
description, time travel, the change feed and the diff, for a Varve store, as
ASP.NET Core endpoint groups any host mounts.

- **One endpoint group per dataset.** `MapVarveDataset` maps the data plane,
  `/`, `/sparql`, `/graphs`, `/commits`, `/diff` and `/events`, under a
  prefix the host chooses. The
  host says which dataset a request names (`IDatasetResolver`), how an update
  executes (`ISparqlUpdateExecutor`), and who the caller is (`ICallerIdentity`).
- **Every write is one commit**, with the caller as its agent. `If-Match`
  carries an expected position: `412` when it is stale, `409` on a conflict.
  On a Graph Store write it names the graph's version, so writers of other
  graphs never conflict; `Varve-If-Graphs-Match: <iri>=<n>, default=<n>`
  names the graphs an update read (ADR 0126). A `409` lists the commits that
  caused it under `graphs`.
- **Every read is pinned for its response** and bounded by the host's limits.
  `Varve-As-Of: position:<n>` or `time:<RFC 3339>` reads the past.
  `Varve-Position` names the position read, and `ETag` the position or, for a
  graph, the graph's version; `If-None-Match` polls for `304`.
- **Graphs in six formats** (ADR 0092, amended): a Graph Store `GET`, a
  `CONSTRUCT` or `DESCRIBE`, and the service description are negotiated over
  N-Triples (the default), Turtle, N-Quads, TriG, RDF/XML and JSON-LD, and a
  Graph Store write reads all six. RDF 1.2's triple terms and directions
  travel where the syntax has them; a graph the chosen format cannot spell —
  a triple term in JSON-LD, a predicate with no qualified name in RDF/XML —
  is `406 not-acceptable`, saying why.
- **The commits resource** (ADR 0118) is the change feed: `GET /commits?from=&to=`
  is a range in `application/vnd.varve.delta; version=1`, `GET /commits/{position}`
  is one commit, and an open range tails live, as server-sent events when
  `Accept` asks for `text/event-stream`. The format is `Varve.Delta`'s, read
  for ever (ADR 0128): its `ChangeFeedReader` reads it with Varve's own terms
  and no store.
- **The service description** says what the endpoint does: its languages
  and formats, the named graphs the caller may see, the event-sourced
  extensions under `https://w3id.org/varve/ns#` — `position`, the headers,
  `graphStore`, `commits`, `state`, `diff`, `status`, `settings`, `checkpoints` —
  and `sd:BasicFederatedQuery` when the host answers `SERVICE` (ADR 0104).
- **The management plane** (ADR 0163): `MapVarveManagement` mounts
  `/manage/datasets` and, under `/manage/datasets/{name}`, the dataset's
  declaration as TriG (`GET`, `PUT`, `PATCH`, `DELETE`), `settings`, `state`,
  `status`, `checkpoints` and `grants`; then `/manage/fleet`, `/graphs`,
  `/apply` and `/system`, over the host's `IManagementPlane`: the fleet's
  system dataset, the grants in force, and the code validator every
  management commit runs (ADR 0166). A host with no management plane passes
  none, and gets the list, status, checkpoints, settings and state over its
  `IDatasetAdministration`.
- **Write routing by forwarding** (ADR 0168): with the host's
  `IWriterDirectory`, a write to a dataset another instance writes is sent
  to it whole, once, with the caller's token and `Varve-Forwarded`, and its
  answer passed back; a forwarded write that reaches a non-writer is
  `503 writer-moved`. `IManagementAuthority` pins the management position a
  request is authorised at, which its commit records (ADR 0165), and
  `IDatasetFollowing` advances a follower before a read; `Varve-Min-Position`
  asks for a later one.
- **Authorisation is four policy names**, `varve:read`, `varve:write`,
  `varve:admin` and `varve:server-admin` (an operator's). The host decides
  what satisfies them. Nothing here authenticates.
- **Graph-level scope** (ADR 0107): after the policy, the host's
  `IAccessScopes` answers what the caller reads and writes, by graph. A read
  evaluates over `GraphScopedQuadSource`, so an unreadable graph is absent
  from every answer — a `FROM` naming it names an empty graph, a Graph Store
  `GET`, `PUT` or `DELETE` of it is `404` — and a write that changes a graph
  outside the writable set is `403 graph-not-writable` with nothing
  committed. The feed and the diff are cut to the readable graphs. A caller
  whose scope is every graph is served by the unwrapped view.

```csharp
app.MapGroup("/datasets/{dataset}").MapVarveDataset(new ProtocolOptions
{
    Datasets = resolver,
    Updates = executor,
    Identity = identity,
    AccessScopes = EveryoneEverything.Instance,
    Authorization = app.Services.GetRequiredService<IAuthorizationService>(),
    Clock = TimeProvider.System,
});
```

Layer 5 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
