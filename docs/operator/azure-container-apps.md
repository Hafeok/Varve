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
`restart` for a replica's start, `operator` for a takeover. Restarts alternate
between revisions in the history; that is a handover each time, not a fault.
Should a writer have to be stopped from writing without being stopped,
`varve lease … --take --epoch n` takes epoch n over and releases it for the
next writer.

## Cold start and scale to zero

With `minReplicas` 0 a request after an idle period starts a replica. A start
reads the manifest, the writer record and the catalog, attaches each extent
lazily — its first and last 1 MiB pages, then only the pages reads touch —
and replays the log after the catalog's position (ADR 0139). It scans the
log's record headers from the start to verify the chain, one read per sealed
segment. The cost at REC size M on a real account, and what a cold start
costs in requests, are **to be measured by the maintainer** with the optional
leg (`azure-objects.yml`); the figures for Azurite and RustFS at S, M and L
are in the milestone's record.

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
