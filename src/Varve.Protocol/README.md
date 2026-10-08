# Varve.Protocol

The SPARQL 1.1 Protocol, the Graph Store HTTP Protocol, the service
description, time travel, the change feed and the diff, for a Varve store, as
ASP.NET Core endpoint groups any host mounts.

- **One endpoint group per dataset.** `MapVarveDataset` maps `/`, `/sparql`,
  `/graphs`, `/feed`, `/diff` and `/status` under a prefix the host chooses. The
  host says which dataset a request names (`IDatasetResolver`), how an update
  executes (`ISparqlUpdateExecutor`), and who the caller is (`ICallerIdentity`).
- **Every write is one commit**, with the caller as its agent. `If-Match`
  carries an expected position: `412` when it is stale, `409` on a conflict.
- **Every read is pinned for its response** and bounded by the host's limits.
  `Varve-As-Of: position:<n>` or `time:<RFC 3339>` reads the past.
  `Varve-Position` and `ETag` name the position read, and `If-None-Match` polls
  for `304`.
- **The change feed** is `application/vnd.varve.delta; version=1`, or
  server-sent events for a live tail. `ChangeFeedReader` reads it with Varve's
  own terms.
- **The admin API** (ADR 0105): `MapVarveAdministration` mounts
  `GET /datasets`, `PUT` and `DELETE` of `/datasets/{name}`, and
  `POST …/open` and `…/close`, over the host's `IDatasetAdministration`; each
  dataset group has `POST /settings` (a `Settings` commit) and
  `POST /checkpoints`, and `/status` reports the projection.
- **Authorisation is four policy names**, `varve:read`, `varve:write`,
  `varve:admin` and `varve:server-admin`. The host decides what satisfies
  them. Nothing here authenticates.

```csharp
app.MapGroup("/datasets/{dataset}").MapVarveDataset(new ProtocolOptions
{
    Datasets = resolver,
    Updates = executor,
    Identity = identity,
    Authorization = app.Services.GetRequiredService<IAuthorizationService>(),
    Clock = TimeProvider.System,
});
```

Layer 5 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.
