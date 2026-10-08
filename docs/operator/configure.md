# Configure

Everything is under the `Varve` section (ADR 0101). Values that name a choice
are strings, so a wrong one is a listed error, not a binder exception. Paths
are absolute. A `TimeSpan` is `hh:mm:ss`; a byte count is a number of bytes.

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
`PUT /datasets/{name}` creates a new one there (ADR 0105), and
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
| `Datasets:{name}:Grants:{i}:Claim`, `Permission`, `Graphs`, `GraphPrefixes` | — | a grant scoped to graphs (ADR 0106): `Permission` is `read` or `write`; `Graphs` lists IRIs and `default`; `GraphPrefixes` lists IRI prefixes; at least one of the two |
| `Server:Admin` | `[]` | claim values that administer the server: create, open, close and delete datasets, and every permission on every dataset (ADR 0105) |

A grant that names a dataset not configured refuses to start.
[Authenticate](authenticate.md) explains the model and shows provider
examples.

## Limits — `Limits`

| Setting | Default | Meaning |
|---|---|---|
| `QueryTimeout` | `00:00:30` | a read is cut after this, with an error trailer where the format allows (ADR 0095) |
| `ResultSizeCap` | 1 GiB | a read is cut after this many bytes |
| `MaxRequestBody` | 100 MiB | a request body above this is `413` |
| `PinnedReadLifetime` | `00:02:00` | the longest a response may hold its pin |
| `FeedHeartbeat` | `00:00:15` | a live feed writes a heartbeat line at this interval |

Every duration is positive, or the server does not start.

## `SERVICE` over HTTP — `Federation` (ADR 0103)

| Setting | Default | Meaning |
|---|---|---|
| `AllowedEndpoints` | `[]` | IRI prefixes a `SERVICE` endpoint may start with; **empty refuses every `SERVICE`**, which is the evaluator's own default |
| `AllowPrivateAddresses` | `false` | let an endpoint resolve to loopback, link-local or a private range |
| `Timeout` | `00:00:30` | the time to the endpoint's response headers |
| `MaxResponseBytes` | 100 MiB | the most the endpoint may answer |

Only `http` and `https`, never a redirect, never credentials in the address
(ADR 0102). A refused endpoint is a failed `SERVICE`, and `SILENT` turns it
into an empty result as the evaluator decides (ADR 0055).

## `LOAD` over HTTP — `Load` (ADR 0103)

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

## The runtime

The server is a .NET process: `DOTNET_GCHeapHardLimit`, `DOTNET_gcServer`
and the rest apply as they do to any. The soak gate (ADR 0082) was run with
the default runtime; the 6c record's addendum shows the working set under a
128 MB hard limit. Nothing in Varve sets a GC mode.
