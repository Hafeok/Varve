# Configure

Everything is under the `Varve` section (ADR 0101). Values that name a choice
are strings, so a wrong one is a listed error, not a binder exception. Paths
are absolute. A `TimeSpan` is `hh:mm:ss`; a byte count is a number of bytes.

## Sources and precedence (ADR 0115)

Settings come from, later sources winning:

1. `appsettings.json` in the working directory, when there is one;
2. the files `--config <path>` names, in order;
3. the environment: `VARVE__AUTH__MODE=Oidc`, two underscores for each colon;
   a segment that names a dataset keeps its case, so the dataset `people` is
   `VARVE__DATASETS__people__STORAGE`, and `VARVE__DATASETS__PEOPLE__STORAGE`
   is another dataset, `PEOPLE`;
4. the command line: `--Varve:Auth:Mode=Oidc`, or `--set Auth:Mode=Oidc`, or
   the typed options of `varve serve` (`--auth-mode Oidc`, `--authority`,
   `--audience`, `--datasets-root`, `--dataset name[=File|Memory]`,
   `--anonymous`, `--urls`), which map onto the same keys.

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
    "Datasets": {
      "people": { "Storage": "File" },
      "scratch": { "Storage": "Memory" }
    },
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
| `DatasetsRoot` | none | the directory file datasets live under, one subdirectory per dataset; absolute; required when any dataset is `File`, or when `Datasets` is empty |
| `Datasets:{name}:Storage` | none | `File` or `Memory`; a `Memory` dataset is volatile and starts empty |

A dataset name is 1 to 63 characters of `[A-Za-z0-9._-]`, starting with a
letter or a digit (ADR 0093). Durability is the backend's (ADR 0073): `File`
flushes every commit to the device, `Memory` keeps nothing. Directories under
the root that are not configured are discovered and served; the admin API's
`PUT /datasets/{name}` creates a new one there (ADR 0106), and
`DELETE` of a closed dataset removes its directory.

## Authentication — `Auth`

| Setting | Default | Meaning |
|---|---|---|
| `Mode` | none, required | `Oidc` or `Anonymous` |
| `Authority` | — | the OIDC issuer; its discovery document is `/.well-known/openid-configuration` under it; required for `Oidc` |
| `Audiences` | `[]` | the audiences a token may carry; at least one for `Oidc` |
| `SubjectClaim` | `sub` | the claim that names the caller as a commit's agent: `oid` for Entra |
| `RoleClaimType` | `roles` | the claim whose values are mapped to permissions |
| `RequireHttpsMetadata` | `true` | refuse an authority whose metadata is not served over HTTPS; off only for a loopback issuer in a test |
| `Production` | `false` | refuse anonymous mode |
| `Datasets:{name}:Read`, `Write`, `Admin` | `[]` | claim values granting each permission on every graph of the dataset; cumulative — write grants read, admin grants both |
| `Datasets:{name}:Grants:{i}:Claim`, `Permission`, `Graphs`, `GraphPrefixes` | — | a grant scoped to graphs (ADR 0107): `Permission` is `read` or `write`; `Graphs` lists IRIs and `default`; `GraphPrefixes` lists IRI prefixes; at least one of the two |
| `Server:Admin` | `[]` | claim values that administer the server: create, open, close and delete datasets, and every permission on every dataset (ADR 0106) |

A grant that names a dataset not configured refuses to start.
[Authenticate](authenticate.md) explains the model and shows provider
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

## Telemetry — `Telemetry` (ADR 0112)

| Key | Default | Meaning |
|---|---|---|
| `Telemetry:QueryText` | `false` | record `db.query.text`, the query or update as sent, on the request span; off because a query can carry data |

The exporter itself is configured by the standard `OTEL_*` environment
variables, not under `Varve:`; see [Observe](observe.md).

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
