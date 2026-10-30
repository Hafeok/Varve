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
               image=ghcr.io/hafeok/varve@sha256:{digest} operators='["Varve.Operator"]'
```

Then declare the datasets with `varve apply`, as [Datasets](datasets.md) says.

The deploying identity needs to create role assignments on the resource group
(Owner, or User Access Administrator beside Contributor). The outputs are the
public app's URL, the writer's internal URL, the container's URL, and both
apps' principal ids.

| Parameter | Default | |
|---|---|---|
| `authority`, `audience` | — | `Varve:Auth` (ADR 0037); register the API as [Authenticate](authenticate.md) says |
| `managementAudience` | `api://varve-manage` | `Varve:Auth:Management:Audience` (ADR 0163) |
| `operators` | — | `Varve:Manage:BootstrapOperators`, at least one |
| `image` | `ghcr.io/hafeok/varve:latest` | pin a digest in production |
| `storageSku` | `Standard_ZRS` | `Standard_ZRS` keeps a commit through a zone's loss; `Standard_LRS` is cheaper, one datacentre; `Standard_GZRS` adds a second region asynchronously |
| `cacheBytes` | 4 GiB | `Varve:ObjectStorage:CacheBytes`, within the replica's ephemeral storage |
| `cpu`, `memory` | `1.0`, `2Gi` | each replica's size |
| `writerMinReplicas` | 1 | the writer: 1 keeps it warm; 0 lets it scale to zero, and a write then waits for its start |
| `minReplicas`, `maxReplicas` | 0, 3 | the public app's replicas, each a follower that forwards writes |

**The first start can fail** until the role assignment has propagated, which
takes a few minutes: the system dataset cannot be opened, the server exits,
and Container Apps restarts it until it can.

## Two apps: the writer and the readers

Opening a dataset for write claims its next writer epoch (ADR 0136), so one
instance writes each dataset, and the fleet's system dataset. The layout is
two apps on one container (ADRs 0163, 0168):

- **`varve-writer`**: one replica (`maxReplicas` 1, one active revision),
  `Varve:Fleet:Writer` `true`, and `Varve:Fleet:InternalUrl` its internal
  FQDN in the environment. It writes the system dataset and every dataset
  placed on it; its identity has **Storage Blob Data Contributor** on the
  container.
- **`varve`**: N replicas, `Varve:Fleet:Writer` `false`. Each follows the
  system dataset and the datasets it is read for (ADR 0164), and forwards
  every write to `varve-writer` at the address in the writer record. Its
  identity reads the container, and **writes only under the `readers/`
  prefix**, where a reader announces the oldest catalog version it holds for
  extent maintenance (#110): on Azure an ABAC condition on the role
  assignment, on S3 a prefix-scoped policy. Whichever of #110 and #105 lands
  second reconciles this grant with the reader contract.

**Its recovery is a restart.** When `varve-writer`'s replica fails,
Container Apps restarts it, and writes are refused with `503` until it is
back. Measured from the stop to the first write forwarded again, that is
2.0–3.2 s plus the platform's own restart, where a warm standby's takeover
is 0.3–0.9 s ([Restart or handover](fleet.md#restart-or-handover-choosing-the-layout)).

Clients reach `varve`; a write costs one extra hop inside the environment.
The internal address carries the caller's token, so it stays inside the
environment's network, or is `https`. Datasets are not parameters of the
deployment: declare them with `varve apply` once it is up ([Datasets](datasets.md)).
[The fleet](fleet.md) has the model and the request costs.

A new revision of `varve-writer` is a clean handover: it claims the epoch,
the old revision is superseded, its `/health/ready` turns false
(`superseded`), and it leaves the load balancer before it is stopped. While
`varve-writer` is down, `varve` keeps serving reads under the grants it last
applied, and answers writes
[`503 commit-outcome-unknown`](../problems/commit-outcome-unknown.md), since
it cannot tell whether a write it forwarded arrived (ADR 0214). Every claim is in the writer record:

```sh
varve lease https://{account}.blob.core.windows.net/datasets/people
```

which shows the epoch, its holder and every claim, each with its trigger —
`restart` for a replica's start, `operator` for a takeover. **A run of
`restart` claims alternating between two holders means two writers are
configured for one dataset**: two apps with `Writer` `true`, or `maxReplicas`
above 1 on `varve-writer`, each start superseding the other; readiness
reports the superseded one's management role `superseded`. One claim per revision rollover, each by the new
revision after a `shutdown` release by the old, is the healthy pattern. A
superseded instance never claims again on its own: it stays live and serves
reads, and only a fresh process start claims, which is why liveness stays
true while readiness reports `superseded`.
Should a writer have to be stopped from writing without being stopped,
`varve lease … --take --epoch n` takes epoch n over and releases it for the
next writer.

## Cold start and scale to zero

With `minReplicas` 0, the default, a request after an idle period starts a
replica. A start reads the manifest, the writer record and the catalog,
attaches each extent lazily — its first and last 1 MiB pages, then only the
pages reads touch — and reads the log **only after the catalog's cursor**
(ADR 0139): it lists `log/`, and reads the chunks of the open segment from
the slot the catalog records. A sealed segment is one object, consolidated
by its seal (ADR 0135), and is not read at all. The commit index below the
catalog is read by the first read that needs it — an as-of read below the
newest checkpoint, or a lookup by time — from the extent sets' own commit
index blobs in the store (ADR 0161), and kept in the cache; the log's
headers are read only for a set written before that release.

What a start costs is the listing of `log/`, which grows with the open
segment's chunks, one per commit batch; and the chunks after the newest
checkpoint, read sixteen at a time. So a checkpoint policy keeps the second
small, and the open segment's length bounds the first. At REC size M on
loopback, with the newest checkpoint 1,000 commits behind the head:
**4.3 s on Azurite** (2.1 s listing, 1.2 s reading 1,001 chunks) and **7.0 s
on RustFS** (6.4 s listing the open segment's 19,745 chunk names), against
23–29 s before the cursor. The first as-of read below the checkpoint then
took 10–28 s, once, reading the log's headers; with the shared commit index
it reads the sets' commit index blobs instead (the measured figures are in
the milestone's record and `tests/Varve.Benchmarks/README.md`).

`minReplicas` 1 keeps a replica warm, so no request waits for a start: a
latency option, not a requirement. The cost on a real account is **to be
measured by the maintainer** with the optional leg (`azure-objects.yml`); the
figures for Azurite and RustFS are in the milestone's record and
`tests/Varve.Benchmarks/README.md`.

## Checkpoints

A checkpoint writes an extent set for the window since the previous one and
swaps the catalog; a follower or the next replica finds it there. Reads at
any position below the newest checkpoint are served from the extents with no
log read. The writer merges adjacent sets in the background, so the chain
stays short however many checkpoints are taken (ADRs 0158, 0159); the policy,
its bound and what it costs are in [Back up and restore](backup-and-restore.md#checkpoints).

## Readers, and what the store keeps for them

A set the catalog no longer names is deleted by the writer once no reader
record holds it (ADR 0160). Every instance that reads the dataset's extents
writes one, `readers/<id>` under the dataset's prefix, announcing the catalog
versions it reads: the writer, a second replica serving reads after it lost
the epoch, and `varve info` or `query` over the store's address.

- **Give each replica its own `Varve:ObjectStorage:ReaderId`**, stable across
  its restarts: a container's cache does not survive one, and without a
  configured id every restart leaves its previous record behind, holding
  sets back until an operator forgets it.
- **A follower's identity needs write access to `readers/` only.** On Azure,
  Storage Blob Data Reader on the container, and Storage Blob Data
  Contributor with a condition on the blob path:

  ```text
  (
   (
    !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/write'})
    AND
    !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/add/action'})
    AND
    !(ActionMatches{'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/delete'})
   )
   OR
   (
    @Resource[Microsoft.Storage/storageAccounts/blobServices/containers/blobs:path] StringLike '{dataset}/readers/*'
   )
  )
  ```

  (The form of Microsoft's "Write blobs in named containers with a path"
  example; the blob path is the blob's name within the container.) On S3, a
  policy granting `s3:PutObject` and `s3:DeleteObject` on
  `arn:aws:s3:::{bucket}/{dataset}/readers/*`, beside `s3:GetObject` and
  `s3:ListBucket`.

  The writer's Storage Blob Data Contributor on the container covers its own.
  An identity that may only read opens unannounced, explicitly, with
  `--unannounced` (ADR 0160); such a read fails rather than answer wrong if
  the writer deletes what it reads.
- **Retained bytes are on `/health/ready`**, under `retention`, and in
  `varve storage readers list`. A dead reader's record holds what it
  announced until the same id starts again, or `varve storage readers
  forget` removes it; no timer ever does (ADR 0160). See
  [Back up and restore](backup-and-restore.md#readers-and-retained-bytes).

## Costs

Every commit is one write (a chunk) per group-commit batch; a checkpoint is
nine writes per set, one reader-record replace and one catalog replace; a
merge is the same for its one set, and then a delete per blob of the sets it
replaced once no reader holds them; reads are ranged reads of 1 MiB pages on
a cache miss. Blob Storage bills writes and reads per 10,000 and
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

