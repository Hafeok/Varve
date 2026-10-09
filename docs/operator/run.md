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

- **The container image**, `ghcr.io/hafeok/varve` (ADR 0111): the Native AOT
  binary on the chiseled `runtime-deps` base, one image per architecture
  (`linux/amd64`, `linux/arm64`) joined into one manifest, with build
  provenance and an SPDX SBOM attested through GitHub. See
  [The container](#the-container).

Nothing is published to a registry yet; until it is, build from the
repository with `dotnet publish src/Varve.Server -c Release`, or build the
image with `src/Varve.Server/container/Dockerfile` from that output.

## The container

```sh
docker run --detach --name varve \
  --read-only \
  --volume varve:/var/lib/varve \
  --publish 8080:8080 \
  --env VARVE__AUTH__MODE=Oidc \
  --env VARVE__AUTH__AUTHORITY=https://login.example/ \
  --env VARVE__AUTH__AUDIENCES__0=api://varve \
  --env VARVE__DATASETS__PEOPLE__STORAGE=File \
  ghcr.io/hafeok/varve:v0.1.0-preview.3 serve
```

What the image fixes, and what it leaves to you:

- It runs as the non-root `app` user (uid 1654) and writes nothing outside
  the datasets root, so `--read-only` holds; the pull request's CI runs the
  W3C protocol suites and both auth legs against the container that way.
- `Varve:DatasetsRoot` is `/var/lib/varve` in the image, by
  `VARVE__DATASETSROOT`; mount a volume there. Datasets are directories under
  it, discovered at start (ADR 0106) or created through the admin API.
- It listens on 8080 (`ASPNETCORE_URLS=http://+:8080`); TLS is the proxy's.
- The entry point is the executable; the arguments are its command line:
  `serve` and its options, or any `varve` command. Configuration is the
  `VARVE__*` environment, as everywhere.
- There is no `HEALTHCHECK` (ADR 0111): probe `GET /health/ready` from the
  orchestrator, at the interval it decides.
- Stop with the runtime's stop signal and give it the shutdown timeout
  (`docker stop --time 30`): the drain finishes requests in flight, ends
  live tails and closes the datasets.

Verify what you pulled: the manifest's digest carries a build provenance
attestation and an SBOM, signed with the publishing workflow's identity.

```sh
gh attestation verify oci://ghcr.io/hafeok/varve:v0.1.0-preview.3 --owner hafeok
```

Tags are the release descriptor's version, and `latest` for a version with
no prerelease part. A `pr-<sha>-<arch>` tag is a pull request's own build
for its suites, not a release.

## With .NET Aspire

`Varve.Aspire` (ADR 0117) adds the same container to an AppHost:
`builder.AddVarve("varve").WithOidc(…).WithDataset("people", …)`, with the
volume, the endpoint, the health check and OTLP to the dashboard wired.
`src/Varve.Aspire/README.md` has the three methods; `samples/` runs it beside
a test issuer. Without `WithOidc` the server is anonymous in run mode and
refuses to publish.

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
| `GET /health/live` | liveness: `200` once the process serves (ADR 0113) |
| `GET /health/ready` | readiness: `200` when every dataset that should be open is open, not failed and within `Varve:Health:ReadyLag` of its head, `503 not-ready` with each dataset's state and reason otherwise, and during the drain |
| `/datasets` | the admin API: list, create (`PUT`, idempotent), delete; `/datasets/{name}/state` reads and sets `open` or `closed` (ADRs 0106, 0118) |
| `/datasets/{name}/sparql` | SPARQL 1.1 Protocol query and update; `GET` with an RDF `Accept` is the service description |
| `/datasets/{name}/graphs` | the Graph Store Protocol |
| `/datasets/{name}/commits`, `/commits/{position}`, `/diff` | the commits resource, a range or a live tail, one commit, and the diff (`docs/spec/change-feed.md`, ADR 0118) |
| `/datasets/{name}/status`, `/settings`, `/checkpoints` | the dataset's status, its settings (`GET`, `PUT`, `PATCH` merge patch) and its checkpoints |

Both probes are rate-limited per client address (`Varve:Health:RateLimit`),
and every refusal anywhere is an RFC 9457 problem whose type is one of
`docs/problems/` (ADR 0119).

## Stop it

`SIGTERM` (or `Ctrl-C`) drains: requests in flight finish, live feeds end with
a `shutdown` event, datasets close, and the process exits 0 (ADR 0101). A
container runtime's stop signal is enough; give it the ASP.NET Core shutdown
timeout (30 s by default) before it kills.

## As a systemd service

The Native AOT binary, a dedicated user, the datasets root on a path that
user owns, and the configuration in the unit's environment or a file
`--config` names:

```ini
# /etc/systemd/system/varve.service
[Unit]
Description=Varve
After=network-online.target
Wants=network-online.target

[Service]
User=varve
Group=varve
ExecStart=/usr/local/bin/varve serve --config /etc/varve/varve.json --urls http://127.0.0.1:8080
Environment=VARVE__DATASETSROOT=/var/lib/varve
Environment=OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318
Restart=on-failure
TimeoutStopSec=40
KillSignal=SIGTERM
StateDirectory=varve
ProtectSystem=strict
ReadWritePaths=/var/lib/varve
NoNewPrivileges=true

[Install]
WantedBy=multi-user.target
```

`TimeoutStopSec` is longer than the host's shutdown timeout (30 s), so a
drain is never cut short by systemd. `ProtectSystem=strict` with
`ReadWritePaths` on the datasets root is the same property the container
proves: the server writes nowhere else. `journalctl -u varve` is the log.

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
