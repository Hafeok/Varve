# 0117 — `Varve.Aspire`: the hosting integration

## Status

**Accepted — filed unaccepted by milestone Operability of #12, 2026-10-09**
(ADR 0066). Decided by the maintainer on the Operability plan. Acceptance is
the maintainer's act on the pull request. Admits `Aspire.Hosting` to the
register in `Varve.Aspire` alone, and `Aspire.AppHost.Sdk` and
`Aspire.Hosting.Testing` in the sample and its test.

## Context

ADR 0037 expected the server to run under Aspire in development and said
the Aspire hosting integration enables anonymous mode in development and
never in publish. The 7b report put the package's placement to the
maintainer: a hosting-side package referencing `Aspire.Hosting`, a
composition-root concern, not in `Varve.Protocol.Client`.

An Aspire hosting integration references `Aspire.Hosting` and describes a
resource; it does not run Varve's code. A client integration (a
`builder.AddVarveClient(...)` for the consuming application) would wrap
`Varve.Protocol.Client`; it is not built here, because `SparqlHttpClient`
over an `HttpClient` the application already configures is the integration.

## Decision

1. **`Varve.Aspire`, a packable library** that references `Aspire.Hosting`
   13.6.1 and **nothing from Varve**. By dependency it is a layer-0 package:
   it reaches no Varve assembly, so `ArchLayer` is `0` and every reference
   rule holds trivially. By purpose it is a host concern: it names the image
   of ADR 0111 and the configuration of ADRs 0101 and 0115, and it changes
   when they change. The ADR says both, so that nobody reads layer 0 as
   "foundation" here; it is registered under constraint 4 with this ADR.
2. **`builder.AddVarve("varve")`** adds the container resource:
   `ghcr.io/hafeok/varve` at the version of this build, the datasets volume
   at `/var/lib/varve`, port 8080 as the `http` endpoint, OTLP wiring
   (`OTEL_EXPORTER_OTLP_ENDPOINT` and the headers from the AppHost's
   dashboard, as Aspire gives every resource), `GET /health/ready` as the
   health check (ADR 0113), and the OIDC settings as parameters:
   `WithOidc(authority, audience, …)` sets `Varve:Auth`, and without it the
   resource runs **anonymous in run mode and refuses to publish**: the
   manifest step fails with a message naming `WithOidc`, which is ADR 0037
   point 4 made executable.
3. **`.WithDataset(name, read, write, admin)`** declares the dataset in the
   server's configuration (`Varve:Datasets:{name}:Storage=File`) with the
   roles that may read, write and administer it (ADR 0107): the server
   creates a configured dataset under its root at start when it is not
   there and opens it as it is when it is (ADR 0106), so the step is
   idempotent and needs no token. The admin API's `PUT` (ADR 0118) stays
   the way to add a dataset to a running server.
4. **A sample AppHost under `samples/Varve.Aspire.Sample/`** runs the server
   with the mock issuer of ADR 0100's layer (b) as a second container,
   `WithOidc` pointing at it, and one dataset. Its test
   (`samples/Varve.Aspire.Sample.Tests/`, `Aspire.Hosting.Testing`) starts
   the AppHost, waits for the resource to be healthy, obtains a token from
   the mock by client credentials, and passes one query and one update. It
   runs in CI on Linux, where Docker is.
5. **The sample is not shipped.** `Aspire.AppHost.Sdk`'s orchestration and
   dashboard packages carry platform binaries; the sample and its test are
   outside `Varve.slnx`'s packable set, so ADR 0009's amendment on shipped
   artefacts covers them as it covers the benchmark harness, and
   `eng/native-assets.cs` never sees them. `Varve.Aspire` itself references
   `Aspire.Hosting`, and the gate checks its closure. One dependency of
   `Aspire.Hosting`'s, `Hex1b`, the Aspire CLI's terminal interop, ships
   native binaries for every platform. **`Varve.Aspire` is on
   `eng/native-assets.cs`'s allow-list, citing this ADR**, for the reason the
   list records: it is an AppHost-only package, outside the three hosts of
   constraint 1 (desktop, Native AOT, the browser), and Aspire's natives are
   in every AppHost regardless of Varve, so excluding them from our
   reference would remove nothing from any process. The gate still prints
   the allowed project's native packages, so that a new one is seen, and an
   entry whose project ships no native asset fails as stale. No library the
   three hosts load is on the list.

## Alternatives considered

- **`Varve.Aspire.Hosting` at layer 6 beside `Varve.Server`.** Layer 6 is
  "every executable, and only executables" (ADR 0060); this is a library.
- **In `tools/`.** It is shipped on NuGet, which `tools/` is not.
- **A client integration too.** Deferred: `SparqlHttpClient` over the
  application's `HttpClient` needs no registration helper yet.

## Consequences

- Fourteen packable projects; the README's package table gains a row with
  layer 0 and the note.
- A new solution folder `samples/`; CI gains the `aspire sample` job.

## Checks

- **Checked against the accepted ADRs** (0001–0109). Touches **0009** (the
  register and its amendment), **0037** (point 4 built), **0060** (layers),
  **0106** (the admin call), **0111** (the image). No conflict.
- **Layer ownership.** `Varve.Aspire`, declared 0, a host concern by purpose.
- **Analyzer rule.** None.
- **Open questions owned.** None.
