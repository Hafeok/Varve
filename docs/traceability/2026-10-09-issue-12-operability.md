# Milestone Operability: a server somebody can run

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt below is the
> brief as given, abridged where it repeats the roadmap; the maintainer's
> approval message is verbatim. The transcript itself is held by the
> maintainer.

| | |
|---|---|
| **Issue** | [#12](https://github.com/Hafeok/Varve/issues/12), milestone Operability; [#61](https://github.com/Hafeok/Varve/issues/61) closed with it; [#35](https://github.com/Hafeok/Varve/issues/35) measured and left open |
| **Date** | 2026-10-09 |
| **Tool** | Claude Code 2.1.295, a cloud session started from the desktop app |
| **Model** | `claude-fable-5-1`, configured and served, from the session's own metadata |
| **Session identifier** | `session_017bvHPyUJm8PB3FPctbwbHq` |
| **Branch** | `claude/eloquent-cannon-rb29y9`, from `main` at d02e0f1 |
| **Machine** | a cloud container: Intel Xeon @ 2.10 GHz, 4 logical cores, 15 GiB, Ubuntu 24.04; .NET SDK 10.0.401; no Docker daemon |

## The prompt

"Session O: operability", the milestone brief: read the roadmap's
Operability and 1.0 sections, ADRs 0037, 0082 and 0092–0109, the 6c record's
host-configuration findings, issues #12, #35 and #61 and `docs/operator/`;
`AGENTS.md` applies in full; plan first and wait for approval; one pull
request, red only on `CS0618`; the descriptor `releases/v0.1.0-preview.3.yaml`
with `issue: 12` and the ADRs it ships; land through `land/` (ADR 0102); do
not touch `Varve.Turtle`, the conformance fixtures or `docs/spec/turtle.md`
(session 6b's); merge `main` before close-out. Decisions A.1–A.11: the
shipped runtime configuration and the soak gate amended to judge under it
(closing #61); the container image on GHCR with attestations; telemetry in
two layers; `/health/live` and `/health/ready`; governance limits with
problem types; configuration precedence, `--print-config`, unknown keys
refused; graceful shutdown's lease policy and `varve lease --break`;
`Varve.Aspire`; API alignment while everything is preview; headers and a
problem catalogue under `docs/problems/`; the #35 `INSERT DATA` fast path at
or below pyoxigraph's 392 ms. B: build everything and complete the operator's
guide. C: the definition of done, with the container running the W3C
protocol suites and the auth legs from the image CI built, a readiness
property, governance properties, a telemetry collector test and an
allocation test, the one-hour soak under the shipped configuration with the
default runtime's hour beside it, the #35 benchmark with pyoxigraph in the
same run, the Aspire sample in CI, and `gh attestation verify`. The report:
this record and the pull request body with the ADR numbers, the image
digest, the attestation output, the soak tables, the #35 numbers, every
limit with its default and its problem type, and what 1.0 still needs.

The maintainer's approval of the plan, verbatim:

> Approved: 1 (your refinement), 2, 3, 6, 8, 9. 2, addition: re-run the 7a
> protocol benchmark under server GC and under the shipped workstation
> configuration, side by side in the report; the ADR records the throughput
> cost of the memory gate, or that there is none. 5, reversed: keep 404 for
> the horizon. RFC 9110 §15.5.9 reserves 410 for conditions likely to be
> permanent and says 404 otherwise; archiving is undecided, so 0096 stands
> and the problem type keeps its horizon member. Drop 9(d) from 0118. The
> w3id redirect pull request is mine; tell me when docs/problems/ is on the
> branch.

## The report

### What shipped, by ADR

Eleven ADRs, 0110–0120, filed unaccepted (ADR 0066), with eight dated
amendments to 0082, 0092, 0093, 0094, 0096, 0097, 0101 and 0106. The pull
request is red on `CS0618` alone, by design, until the maintainer accepts
them.

| ADR | What it decided | Where it is |
|---|---|---|
| 0110 | Workstation concurrent GC, `ConserveMemory` 5, shipped in the tool and the AOT binary; the soak gate judged under it | `src/Varve.Server/Varve.Server.csproj`; the soak tables below |
| 0111 | `ghcr.io/hafeok/varve`, Native AOT per architecture on the chiseled base, non-root, read-only, `pr-<sha>-<arch>` from CI, provenance and SBOM attested on release | `src/Varve.Server/container/Dockerfile`; `ci.yml` `container (linux-x64)`, `container (linux-arm64)`; `publish.yml` `image`, `manifest` |
| 0112 | `ActivitySource Varve.Protocol` and `Meter Varve.Store` in the libraries, the OpenTelemetry SDK in the server alone, built only when an `OTEL_*` endpoint is set | `src/Varve.Protocol/Telemetry.cs`, `src/Varve.Store/StoreMetrics.cs`, `src/Varve.Server/Telemetry.cs`; `docs/operator/observe.md` |
| 0113 | `/health/live`, `/health/ready` as a property of the datasets' states and lag, rate-limited, false during the drain | `src/Varve.Server/Readiness.cs`, `ServerHost.cs` |
| 0114 | Every limit answered: query memory by counting rows, as-of distance, live tails per client, concurrent reads with a queue, paged commits | `src/Varve.Sparql.Evaluation/MemoryBudget.cs`, `src/Varve.Protocol/ProtocolLimits.cs`, `Http/Reads.cs`, `Endpoints/LiveTails.cs` |
| 0115 | File, environment, command line, later winning; typed `serve` options; `--print-config`; unknown keys refuse | `src/Varve.Server/ServerHost.cs`, `SettingsCheck.cs`, `Commands/Serve.cs` |
| 0116 | A held lease is waited for, never broken; `varve lease`, `--break` for a dead holder on a filesystem that did not release | `src/Varve.Server/OpenDatasets.cs`, `Commands/Lease.cs` |
| 0117 | `Varve.Aspire`: `AddVarve`, `WithOidc`, `WithDataset`; the sample AppHost tested in CI | `src/Varve.Aspire/`, `samples/` |
| 0118 | State, settings and commits are resources; `PUT` of a dataset is idempotent; old paths removed | `src/Varve.Protocol/Endpoints/AdminEndpoints.cs`, `CommitsEndpoint.cs` |
| 0119 | `Vary`, `Varve-Request-Id`, `Link`, `Cache-Control` by read kind, `Last-Modified`; the problem catalogue and `docs/problems/` | `src/Varve.Protocol/Model/ProblemCatalogue.cs`, `Http/HttpProblems.cs`, `docs/problems/` |
| 0120 | `INSERT DATA` and `DELETE DATA` alone go straight to the commit request | `src/Varve.Sparql.Store/RequestExecution.cs` |

### Where the work departed from the filing, and why

- **Store metrics are synchronous gauges, not observable ones** (0112). A
  meter per dataset with observable callbacks held every dataset for the
  life of the process's meter; the fault-injection suite, which abandons
  datasets by the hundred, ran out of memory. One meter per process, the
  dataset as a tag, the gauges recorded per commit, per checkpoint and per
  maintenance step.
- **`WithDataset` declares the dataset in the configuration** (0117) rather
  than calling the admin API once the server is ready: the server creates a
  configured dataset at start when it is absent and opens it when present,
  which is idempotent and needs no token.
- **`Hex1b`**, a dependency of `Aspire.Hosting`, ships native binaries.
  `Varve.Aspire` references it with `ExcludeAssets="native"`, the gate no
  longer counts NuGet's `_._` placeholder, and the ADR says so; the
  maintainer may prefer another answer.
- **Kestrel sends no HTTP/1.1 response trailers.** A memory limit met after
  the first byte ends the response with an abort over HTTP/1.1 and a trailer
  over HTTP/2; ADR 0114 says so, and the test host has an HTTP/2 cleartext
  option.
- **The protocol suites against the container** needed a server subject in
  the conformance harness: `VARVE_SERVER_URL` moves the manifests' paths and
  the store's address under the server's dataset, one dataset closed,
  deleted and created per case. Verified here against a locally running
  server before CI: every case the memory subject passes, the server passes.
- **`conforms-to-schema` had regressed** when the service description
  renamed `varve:changeFeed` to `varve:commits`; the shapes fixture now says
  `commits`, and the ratchet holds.

### Measured

{{MEASURED}}

### Every limit, its default and its problem type

| Limit | Key | Default | Problem |
|---|---|---:|---|
| Query timeout | `Limits:QueryTimeout` | 30 s | `request-timeout` |
| Result size | `Limits:ResultSizeCap` | 1 GiB | `result-too-large` |
| Request body | `Limits:MaxRequestBody` | 100 MiB | `413 request-too-large` |
| Pinned read lifetime | `Limits:PinnedReadLifetime` | 2 min | the read is cut |
| Concurrent reads | `Limits:MaxConcurrentReads`, `ReadQueueLength` | 64, 256 | `503 server-busy`, `Retry-After` |
| Query memory | `Limits:MaxQueryMemory` | 256 MiB | `422 memory-limit-exceeded` |
| As-of distance | `Limits:MaxAsOfDistance` | 10,000 | `422 as-of-distance-exceeded` |
| Live tails per client | `Limits:MaxLiveTailsPerClient` | 16 | `429 too-many-live-tails` |
| Commits page | `Limits:CommitsPageSize` | 1,000 | `Link rel="next"` |
| Health probes | `Health:RateLimit` | 60 a minute | `429 too-many-requests` |
| Lease wait | `Lease:WaitFor` | 30 s | the dataset is failed, naming the holder |

### What this environment could not do

No Docker daemon: the image, the `container` jobs, the Aspire sample's test
and `gh attestation verify` run on CI alone. The image digest and the
attestation output are the first `manifest` run's, on the release's tag;
the pull request body says so in place of them. The first push of the image
creates a private package on GHCR, which the maintainer makes public once.

### What 1.0 still needs

Of the roadmap's 1.0 definition after this milestone: the public API freeze
(`PublicAPI.Shipped.txt` as the contract), package-manager distribution of
the CLI (`winget`, Homebrew), the docs site with Mermaid, erasure mode's
review, and the acceptance of ADRs 0110–0120. Signed multi-arch images with
an SBOM, the operator guide with the complete Entra and generic-issuer
examples, and the soak gate under the shipped configuration are done here.
The soak's dataset is bounded by its vocabulary and not by a retention
policy; a retention or archiving decision (ADR 0096's horizon) is still
open, and #35 stays open until the median meets pyoxigraph's.

### Commits

{{COMMITS}}

Developed with AI assistance under human review.
