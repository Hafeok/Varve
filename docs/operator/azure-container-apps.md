# Run on Azure Container Apps

A Varve server on Azure Container Apps with its datasets in Blob Storage
(ADRs 0134–0139, 0142): the log, the checkpoints' extents and the catalog in a
container, the replica's disk only a cache. No key, SAS token or connection
string exists anywhere in it.

## Deploy

`deploy/azure/container-apps/main.bicep` creates, in one resource group, a
Log Analytics workspace, a storage account that refuses shared-key access, a
private container `datasets`, a Container Apps environment, and the app with
a system-assigned identity granted **Storage Blob Data Contributor** on that
container alone.

```sh
az group create --name varve --location westeurope
az deployment group create --resource-group varve \
  --template-file deploy/azure/container-apps/main.bicep \
  --parameters authority=https://login.microsoftonline.com/{tenant}/v2.0 audience=api://varve \
               image=ghcr.io/hafeok/varve@sha256:{digest} datasets='["people","orders"]'
```

The deploying identity needs to create role assignments on the resource group
(Owner, or User Access Administrator beside Contributor). The outputs are the
server's URL, the container's URL and the app's principal id.

| Parameter | Default | |
|---|---|---|
| `authority`, `audience` | — | `Varve:Auth` (ADR 0037); register the API as [Authenticate](authenticate.md) says |
| `image` | `ghcr.io/hafeok/varve:latest` | pin a digest in production |
| `datasets` | `["default"]` | each an `ObjectStorage` dataset under its own prefix |
| `storageSku` | `Standard_ZRS` | `Standard_ZRS` keeps a commit through a zone's loss; `Standard_LRS` is cheaper, one datacentre; `Standard_GZRS` adds a second region asynchronously |
| `cacheBytes` | 4 GiB | `Varve:ObjectStorage:CacheBytes`, within the replica's ephemeral storage |
| `cpu`, `memory` | `1.0`, `2Gi` | the replica's size |
| `minReplicas` | 1 | 0 scales to zero between requests |

**The first start can fail** until the role assignment has propagated, which
takes a few minutes: the configured datasets cannot be opened, the server
exits, and Container Apps restarts it until they can.

## One replica, and why

Opening a dataset for write claims its next writer epoch (ADR 0136). A second
replica would claim it in turn, and the first's next write would fail with
[`503 writer-superseded`](../problems/writer-superseded.md). So the template
sets `maxReplicas` to 1 and one active revision. A new revision is a clean
handover: it claims the epoch, the old revision is superseded, its
`/health/ready` turns false (`superseded`), and it leaves the load balancer
before it is stopped. Every claim is in the writer record:

```sh
varve lease https://{account}.blob.core.windows.net/datasets/people
```

which shows the epoch, its holder and every claim, each with its trigger —
`restart` for a replica's start, `operator` for a takeover. **A run of
`restart` claims alternating between two holders means two writers are
configured for one dataset**: two apps, or `maxReplicas` above 1, each start
superseding the other. One claim per revision rollover, each by the new
revision after a `shutdown` release by the old, is the healthy pattern. A
superseded instance never claims again on its own: it stays live and serves
reads, and only a fresh process start claims, which is why liveness stays
true while readiness reports `superseded`.
Should a writer have to be stopped from writing without being stopped,
`varve lease … --take --epoch n` takes epoch n over and releases it for the
next writer.

## Cold start and scale to zero

With `minReplicas` 0 a request after an idle period starts a replica. A start
reads the manifest, the writer record and the catalog, attaches each extent
lazily — its first and last 1 MiB pages, then only the pages reads touch —
and replays the log after the catalog's position (ADR 0139). It scans the
log's record headers from the start to verify the chain: one read per sealed
segment, and one per chunk of a segment not yet sealed. That last term grows
with the open segment: at REC size M on loopback a cold start made 19,790
reads and took 23–29 s, so a dataset with a long open segment is better kept
at `minReplicas` 1. The cost on a real account is **to be measured by the
maintainer** with the optional leg (`azure-objects.yml`); the figures for
Azurite and RustFS at S and M are in the milestone's record and
`tests/Varve.Benchmarks/README.md`.

## Checkpoints

A checkpoint writes an extent set for the window since the previous one and
swaps the catalog; a follower or the next replica finds it there. Reads at
any position below the newest checkpoint are served from the extents with no
log read. The chain grows by a set per checkpoint until milestone Extent
maintenance merges sets in the background; until then keep it short with
`Checkpoints:Keep`, which merges the oldest set into the next. The read
latency after 10, 50 and 100 checkpoints, and the budget it implies, are in
[Back up and restore](backup-and-restore.md#checkpoints).

## Costs

Every commit is one write (a chunk) per group-commit batch; a checkpoint is
eight writes per set and one catalog replace; reads are ranged reads of 1 MiB
pages on a cache miss. Blob Storage bills writes and reads per 10,000 and
storage per GiB-month. The monthly figure for a given workload is **to be
measured by the maintainer**.

## Pairing with an Eventhouse

Telemetry and the state it is about go to different stores (ADR 0140): the
readings to an Eventhouse in Fabric Real-Time Intelligence, the buildings,
points and leases to Varve.

1. **Varve to Event Hubs.** A small follower — any process following the
   commits resource with `FollowAsync` and its own checkpoint
   ([Integrate](integrate.md)) — publishes each commit to an Event Hub, with
   the position as the event's id. Delivery is at least once, so a consumer
   drops a position it has seen. Varve has no Event Hubs sink of its own; the
   follower is the application's, and a CloudEvents rendering of the feed is
   the integration roadmap's.
2. **Event Hubs to the Eventhouse.** The Eventhouse ingests the hub as any
   other source; a KQL table of commits and a table of readings sit side by
   side.
3. **The join.** A reading carries its point's IRI, as the REC model's points
   are named. A KQL query joins readings to the commit stream by IRI, or asks
   Varve, as of the reading's time, which room and asset the point was in.
4. **Retention.** The Eventhouse's retention is its own. Varve's history moves
   only by an explicit act: a scheduled task outside Varve may issue it, and
   nothing in Varve moves because time passed (ADR 0140, N3).

