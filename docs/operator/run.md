# Run

## Install

Two artefacts carry the same executable (ADR 0105):

- **The .NET tool**, framework-dependent, for a machine with the .NET 10 SDK
  or runtime:

  ```sh
  dotnet tool install --global Varve.Server
  varve --version
  ```

- **The Native AOT single file**, from a release, for a machine with nothing
  installed: `Varve.Server` on Linux and macOS, `Varve.Server.exe` on Windows.
  Rename it `varve` or call it by its name; the commands are the same.

Nothing is published to a registry yet; until it is, build from the
repository with `dotnet publish src/Varve.Server -c Release`.

## Start the server

`varve serve`, a bare `varve`, or any command line of nothing but
configuration runs the server:

```sh
varve serve --Varve:DatasetsRoot=/var/lib/varve \
            --Varve:Datasets:people:Storage=File \
            --Varve:Auth:Mode=Oidc \
            --Varve:Auth:Authority=https://login.example/ \
            --Varve:Auth:Audiences:0=api://varve \
            --urls=http://0.0.0.0:8080
```

Configuration comes from `appsettings.json` in the working directory,
environment variables (`VARVE__AUTH__MODE=Oidc`, two underscores for each
colon) and command-line arguments, later sources overriding earlier ones.
[Configure](configure.md) lists every setting.

**A configuration that does not validate refuses to start**: the server
prints every error, one per line, and exits with code 2. It never substitutes
a default for a value that was given and is wrong.

Datasets configured as `File` live under `Varve:DatasetsRoot`, one directory
each, named after the dataset. A directory under the root that is not
configured is **discovered** and served too (ADR 0106); one that fails to
open is listed as failed with a reason, and the server still starts for the
others. `Varve:Datasets` may be empty when a root is given: the admin API
creates datasets under it.

## What it listens on

The ASP.NET Core defaults: `http://localhost:5000` unless `--urls` or
`ASPNETCORE_URLS` says otherwise. TLS is the reverse proxy's or Kestrel's by
the usual ASP.NET Core settings. Behind a proxy, turn
`Varve:ForwardedHeaders:Enabled` on and list the proxies in `KnownProxies`,
or the scheme and host in every IRI the server mints — the service
description's, a `POST`ed graph's — are the proxy's.

The routes, per dataset:

| Route | What |
|---|---|
| `GET /live` | liveness: `200` once the process serves |
| `GET /ready` | readiness: `200` when every dataset that should be open is open and not failed, `503` with each dataset's state and reason otherwise |
| `/datasets` | the admin API: list, create, open, close, delete (ADR 0106) |
| `/datasets/{name}/sparql` | SPARQL 1.1 Protocol query and update; `GET` with an RDF `Accept` is the service description |
| `/datasets/{name}/graphs` | the Graph Store Protocol |
| `/datasets/{name}/feed`, `/diff` | the change feed and the diff (`docs/spec/change-feed.md`) |
| `/datasets/{name}/status`, `/settings`, `/checkpoints` | the dataset's admin endpoints |

## Stop it

`SIGTERM` (or `Ctrl-C`) drains: requests in flight finish, live feeds end with
a `shutdown` event, datasets close, and the process exits 0 (ADR 0101). A
container runtime's stop signal is enough; give it the ASP.NET Core shutdown
timeout (30 s by default) before it kills.

## Logs

Standard output, through `Microsoft.Extensions.Logging`; the level and the
format are the usual `Logging` section. Anonymous mode logs a warning at
every start.

## The command line against a running server

Every command but `create`, `load` and `serve` takes the URL of a dataset on a
server in place of a directory:

```sh
varve query https://varve.example/datasets/people/ -q 'SELECT * WHERE { ?s ?p ?o } LIMIT 10' --token "$TOKEN"
varve feed  https://varve.example/datasets/people/ --from 0 --follow --authority https://login.example/ --client-id varve-cli
```

[Authenticate](authenticate.md) says where the token comes from. A directory
must not be opened by a command while a server holds it: the lease refuses
the second opener (ADR 0075), so stop the server, or close the dataset through
the admin API, before `load`ing into its directory.
