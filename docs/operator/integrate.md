# Integrate: the commits resource as a stream

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
