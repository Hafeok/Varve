# 0142 — Deployment: Azure Container Apps, the Aspire Azure variant, small machines

## Status

**Accepted — filed unaccepted by milestone Object storage of #92, 2026-10-15**
(ADR 0066). Acceptance is the maintainer's act on the pull request. The
maintainer's decisions on the Object storage plan, 2026-10-15, that it
records: what-if and deployment only in the optional leg; S3 credentials
are web identity plus the loopback key, nothing else; the Pi page supports
the file backend on NVMe and says plainly that a typical NAS falls outside
both credential paths.

Builds on ADRs 0111 (the image), 0113 (health), 0117 (Varve.Aspire), 0136
(the epoch) and 0137 (identity-only credentials). Records decision set
[`deployment-on-container-apps-and-small-machines`](../decisions/deployment-on-container-apps-and-small-machines.md).

## Context

The object-storage backend makes a stateless deployment possible: the log,
the checkpoints' extents and the catalog are in a container, and the local
disk is a cache any replica rebuilds. Three targets were asked for: Azure
Container Apps, the Aspire integration's Azure variant, and a small
self-hosted machine — a Raspberry Pi 5.

## Decision

### Azure Container Apps — `deploy/azure/container-apps/main.bicep`

One resource group:

| Resource | What |
|---|---|
| Log Analytics workspace | the environment's logs |
| Storage account | StorageV2, `Standard_ZRS` by default (`Standard_LRS`, `Standard_GZRS` allowed); **shared-key access off**, public blob access off, TLS 1.2, OAuth by default |
| Blob container `datasets` | private; each dataset under its own prefix |
| Container Apps environment | consumption, logs to the workspace |
| Container App | the image of ADR 0111; system-assigned identity; external ingress to 8080; `/health/live` liveness, `/health/ready` readiness, a startup probe; an `EmptyDir` volume for the cache; `minReplicas` 0 or 1, **`maxReplicas` 1**, single revision mode |
| Role assignment | **Storage Blob Data Contributor** for the app's identity, scoped to the container |

- **No credential is created or read.** With shared-key access off, no
  account key, SAS token or connection string works against the account, so
  none can leak; the server reaches the container as its managed identity
  (ADR 0137).
- **One replica.** A second would claim the epoch and supersede the first
  (ADR 0136). A revision rollover is a handover: the new revision claims,
  the old one is superseded and leaves the load balancer by its readiness.
- **Scale to zero** (`minReplicas` 0) is allowed: a cold start reads the
  catalog, attaches extents lazily and replays the log after the catalog's
  position (ADR 0139).
- The first start may fail until the role assignment propagates; the
  configured dataset then fails to open, the replica restarts, and a later
  start succeeds. The operator guide says so.

### CI

- **Always:** `az bicep build` and `az bicep lint` for every template under
  `deploy/azure`.
- **Optional leg** (`.github/workflows/azure-objects.yml`, dispatched by the
  maintainer, environment `azure-objects`, `main` only): the object-store
  contract suite and the storage contract against a real container, signed
  in by GitHub's OIDC token used as a federated credential through the
  backend's own workload-identity source; then `az deployment group what-if`
  for the template, and a deployment when asked. Without the leg, the
  Container Apps cold-start and cost figures are **to be measured by the
  maintainer**.

### Varve.Aspire — `WithAzureBlobStorage(containerUrl, cacheBytes?)`

- Takes the container's address as a `ReferenceExpression`, as `WithOidc`
  takes its authority, and sets `Varve:ObjectStorage` and each dataset's
  storage to `ObjectStorage`; the volume holds the page cache. **It adds no
  package**: Aspire's Azure Storage integration would hand over a connection
  string, which ADR 0137 refuses.
- **No Azurite variant of the sample.** The server accepts a development key
  only on loopback (ADR 0137), and a container reaches a neighbouring
  container by name, not on loopback. An Azurite container beside the
  server is therefore refused by design; run mode keeps file datasets, and
  the store's own tests run against Azurite on the CI host's loopback.

### Small machines — a Raspberry Pi 5 (arm64, 8 GB)

- **Supported: the file backend on an NVMe SSD.** An SD card's `fsync` and
  write endurance do not suit a log.
- **An object store on a NAS** is supported only with
  `AssumeRoleWithWebIdentity`, or when the store runs on the same host as
  the server and is reached on loopback with its key. **A typical NAS
  deployment — an S3 server on the NAS, static keys, reached over the LAN —
  falls outside both**, and the server refuses it.
- The operator page "Run on a small machine" gives the setup for both, the
  runtime limits, and the timings, with the Pi's own figures **to be
  measured by the maintainer**.

## Alternatives considered

- **Aspire's `Aspire.Hosting.Azure.Storage` resource.** It models the
  account well, but its reference to a container is a connection string, and
  for the emulator one with the well-known key: both refused by ADR 0137. An
  expression keeps the integration at one package.
- **More than one replica with a lease.** A lease is a timer (ADR 0136); the
  epoch makes the second replica a takeover, not a standby. Standbys are
  Replication's (#97).
- **A static key for the NAS case.** It would break ADR 0137's rule for one
  target; the maintainer decided against it.

## Consequences

- An Azure deployment has no secret to rotate or leak.
- The Pi's NAS case needs a store that speaks web identity; most do not, and
  the page says so.
- The template is validated on every run; whether it deploys is the optional
  leg's to show.

## Checks

- `az bicep build` and `az bicep lint` pass for `deploy/azure/container-apps/main.bicep` (CI job `bicep`).
- `AzureBlobStorageTests`: the environment names the container and the
  cache, every dataset is `ObjectStorage`, and no variable is a credential.
- The optional leg: `AzureAccountObjectStoreContractTests`,
  `AzureAccountStorageContractTests`, and the what-if.
