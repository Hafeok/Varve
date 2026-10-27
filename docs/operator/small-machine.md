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

## Runtime limits for 8 GB

| Setting | Suggestion | Why |
|---|---|---|
| `DOTNET_GCHeapHardLimit` | `0x140000000` (5 GiB) | leaves the page cache and the operating system room |
| `Varve:Store:GroupCommit:MaxBytes` | the default, 4 MiB | a batch's records are held until written |
| `Varve:ObjectStorage:CacheBytes` | 1–2 GiB | when the datasets are in an object store; the cache is on the SSD |
| `Varve:Limits:MaxQueryMemory` | the default, 256 MiB | one query's bound (ADR 0114) |
| `Varve:Limits:MaxConcurrentReads` | 4 | one per core |

## Measured

Commit latency, query throughput and cold start on a Pi 5 with an NVMe SSD:
**to be measured by the maintainer**, with the same commands as the
milestone's record (`Varve.Benchmarks --objects file S`, `--extent-reads S`),
and filled in here.
