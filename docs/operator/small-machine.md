# Run on a small machine

A Raspberry Pi 5 (arm64, 8 GB) is a first-class target (ADR 0142): the server
is a Native AOT single file for `linux-arm64`, built and tested on that
architecture in CI, and the image is multi-architecture (ADR 0111).

## The file backend on an NVMe SSD — supported

Put the datasets root on an NVMe SSD (a Pi 5 HAT, or USB 3), not on the SD
card. A log flushes every commit to the device (`Synchronised`, ADR 0073), and
an SD card's `fsync` latency and write endurance do not suit that: commits
are slow, and the card wears out.

```sh
sudo mkdir -p /srv/varve && sudo chown "$USER" /srv/varve     # on the SSD
varve serve --set DatasetsRoot=/srv/varve --anonymous
# then declare it, with varve apply or:
curl -X PUT -H 'Content-Type: application/trig' http://localhost:5000/manage/datasets/people \
  --data '<urn:people> a <https://w3id.org/varve/ns#Dataset> ; <https://w3id.org/varve/ns#datasetName> "people" ; <https://w3id.org/varve/ns#storage> <https://w3id.org/varve/ns#File> .' 
```

or with the image:

```sh
docker run --rm --read-only -v /srv/varve:/var/lib/varve -p 8080:8080 ghcr.io/hafeok/varve serve --anonymous
```

## An object store on a NAS — only by identity

**A single machine uses the file backend, on a directory** (above). An
object store earns its place when several instances share one log. On one
machine it only adds a server to run and a credential path to set up. An
S3-compatible store is for a setup that already has an OIDC issuer for the
store to trust, so that the server's web identity can be exchanged at the
store's STS. [CI's storage legs](../../.github/storage/compose.yaml) are the
worked example: RustFS, with a mock-oauth2-server as its OIDC provider, its
outbound allow-list, and the `groups` claim mapped to a policy.

The server takes no static key for an object store, not even on loopback
(ADR 0137, amended 2026-10-27): the loopback key is the S3 adapter's, for
tests that make their own buckets, and no server setting reaches it. So an
S3-compatible store on a NAS, or on the Pi itself, is supported in one shape:

- **The store speaks web identity.** The server assumes a role with
  `AssumeRoleWithWebIdentity`, its token in `AWS_WEB_IDENTITY_TOKEN_FILE` and
  the role in `AWS_ROLE_ARN`; `Varve:ObjectStorage:Provider` is
  `S3Compatible` with the NAS's `Endpoint`, `Bucket` and `Region`, and
  `StsEndpoint` the store's own STS (for RustFS, its root URL, with an OIDC
  provider configured on it, as CI's storage legs do).

**A typical NAS deployment falls outside it**: an S3 server on the NAS,
reached over the LAN with a static access key, is refused at start, naming
ADR 0137. On such a NAS, use the file backend on the Pi's own SSD, and back it
up to the NAS by copying the directory ([Back up and restore](backup-and-restore.md)).

## Garage is not supported for the object-storage backend

[Garage](https://garagehq.deuxfleurs.fr/), an S3-compatible store often run on
small machines, accepts a conditional write's `If-None-Match: *` and ignores
it; its documentation says conditional writes are structurally impossible
there. The object-storage backend's one-writer guarantee rests on a store
refusing a second create of a name and a replace against a stale version
(ADRs 0134, 0136), so every writer's open probes for those refusals, and on
Garage the open fails: *the object store at … does not enforce conditional
writes*. Use the file backend on the Pi's NVMe SSD, which is the supported
small-machine setup; Garage, or any store, can still hold its backups as
copied files. Any S3-compatible store but RustFS is untested for the
object-storage backend: the probe catches a store that ignores conditional
writes, not one that enforces them only one request at a time
([Configure](configure.md)).

## What a dataset needs, to be written and to be reopened

**The dataset measured below was written and reopened in a 256 MB
container** (ADR 0244). The server sizes its memtable to the memory it is given, a writer
cannot outrun the flush, and an open replays within the same bound.

**Every figure on this page is per open dataset**, except the server's own
50 MB. Each open dataset has its own memtable, its own key directories and
its own seek directory cache, and nothing here bounds their sum: a server
with ten open datasets can hold ten memtables. A budget across datasets is
[#295](https://github.com/Hafeok/Varve/issues/295).

What a dataset holds, by part:

| Part | In memory | Bounded by |
|---|---|---|
| The server itself | about 50 MB | |
| The memtable | 192 bytes a quad and about 96 a term in it; between its limit and twice it while a flush runs, and as much again while its tiers merge | `Varve:Store:MemtableLimit`: unset, a sixteenth of the collector's heap limit, at most 1,000,000 entries. In a 512 MB container about 117,000 entries, so 24 to 96 MiB |
| Named graphs | 64 bytes each, always | the data. A million graphs is 64 MB ([#255](https://github.com/Hafeok/Varve/issues/255)) |
| Key directories | 40 to 48 bytes per 128 quads, per index order | the data. About 4 MB per million quads |
| A checkpoint being written | its sorts' buffers and at most 16 MiB of terms | fixed |
| An open | 8 MiB of the log, decoded, and the memtable | fixed |
| As-of reads | the seek directories they use | `Varve:Store:SeekDirectoryCacheBytes`, 32 MiB |

Reopening needs no more than writing did. After a kill, the open replays
what lay above the last flush through the same memtable, flushing as it
goes.

**On disk** a dataset is its log, its extent sets, which grow with its
history, and the default projection's own runs, which are a second copy of
the present state: about 200 bytes a quad, and the dictionary. The second
copy does not grow with checkpoints. It is replaced by the newest
checkpoint's set when the server next starts
([#256](https://github.com/Hafeok/Varve/issues/256)).

**Measured**, on the Native AOT server (`linux-x64`, 4 cores), in a memory
cgroup as `docker run --memory` makes one: an import of 3.06 million quads
in 515,000 named graphs over HTTP, 4,731 commits, then a checkpoint, a
minute idle and a restart.

| Memory limit | The import | Peak | Idle, after the checkpoint | After a restart |
|---|---:|---:|---:|---:|
| none (16 GB) | 62 s | 704 MB | 154 MB | 111 MB |
| 1 GB | 72 s | 317 MB | 116 MB | 112 MB |
| 512 MB | 57 s | 246 MB | 107 MB | 108 MB |
| 384 MB | 59 s | 268 MB | 108 MB | 108 MB |
| 256 MB | 68 s | 214 MB | 108 MB | 105 MB |

- No write was refused and nothing was killed.
- Of the 108 MB at rest, 33 MB is the 515,000 named graphs and about 50 MB
  the server.
- With no limit the collector lets the heap run to 704 MB during the
  import, and gives it back once no dataset on the server has taken a
  commit for five seconds after the checkpoint
  (`Varve:Store:ReleaseMemoryAfterCheckpoint`). The collection pauses the
  whole process, so one busy dataset keeps it from running.
- Killed mid-import under 512 MB and started again under it, the server
  answered in 0.4 s at 91 MB.

To measure your own, with the server binary and no container:

```sh
dotnet run -c Release --project tests/Varve.Benchmarks -- --import-memory /path/to/Varve.Server 3000
```

A third argument names a cgroup directory to put the server in.

## Runtime limits for 8 GB

| Setting | Suggestion | Why |
|---|---|---|
| `DOTNET_GCHeapHardLimit` | `0x140000000` (5 GiB) | leaves the page cache and the operating system room |
| `Varve:Store:GroupCommit:MaxBytes` | the default, 4 MiB | a batch's records are held until written |
| `Varve:ObjectStorage:CacheBytes` | 1–2 GiB | when the datasets are in an object store; the cache is on the SSD |
| `Varve:Limits:MaxQueryMemory` | the default, 256 MiB | one query's bound (ADR 0114) |
| `Varve:Limits:MaxConcurrentReads` | 4 | one per core |
| `Varve:Limits:IdempotencyWindow` | the default, 100,000 | see below |

**The idempotency index, per dataset this instance writes** (ADR 0246). The
writer holds 16 bytes of each key's hash and its commit's position for every
keyed commit in the window. That is **about 56 bytes per keyed commit**:
- 5.4 MiB for a full window of 100,000 keyed commits;
- 2.6 MiB when 75,000 of them are keyed.

These were measured at REC M with `Varve.Benchmarks -- --idempotency-index M`.
The index grows with the keyed writes in the window, never with the size of
the data.

A follower and a standby hold none: an instance reads a dataset's keys only
when it becomes that dataset's writer, in the background, in 0.3 to 0.5 s at
REC M.

Ten written datasets with full windows hold about 55 MiB. If that is too
much on 8 GB, a smaller `IdempotencyWindow` shrinks the index in proportion.
It also shortens how long a client may wait before retrying: a retry from
further back is refused with `422 idempotency-key-expired`.

## Inference

A binding's closure is held in memory (ADR 0195). Measured on REC M
(5 million data quads), it holds 718 bytes a data quad under `rdfs` and
755 under `owl2rl`, the store's own data not included: 3.4 and 3.6 GiB.

`Varve:Inference:ClosureBytes` is a budget that every structure a closure
grows asks before it is allocated (ADR 0195, amended 2026-11-18): a fact at
its profile's measured cost (100 bytes under `rdfs`, 70 under `owl2rl`,
beside its 72-byte record), 96 bytes beside it for the binding's row, its
explicit set, a build's working sets while they live, and 160 bytes for
each of the dataset's terms the binding keeps resolved. Readiness's `peak`
for a binding is what it has asked for at most.

**Sizing REC M** (5 million data quads, one binding over its 63 graphs,
measured 2026-11-18 on .NET 10.0.15): a from-scratch build asks the budget
for at most

| Profile | Facts | Closures' peak | With the rows | `ClosureBytes` at least | Heap limit at least |
|---|---|---|---|---|---|
| `rdfs` | 17.5 million | 4.66 GB | 6.34 GB | 6 GiB | 9 GiB |
| `owl2rl` | 23.1 million | 4.54 GB | 6.76 GB | 6.5 GiB | 9.5 GiB |

with the terms' 160 bytes each on top. Every binding together takes from
one pool: the heap limit less the memtable's worst case (820 MiB at the
default limit above a 3.2 GiB heap limit) and `Varve:Inference:ReserveBytes`,
2 GiB by default, kept for queries, caches and requests. The heap limit
above is `ClosureBytes` plus those two. A build needs 0.75 to 0.88 of what
the budget counts, measured, and the budget keeps a margin of a tenth, so a
`ClosureBytes` the pool can hold is never outrun. The same graphs bound
`varve:crossGraph true` need their premise sets besides: on REC S, the
budget's peak is 1.9 times the per-graph build's under `rdfs` and 2.2 times
under `owl2rl`.

**The reserve** was measured at the runtime limits above, with a 5 GiB heap
limit, four concurrent reads at 256 MiB each and a writer: the rest of the
server needed more than 1.5 GiB and at most 2 GiB. Raise it if you raise
`Varve:Limits:MaxConcurrentReads` or `Varve:Limits:MaxQueryMemory`.

At the 5 GiB heap limit above, the pool is 2.2 GiB, and `ClosureBytes`
unset is a quarter of the heap limit, 1.25 GiB: about a million data quads
under either profile. Under a heap limit of 2.67 GiB the defaults leave no
pool, and every materialising binding is refused until the reserve or the
memtable limit is lowered. A closure past its limit is refused before the
memory is asked for, the binding stops and answers `inference-unavailable`,
and the instance stays up; restart it with the limit raised, or narrow the
binding (the problem page says how). The instance refuses at start a
`ClosureBytes` above the pool.

A from-scratch closure of REC M takes about one minute under `rdfs` and
four and a half under `owl2rl`, on four cores: what a cold start or an
as-of read older than the history pays.

## Measured

Commit latency, query throughput and cold start on a Pi 5 with an NVMe SSD:
**to be measured by the maintainer**, with the same commands as the
milestone's record (`Varve.Benchmarks --objects file S`, `--extent-reads S`),
and filled in here.
