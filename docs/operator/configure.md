# Configure

Everything is under the `Varve` section (ADR 0101). Values that name a choice
are strings, so a wrong one is a listed error, not a binder exception. Paths
are absolute. A `TimeSpan` is `hh:mm:ss`; a byte count is a number of bytes.

## Sources and precedence (ADR 0115)

Settings come from, later sources winning:

1. `appsettings.json` in the working directory, when there is one;
2. the files `--config <path>` names, in order;
3. the environment: `VARVE__AUTH__MODE=Oidc`, two underscores for each colon;
4. the command line: `--Varve:Auth:Mode=Oidc`, or `--set Auth:Mode=Oidc`, or
   the typed options of `varve serve` (`--auth-mode Oidc`, `--authority`,
   `--audience`, `--datasets-root`, `--anonymous`, `--urls`), which map onto
   the same keys. `--record-bootstrap` is no key: it asks the system writer
   to record the configured bootstrap operators (below).

**A key the server does not know refuses to start**, naming it: a misspelt
`Varve:Limit:QueryTimeout` is an error, never a default silently taken. A
value that does not validate refuses to start too, with every error listed.

`varve serve --print-config …` prints every effective value as JSON with the
source it came from (`command line`, `environment`, `file <path>`,
`default`), secrets redacted, validates, and exits without serving: run it
before a change goes live, and keep its output with the change.

```sh
varve serve --print-config --config /etc/varve/varve.json --set Limits:QueryTimeout=00:00:10
```

```json
{
  "Varve": {
    "DatasetsRoot": "/var/lib/varve",
    "Manage": { "BootstrapOperators": [ "Varve.Operator" ] },
    "Auth": { "Mode": "Oidc", "Authority": "https://login.example/", "Audiences": [ "api://varve" ] },
    "Limits": { "QueryTimeout": "00:00:30" },
    "Federation": { "AllowedEndpoints": [ "https://query.wikidata.org/" ] },
    "Load": { "AllowedSources": [ "https://data.example/" ] },
    "ForwardedHeaders": { "Enabled": true, "KnownProxies": [ "10.0.0.2" ] }
  }
}
```

## Datasets

| Setting | Default | Meaning |
|---|---|---|
| `DatasetsRoot` | none | the directory file datasets live under, one subdirectory per dataset, and the fleet's system dataset under `_fleet/` when there is no object store; absolute |

**Datasets are not configuration** (ADR 0163). They are declared in the
management plane, as TriG, with `varve apply` or `PUT /manage/datasets/{name}`;
every instance of the fleet converges to the declarations. A declared `File`
dataset is the directory of its name under the root; `Memory` keeps nothing
and starts empty; `ObjectStorage` is in the object store under the prefix
`{name}/`. A dataset name is 1 to 63 characters of `[A-Za-z0-9._-]`, starting
with a letter or a digit (ADR 0093). Durability is the backend's (ADR 0073).
A directory under the root that is not declared is not served. Declaring,
closing and deleting is [Datasets](datasets.md).

With neither `DatasetsRoot` nor `ObjectStorage`, the system dataset is in
memory and every declaration is lost when the process stops; the server
warns at start.

## The fleet — `Fleet` and `Manage`

The servers that share one system dataset are a fleet (ADR 0163): one writes
it, the others follow it (ADR 0164) and forward writes (ADR 0168). A server
with no object store is a fleet of one. [The fleet](fleet.md) explains the
model and its request costs.

| Setting | Default | Meaning |
|---|---|---|
| `Fleet:Writer` | `true` | `true`, `false`, or an instance name compared with `ObjectStorage:Instance`: whether this instance writes the system dataset; any value but `false` lets it claim the datasets placed on it. A fleet of one is its own writer whatever this says |
| `Fleet:InternalUrl` | none | the address other instances forward writes to, recorded in the writer record at claim; required on an instance that may claim in an object-store fleet; `https`, or `http` inside a private network only, since a caller's token crosses it |
| `Fleet:FollowInterval` | `00:00:01` | how old a follower's last advance may be before a read advances it, and how often the system dataset is advanced; at most a minute (ADR 0164) |
| `Fleet:AllowedLag` | 100 | how many system positions this instance may be behind the system tail and be ready |
| `Manage:BootstrapOperators` | `[]` | agent IRIs (`<issuer>#<subject>`) or role values that are always operators; the first system writer records them; at least one under `Oidc` (ADR 0163 point 7) |
| `Manage:Urls` | unset | listeners, `;`-separated, that serve `/manage/` alone; with them, the host's own listeners serve no management route |

A configured bootstrap set that differs from the recorded one makes the
instance not ready (`bootstrap: mismatch`). Change it by starting the system
writer once with `varve serve --record-bootstrap`: an explicit system commit.

## Removed keys

These keys are refused at start, each naming what replaces it:

| Key | Replaced by |
|---|---|
| `Datasets:{name}:Storage` | a dataset's declaration (ADR 0163); `varve apply --from-config <file>` prints the TriG |
| `Auth:Datasets:{name}:…` | grants in the dataset's declaration (ADR 0166); `varve apply --from-config` prints them too |
| `Auth:Server:Admin` | `Manage:BootstrapOperators`, and `varve:operator` grants in the fleet graph |

## The object store — `ObjectStorage`

One container or bucket for the server's `ObjectStorage` datasets, a dataset
per prefix `{name}/` (ADRs 0134–0139), and the fleet's system dataset under
`_fleet/` (ADR 0163). A dataset is declared with `varve:storage
varve:ObjectStorage`. Its placed instance claims its next writer epoch (ADR
0136); every other instance follows it read-only and forwards its writes
([The fleet](fleet.md)).

| Setting | Default | Meaning |
|---|---|---|
| `ObjectStorage:Provider` | none | `Azure` or `S3Compatible` |
| `ObjectStorage:ContainerUrl` | none | Azure: `https://{account}.blob.core.windows.net/{container}`; `http` only on loopback, for Azurite |
| `ObjectStorage:Endpoint` | none | S3-compatible: the service's `https` address |
| `ObjectStorage:Bucket` | none | S3-compatible: the bucket |
| `ObjectStorage:Region` | none | S3-compatible: the region the signature is for |
| `ObjectStorage:CachePath` | none | absolute; the local cache, a directory per dataset, and a follower's under `_followers/`: extent pages and the derived data any instance rebuilds |
| `ObjectStorage:CacheBytes` | 8 GiB | the most each dataset's extent page cache holds; at least 64 MiB (ADR 0139) |
| `ObjectStorage:Instance` | the machine name | who this server is in the writer record that `varve lease` shows, and the name `Fleet:Writer` and `varve:placedOn` compare with |

**No credential is configured, and none is accepted** (ADR 0137). Azure is
reached as the workload's identity: Container Apps' managed identity, workload
identity (`AZURE_FEDERATED_TOKEN_FILE`, `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`),
or on a workstation `az account get-access-token`; it needs Storage Blob Data
Contributor on the container. An S3-compatible store is reached by
`AssumeRoleWithWebIdentity` (`AWS_WEB_IDENTITY_TOKEN_FILE`, `AWS_ROLE_ARN`). A
key named like `AccountKey`, `ConnectionString` or `SecretAccessKey`, or a SAS
token in the address, refuses the start, naming the rule. Durability is the
store's: `Replicated`, a commit returns once the store acknowledged its
chunk. A follower — a read-only open — writes nothing and needs only read
access (Storage Blob Data Reader on Azure).

**Supported stores.** A writer's open first checks that the store refuses a
second create of a name and a replace against a stale version, and refuses to
open on one that does not (ADR 0134). That check catches a store that ignores
conditional writes; it cannot catch one that enforces them for one request at
a time but not under concurrency. The evidence for that is the contract
suite's race property, and the stores it has passed are the supported ones:
**Azure Blob Storage** (Azurite in CI, a real account on the optional leg) and
**RustFS**. **Any other S3-compatible store is untested.** Garage is known not
to enforce conditional writes and is refused.

## Authentication — `Auth`

| Setting | Default | Meaning |
|---|---|---|
| `Mode` | none, required | `Oidc` or `Anonymous` |
| `Authority` | — | the OIDC issuer; its discovery document is `/.well-known/openid-configuration` under it; required for `Oidc` |
| `Audiences` | `[]` | the data plane's audiences; at least one for `Oidc` |
| `Management:Audience` | `api://varve-manage` | the management plane's audience, which `Audiences` must not name: each plane accepts only its own, and a token for both is refused by both (ADR 0163 point 1) |
| `SubjectClaim` | `sub` | the claim that names the caller as a commit's agent: `oid` for Entra |
| `ApplicationClaim` | `azp` | the claim that names the application acting for the caller, recorded on the request span as `varve.azp` and never in the log; `appid` for an Entra v1 token (ADR 0127) |
| `RoleClaimType` | `roles` | the claim whose values are mapped to permissions |
| `RequireHttpsMetadata` | `true` | refuse an authority whose metadata is not served over HTTPS; off only for a loopback issuer in a test |
| `Production` | `false` | refuse anonymous mode |
Grants are not configuration: they are declared with each dataset (ADR
0166). [Authenticate](authenticate.md) explains the model and shows provider
examples.

## Limits — `Limits`

| Setting | Default | Meaning |
|---|---|---|
| `QueryTimeout` | `00:00:30` | a read is cut after this, with an error trailer where the format allows (ADR 0095); `request-timeout` |
| `ResultSizeCap` | 1 GiB | a read is cut after this many bytes; `result-too-large` |
| `MaxRequestBody` | 100 MiB | a request body above this is `413 request-too-large` |
| `PinnedReadLifetime` | `00:02:00` | the longest a response may hold its pin |
| `FeedHeartbeat` | `00:00:15` | a live tail writes a heartbeat line at this interval |
| `MaxConcurrentReads` | 64 | reads evaluated at once; a read beyond this waits in the queue |
| `ReadQueueLength` | 256 | reads waiting; one more is `503 server-busy` with `Retry-After` (ADR 0114) |
| `MaxQueryMemory` | 256 MiB | what one query's joins, sorts, groups and `DISTINCT` may hold, counted by rows; over it is `422 memory-limit-exceeded`, before the first byte or as a trailer (ADR 0114) |
| `MaxAsOfDistance` | 10,000 | how many positions below the head a `Varve-As-Of` read may go; further is `422 as-of-distance-exceeded`; a checkpoint at or below the position makes any distance cheap (ADR 0114) |
| `MaxLiveTailsPerClient` | 16 | live tails of `/commits` open per caller (per subject, or per address anonymous); one more is `429 too-many-live-tails` |
| `CommitsPageSize` | 1,000 | commits a `GET /commits` range answers before a `Link rel="next"` |

Every duration is positive and every count at least one, or the server does
not start. Each limit's problem type is in `docs/problems/`, with its
members: what the limit was and what the request asked for. The service
description states `varve:resultSizeCap`, `varve:maxAsOfDistance` and
`varve:commitsPageSize`, so a client can read them.

## Health and readiness — `Health` (ADR 0113)

| Setting | Default | Meaning |
|---|---|---|
| `ReadyLag` | `0` | how many positions a dataset's default projection may be behind its head and the server still ready; `0` is at head |
| `RateLimit` | `60` | probes per minute per client address on `/health/live` and `/health/ready` before `429 too-many-requests` |
| `StopDelay` | `00:00:00` | how long readiness answers `503` before the listener closes on a stop, so a load balancer takes the instance out first; up to five minutes |

## The lease — `Lease` (ADR 0116)

| Setting | Default | Meaning |
|---|---|---|
| `WaitFor` | `00:00:30` | how long a dataset lease another process holds is retried at start, logging the holder each second, before the dataset is failed with the holder as its reason; up to ten minutes |

A lease is never broken by the server. [Back up and restore](backup-and-restore.md#a-stale-lease)
says what `varve lease` does when a process died holding one.

## `SERVICE` over HTTP — `Federation` (ADR 0104)

| Setting | Default | Meaning |
|---|---|---|
| `AllowedEndpoints` | `[]` | IRI prefixes a `SERVICE` endpoint may start with; **empty refuses every `SERVICE`**, which is the evaluator's own default |
| `AllowPrivateAddresses` | `false` | let an endpoint resolve to loopback, link-local or a private range |
| `Timeout` | `00:00:30` | the time to the endpoint's response headers |
| `MaxResponseBytes` | 100 MiB | the most the endpoint may answer |

Only `http` and `https`, never a redirect, never credentials in the address
(ADR 0103). A refused endpoint is a failed `SERVICE`, and `SILENT` turns it
into an empty result as the evaluator decides (ADR 0055).

## `LOAD` over HTTP — `Load` (ADR 0104)

| Setting | Default | Meaning |
|---|---|---|
| `AllowedSources` | `[]` | IRI prefixes a `LOAD` document may start with; **empty refuses every `LOAD`** |
| `AllowPrivateAddresses` | `false` | as above |
| `Timeout` | `00:01:00` | the time to the document's response headers |
| `MaxResponseBytes` | 1 GiB | the largest document |

## Behind a proxy — `ForwardedHeaders`

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | honour `X-Forwarded-For`, `-Proto` and `-Host` |
| `KnownProxies` | `[]` | the addresses the headers are honoured from; with none listed, from none |

## Browsers from another origin — `Cors` (ADR 0132)

| Setting | Default | Meaning |
|---|---|---|
| `AllowedOrigins` | `[]` | the origins a browser application may call from, each `scheme://host[:port]` with no path, or `*` for any; **empty adds no CORS at all**: no response carries an `Access-Control-*` header |
| `MaxAge` | `00:10:00` | how long a browser may cache a preflight (`Access-Control-Max-Age`) |

```json
{ "Varve": { "Cors": { "AllowedOrigins": [ "https://console.example.org" ] } } }
```

A browser application sends its token in `Authorization`, exactly like any
other client, so credentials mode is never allowed:
`Access-Control-Allow-Credentials` is never sent, whatever is configured.

- **Allowed methods:** `GET`, `HEAD`, `POST`, `PUT`, `PATCH`, `DELETE`.
- **Allowed request headers:** `Authorization`, `Content-Type`, `Accept`,
  `If-Match`, `If-None-Match`, `Varve-If-Graphs-Match` (ADR 0126),
  `Last-Event-ID`, `Varve-As-Of`, `traceparent`, `tracestate`.
- **Exposed response headers:** `Varve-Position`, `ETag`,
  `Varve-Request-Id`, `Link`, `Location`, `Retry-After`,
  `WWW-Authenticate`.

A preflight is answered before authentication and before the read limit, so
it needs no token. A refusal (`401`, `429`, any problem) carries the CORS
headers too, so the application can read the problem.

## Telemetry — `Telemetry` (ADR 0112)

| Key | Default | Meaning |
|---|---|---|
| `Telemetry:QueryText` | `false` | record `db.query.text`, the query or update as sent, on the request span; off because a query can carry data |

The exporter itself is configured by the standard `OTEL_*` environment
variables, not under `Varve:`; see [Observe](observe.md).

## Writing the log — `Store` (ADR 0124)

Commits that queue while a write of the log is in flight are written
together, each still its own commit, with one flush: **group commit**. A
commit that finds the dataset idle is written at once; there is no timer.

| Key | Default | Meaning |
|---|---|---|
| `Store:GroupCommit:MaxRecords` | `1024` | a batch takes no more commits once it holds this many records; `1` writes every commit alone. A dataset in memory always writes each commit alone: it has no flush for a batch to share |
| `Store:GroupCommit:MaxBytes` | 4 MiB | the most bytes of records a batch of two or more commits may hold; at most 4 MiB, the format's write window |

`varve.store.commit.batch_size` ([Observe](observe.md)) shows how many
commits each write carried.

## Domain events — `Events` (ADRs 0143, 0144)

| Key | Default | Meaning |
|---|---|---|
| `Events:Source` | unset | the base of every event's `source` and every schema URL: an absolute `http` or `https` URL ending in `/`, under which a dataset is `datasets/{name}` |
| `Events:EventHubs:Host` | unset | the Event Hubs namespace's host, `{namespace}.servicebus.windows.net`, that the AsyncAPI document lists as the `eventhubs` server; with `Hub`, both or neither |
| `Events:EventHubs:Hub` | unset | the event hub's name |

**Byte identity holds per base.** A rendered event is the same bytes every
time it is read, and its `source` and `dataschema` name the base. With
`Events:Source` unset, the base is the request's own scheme, host and path
base, so a deployment reached under more than one host name renders
different bytes under each. Set `Events:Source` to the one public URL. The
Event Hubs server is only named here: publishing to it is an application's
work, as the Event Hubs sample shows ([Integrate](integrate.md)).

## The runtime (ADR 0110)

The server ships its runtime configuration: the **workstation** garbage
collector, **concurrent**, with `System.GC.ConserveMemory` **5**, in the
tool's `runtimeconfig.json` and in the Native AOT binary alike. The Web SDK's
default would be the server collector, which keeps committed memory
proportional to what every core allocated; the server's live heap is small
and allocates in bursts, which the workstation collector fits, and
`ConserveMemory` compacts the large object heap that the 6c soak found
fragmenting. The soak gate is judged under this configuration, with the
default runtime's hour beside it in the record and its drift measured
against minutes 30–40, after the collector has reached its working size
(ADR 0082, amended and superseded in part by ADR 0110).

`DOTNET_GCHeapHardLimit`, `DOTNET_gcServer` and the rest still apply as to
any .NET process and override what ships: in a container the runtime bounds
the heap to the cgroup limit by itself, and no hard limit of Varve's own is
set.
