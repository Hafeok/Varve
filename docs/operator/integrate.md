# Integrate: the commits resource as a stream, and domain events

The commits resource (`GET /datasets/{name}/commits`, ADRs 0097 and 0118) is
the contract another system integrates against. It has every property
integration needs:

- **a total order per dataset:** positions 1, 2, 3, … and nothing between;
- **resumable by position:** `from`, or `Last-Event-ID` on the live tail;
- **at least once:** a reader that resumes from where it was never misses a
  commit;
- **byte-deterministic:** a commit's record is the same bytes every time it is
  read;
- **retained for ever:** the log is never rewritten, so a consumer may replay
  from position 0 at any time;
- **filterable by graph:** `graph=`, so a consumer reads the part it needs.

Its format, `application/vnd.varve.delta; version=1`
([`change-feed.md`](../spec/change-feed.md)), **is read for ever** (ADR
0128). Every later release reads a version-1 document with the same meaning,
and every release names the version it writes (`delta-format`). `Varve.Delta`
reads and writes it, with no store in it.

## Following: `FollowAsync`

```csharp
SparqlHttpClient client = /* the dataset, with its token provider */;
IFollowCheckpoint checkpoint = new FileFollowCheckpoint("orders.position");

await client.FollowAsync(new FeedPosition(0), async (commit, cancellationToken) =>
{
    // One commit: commit.Position, commit.Timestamp, commit.Agent, commit.Changes.
    await PublishAsync(commit, cancellationToken);
}, checkpoint, new FollowOptions(TimeProvider.System) { Graph = "https://shop.example/events" }, stoppingToken);
```

**How it follows:**

- It reads history up to the head as paged ranges (`Link: rel="next"`, ADR
  0114), then the live tail as server-sent events.
- It hands each commit to the handler in order.
- **It saves the position only after the handler returns.**
- It waits out the following and resumes from the checkpoint:
  - a dropped connection;
  - a `shutdown` or `error` event;
  - `429`, `503` and other `5xx`;
  - a tail silent past `IdleTimeout`.
- Any other refusal ends it, with the problem (ADR 0129).

**The contract is at least once.** A crash between the handler's return and the
checkpoint's save hands that commit over again on restart. So a handler is
idempotent by position. A consumer that saves the position **in the same
transaction** as its effect gets exactly once in effect. Examples:

- a database row updated with the projection it feeds;
- a quad in the consumer's own Varve dataset, written in the same update as
  the data it derives.

**Grants apply** (ADR 0107). A commit the follower may not read is not
delivered, so positions may have gaps, and a settings or erasure commit reaches
only a dataset administrator.

The checkpoint is the caller's: `IFollowCheckpoint` has `LoadAsync` and
`SaveAsync`. `FileFollowCheckpoint` replaces its file atomically.

## Domain events

A system that does not speak RDF wants named facts in plain JSON: *an order
was placed*, with its total. Varve gives it those without a second source of
truth (ADRs 0143–0145, [`events.md`](../spec/events.md)).

1. **The publisher writes the event as quads**, into an events graph, **in the
   same commit** as the change it describes. The event is atomic with the
   data, and the feed filtered to the events graph is already the domain
   stream, in RDF.
2. **A declaration**, also data, in the events graph's catalogue graph, names
   the event's class, a short name, a version, and a SPARQL `SELECT` whose
   variables become the JSON fields.
3. **Varve renders** each event as a CloudEvent 1.0, structured JSON, in RFC
   8785 canonical form, with the declaration in force at the commit's
   position. The same commit always renders to the same bytes.

### One type, end to end

**The declaration**, written once by a writer of the events graph. The
catalogue graph of `https://shop.example/orders` is that IRI percent-encoded
under `https://w3id.org/varve/ns#events-catalogue/`. A caller reads and
writes it exactly when it reads and writes the events graph.

```sparql
PREFIX varve: <https://w3id.org/varve/ns#>
PREFIX xsd: <http://www.w3.org/2001/XMLSchema#>
INSERT DATA {
  GRAPH <https://w3id.org/varve/ns#events-catalogue/https%3A%2F%2Fshop.example%2Forders> {
    <https://shop.example/types/OrderPlaced> a varve:EventType ;
      varve:eventName "OrderPlaced" ;
      varve:eventClass <https://shop.example/ns#OrderPlaced> ;
      varve:eventVersion 1 ;
      varve:eventSubject "order" ;
      varve:eventField <https://shop.example/types/OrderPlaced/total> ,
                       <https://shop.example/types/OrderPlaced/placedAt> ;
      varve:eventSelect """
        PREFIX shop: <https://shop.example/ns#>
        SELECT ?order ?total ?placedAt WHERE {
          ?event shop:order ?order ; shop:total ?total ; shop:placedAt ?placedAt .
        }""" .
    <https://shop.example/types/OrderPlaced/total> a varve:EventField ;
      varve:fieldName "total" ; varve:fieldDatatype xsd:decimal ; varve:fieldRequired true .
    <https://shop.example/types/OrderPlaced/placedAt> a varve:EventField ;
      varve:fieldName "placedAt" ; varve:fieldDatatype xsd:dateTime ; varve:fieldRequired true .
  }
}
```

A declaration that is not well formed is refused with
[`event-declaration-invalid`](../problems/event-declaration-invalid.md). A
change that would break a consumer is refused with
[`event-schema-incompatible`](../problems/event-schema-incompatible.md)
unless it raises the version. Breaking changes are removing a field, changing
its JSON type, or making it required. Adding an optional field keeps the
version.

**The commit**: the order and its event, together.

```sparql
PREFIX shop: <https://shop.example/ns#>
PREFIX xsd: <http://www.w3.org/2001/XMLSchema#>
INSERT DATA {
  GRAPH <https://shop.example/data> {
    <https://shop.example/orders/1002> shop:status "placed" ; shop:total 1250.00 .
  }
  GRAPH <https://shop.example/orders> {
    <https://shop.example/events/1002-placed> a shop:OrderPlaced ;
      shop:order <https://shop.example/orders/1002> ;
      shop:total 1250.00 ;
      shop:placedAt "2026-10-15T10:05:12Z"^^xsd:dateTime .
  }
}
```

**The CloudEvent** a consumer receives from
`GET /datasets/shop/events?from=0`, one line, members sorted:

```json
{"data":{"placedAt":"2026-10-15T10:05:12Z","total":1250},"datacontenttype":"application/json","dataschema":"https://varve.shop.example/datasets/shop/events/types/OrderPlaced/versions/1/revisions/2","id":"3 https://shop.example/events/1002-placed","sequence":"00000000000000000003.000000","source":"https://varve.shop.example/datasets/shop","specversion":"1.0","subject":"https://shop.example/orders/1002","time":"2026-10-15T20:53:43.1613986Z","type":"OrderPlaced"}
```

- `id` is the position, a space, and the event node.
- `sequence` orders every event of the dataset as a string.
- `time` is the commit's timestamp.
- `dataschema` is the schema revision in force at that position.

**The schema** at `dataschema`, which is generated from the declaration, never
changes, and is served with `Cache-Control: immutable`:

```json
{"$id":"https://varve.shop.example/datasets/shop/events/types/OrderPlaced/versions/1/revisions/2","$schema":"https://json-schema.org/draft/2020-12/schema","additionalProperties":true,"properties":{"placedAt":{"format":"date-time","type":"string"},"total":{"type":"number"}},"required":["placedAt","total"],"title":"OrderPlaced","type":"object","x-varve-version":1}
```

**Numbers.** A number is written as a JSON number only when it survives a
round trip through a double exactly. `1250.00` does, and is written `1250`. A
value that does not survive the trip is not written as a number:

- `9007199254740993`, which is 2^53 + 1;
- a decimal with more digits than a double holds.

Such an event is not rendered: a problem stands in its place unless the field
is declared `varve:fieldAs "string"`. A problem is a CloudEvent of type
[`event-not-rendered`](../problems/event-not-rendered.md), in the event's
place, with the reason. A consumer never gets a guessed value, and never
misses one silently.

### Where to read them

| Route | What |
|---|---|
| `GET /datasets/{name}/events?from=&to=&type=` | a range, `application/cloudevents-batch+json`, paged with `Link: rel="next"`; `text/event-stream` for an open range, one SSE event per commit, resumed by `Last-Event-ID` |
| `GET /datasets/{name}/events/types` | the dataset's types, with every version and revision |
| `GET /datasets/{name}/events/types/{type}[/versions/{n}[/revisions/{r}]]` | a schema; the unversioned address answers the current one with `Content-Location` |
| `GET /datasets/{name}/events/asyncapi` | an AsyncAPI 3.0 document, with one channel per type |
| `GET /events/types` | every type of every dataset the caller reads |

Every view honours `Varve-As-Of`, so "which events did this dataset expose
then" is a request. The catalogue graphs are also data: a SPARQL query over
them answers the same question, and the three views agree.

**Grants apply.** A caller who cannot read the events graph cannot read its
catalogue either. It sees the graph's types in none of the views, and gets
none of their events.

**Configuration.** `Varve:Events:Source` fixes the base of `source`,
`dataschema` and the schema `$id`s. Without it the base is the request's, so
**byte identity holds per base**. A deployment reached under more than one
host name sets the option. `Varve:Events:EventHubs:Host` and `Hub` add an
Event Hubs server to the AsyncAPI document. See
[Configure](configure.md#domain-events--events-adrs-0143-0144).

### Following: `FollowEventsAsync`

`FollowEventsAsync` is `FollowAsync`'s sibling over the events resource, with
the same contract:

- at least once;
- the position saved after the handler returns;
- transient failures waited out.

It hands over each commit's events, `CommitEvents`. Each event's `Json` holds
the canonical bytes exactly as the server rendered them. Forward those bytes,
never a re-serialisation.

```csharp
await client.FollowEventsAsync(new FeedPosition(0), async (commit, cancellationToken) =>
{
    foreach (ReceivedEvent e in commit.Events)
    {
        // e.Id, e.Type, e.Sequence, e.IsProblem, e.Json
        await PublishAsync(e.Json, e.Id, cancellationToken);
    }
}, new FileFollowCheckpoint("orders-events.position"), new FollowOptions(TimeProvider.System), stoppingToken);
```

### Publishing to Azure Event Hubs

[`samples/Varve.EventHubs.Sample`](../../samples/Varve.EventHubs.Sample) is
such a follower. Each event is sent with:

- the structured CloudEvent's bytes as the body;
- `application/cloudevents+json` as the content type;
- the event's `id` as the message id;
- the dataset's name as the partition key.

So the dataset's events land in one partition, in commit order. A commit's
events are sent before its position is saved. A bridge stopped between the two
sends the commit again when it resumes, so consumers drop duplicates by
message id.

CI runs the sample against the Event Hubs emulator
([`.github/eventhubs/compose.yaml`](../../.github/eventhubs/compose.yaml)).
Its test stops and resumes the bridge at every position, and checks that every
event arrives at least once, in order, in one partition.

### What a replay costs

Rendering an event reads its closure as of its own position. That read
replays the log from the nearest store checkpoint below the position, so the
cost per event grows with the distance from that checkpoint, not with the
size of the range. Measured in memory on a cloud container, 100 events at a
time:

| Distance from the nearest checkpoint | Per event |
|---:|---:|
| ~0 to 100 commits | 0.4–0.6 ms |
| ~1,000 commits | 9 ms |
| ~1,900 commits | 18 ms |

`Limits:MaxAsOfDistance` (10,000 by default) bounds how far that replay may
go. A range beyond it ends with the stream's `error` event. A checkpoint
(`POST /datasets/{name}/checkpoints`) brings the cost back down for the
positions above it. A consumer that follows the live tail pays the near-head
cost. A full replay of a long history pays the distance, until Object
storage's extents (ADR 0138) make an as-of read independent of it.
