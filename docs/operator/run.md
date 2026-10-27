# Run

## Install

The same executable comes three ways (ADRs 0105, 0111):

- **The .NET tool** on nuget.org, framework-dependent, for a machine with the
  .NET 10 SDK or runtime. While every release is a preview, the install
  names `--prerelease` or a `--version`:

  ```sh
  dotnet tool install --global Varve.Server --prerelease
  varve --version
  ```

- **The container image**, `ghcr.io/hafeok/varve` (ADR 0111): the Native AOT
  binary on the chiseled `runtime-deps` base, one image per architecture
  (`linux/amd64`, `linux/arm64`) joined into one manifest, with build
  provenance and an SPDX SBOM attested through GitHub. See
  [The container](#the-container).

- **The Native AOT single file**, for a machine with nothing installed:
  `Varve.Server` on Linux and macOS, `Varve.Server.exe` on Windows. No
  release carries it as a download; build it on the platform it is for, with
  `dotnet publish src/Varve.Server -c Release -r <rid>` (CI builds
  `linux-x64`, `linux-arm64` and `win-x64`), and rename it `varve` or call it
  by its name; the commands are the same. The image is this binary.

**Verify what you install.** Every package, the tool among them, and the
image carry two attestations, both keyless:
- the build provenance: which workflow, at which commit;
- an SPDX SBOM of exactly what they contain.

Each release attaches the packages as attested, their `SHA256SUMS`, every
SBOM and the VEX document. Before installing the tool, for example:

```sh
gh release download v0.1.0-preview.16 --repo Hafeok/Varve --pattern 'Varve.Server.*'
gh attestation verify Varve.Server.0.1.0-preview.16.nupkg --owner hafeok
gh attestation verify oci://ghcr.io/hafeok/varve:v0.1.0-preview.16 --owner hafeok
```

[`docs/supply-chain.md`](../supply-chain.md) has the rest: what each SBOM
lists, the package nuget.org serves (the same package under nuget.org's own
signature), and the vulnerability process.

## The container

```sh
docker run --detach --name varve \
  --read-only \
  --volume varve:/var/lib/varve \
  --publish 8080:8080 \
  --env VARVE__AUTH__MODE=Oidc \
  --env VARVE__AUTH__AUTHORITY=https://login.example/ \
  --env VARVE__AUTH__AUDIENCES__0=api://varve \
  --env VARVE__MANAGE__BOOTSTRAPOPERATORS__0=Varve.Operator \
  ghcr.io/hafeok/varve:v0.1.0-preview.3 serve
```

Then declare its datasets with `varve apply` ([Datasets](datasets.md)).

What the image fixes, and what it leaves to you:

- It runs as the non-root `app` user (uid 1654) and writes nothing outside
  the datasets root, so `--read-only` holds; the pull request's CI runs the
  W3C protocol suites and both auth legs against the container that way.
- `Varve:DatasetsRoot` is `/var/lib/varve` in the image, by
  `VARVE__DATASETSROOT`; mount a volume there. A declared `File` dataset is
  a directory under it, and the fleet's system dataset is `_fleet/` there.
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
`builder.AddVarve("varve").WithOidc(…).WithOperators(…).WithDataset("people", …)`,
with the volume, the endpoint, the health check and OTLP to the dashboard
wired, and the datasets declared by `varve apply` from a container of the
same image. `src/Varve.Aspire/README.md` has the methods; `samples/` runs it beside
a test issuer. Without `WithOidc` the server is anonymous in run mode and
refuses to publish.

## Start the server

`varve serve`, a bare `varve`, or any command line of nothing but
configuration runs the server:

```sh
varve serve --Varve:DatasetsRoot=/var/lib/varve \
            --Varve:Manage:BootstrapOperators:0=Varve.Operator \
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

**Datasets are declared, not configured** (ADR 0163). A server starts with
the datasets its fleet's management plane declares, and an empty one starts
with none; `varve apply` or `PUT /manage/datasets/{name}` declares one, and
every instance converges to it. A `File` dataset lives under
`Varve:DatasetsRoot`, one directory each, named after the dataset. One that
fails to open is listed as failed with a reason, tried again on every round,
and the server still serves the others. A directory under the root that is
not declared is not served. The removed `Varve:Datasets` and
`Varve:Auth:Datasets` refuse to start; `varve apply --from-config` prints the
TriG that replaces them.

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
| `GET /health/ready` | readiness: `200` when every dataset that should be open is open, not failed and within `Varve:Health:ReadyLag` of its head, and the management plane is applied within `Varve:Fleet:AllowedLag`, not superseded, with the bootstrap operators recorded as configured; `503 not-ready` otherwise and during the drain. Both answers carry a `management` object — `role`, `epoch`, `position`, `lag`, `bootstrap` — and each dataset's state, role and lag (ADR 0163 point 13) |
| `/manage/…` | the management plane, with a token for its own audience: datasets declared as TriG, their settings, state, status, checkpoints and grants, the fleet graph, `apply`, `sparql` and `system` ([Datasets](datasets.md#the-management-resources), ADR 0163) |
| `/datasets/{name}/sparql` | SPARQL 1.1 Protocol query and update; `GET` with an RDF `Accept` is the service description, whose Varve terms [`docs/vocabulary.md`](../vocabulary.md) defines |
| `/datasets/{name}/graphs` | the Graph Store Protocol |
| `/datasets/{name}/commits`, `/commits/{position}`, `/diff` | the commits resource, a range or a live tail, one commit, and the diff (`docs/spec/change-feed.md`, ADR 0118) |

A dataset's status, settings, state and checkpoints are management resources
now, under `/manage/datasets/{name}/` (ADR 0163). Every response carries
`Varve-Management-Position`, and in a fleet a write is forwarded to its
writer ([The fleet](fleet.md)).

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
varve setup apply people.yaml https://varve.example/ --token "$TOKEN"
```

`varve setup apply` takes a setup file and the server's address
([Datasets](datasets.md)). A token from the issuer is renewed before it
expires, so a long `feed --follow` outlives one token (ADR 0127).

[Authenticate](authenticate.md) says where the token comes from. A directory
must not be opened by a command while a server holds it: the lease refuses
the second opener (ADR 0075), so stop the server, or close the dataset through
the admin API, before `load`ing into its directory.
