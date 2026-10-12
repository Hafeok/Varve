# Varve.Protocol

The SPARQL 1.1 Protocol, the Graph Store HTTP Protocol, the service
description, time travel, the change feed and the diff, for a Varve store, as
ASP.NET Core endpoint groups any host mounts.

- **One endpoint group per dataset.** `MapVarveDataset` maps `/`, `/sparql`,
  `/graphs`, `/commits`, `/diff`, `/status`, `/settings` and `/checkpoints`
  under a prefix the host chooses. The
  host says which dataset a request names (`IDatasetResolver`), how an update
  executes (`ISparqlUpdateExecutor`), and who the caller is (`ICallerIdentity`).
- **Every write is one commit**, with the caller as its agent. `If-Match`
  carries an expected position: `412` when it is stale, `409` on a conflict.
- **Every read is pinned for its response** and bounded by the host's limits.
  `Varve-As-Of: position:<n>` or `time:<RFC 3339>` reads the past.
  `Varve-Position` and `ETag` name the position read, and `If-None-Match` polls
  for `304`.
- **The commits resource** (ADR 0118) is the change feed: `GET /commits?from=&to=`
  is a range in `application/vnd.varve.delta; version=1`, `GET /commits/{position}`
  is one commit, and an open range tails live, as server-sent events when
  `Accept` asks for `text/event-stream`. `ChangeFeedReader` reads it with
  Varve's own terms.
- **The service description** says what the endpoint does: its languages
  and formats, the named graphs the caller may see, the event-sourced
  extensions under `https://w3id.org/varve/ns#` — `position`, the headers,
  `graphStore`, `commits`, `state`, `diff`, `status`, `settings`, `checkpoints` —
  and `sd:BasicFederatedQuery` when the host answers `SERVICE` (ADR 0104).
- **The admin API** (ADRs 0106, 0118): `MapVarveAdministration` mounts
  `GET /datasets`, `PUT` and `DELETE` of `/datasets/{name}`, and `GET` and
  `PUT` of `/datasets/{name}/state` (`open` or `closed`, idempotent), over the
  host's `IDatasetAdministration`; each dataset group has `/settings` as a
  resource (`GET` with the position as `ETag`, `PUT` replaces, `PATCH`
  merges, both under `If-Match`, each a `Settings` commit) and
  `POST /checkpoints`, and `/status` reports the projection.
- **Authorisation is four policy names**, `varve:read`, `varve:write`,
  `varve:admin` and `varve:server-admin`. The host decides what satisfies
  them. Nothing here authenticates.
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
