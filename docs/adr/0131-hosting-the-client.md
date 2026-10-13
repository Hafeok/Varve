# 0131 — Hosting the client: `AddVarve`, `varve apply`, and `WithDataset`'s setup

## Status

**Accepted — filed unaccepted by milestone Client and integration of #93,
2026-10-13** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the Client and integration plan:
- `AddVarve` lives in `Varve.Protocol.Client`, with two
  `Microsoft.Extensions` abstractions packages admitted;
- `varve apply` reads the setup file's YAML subset without a package;
- `Varve.Aspire` runs `varve apply` from the server's image.

**Amends** ADR [0117](0117-varve-aspire.md), whose deferred client integration
this is, by a dated block (ADR 0068).

## Context

ADR 0130 makes a dataset's setup a value an application declares. An
application on the .NET generic host wants three things:
- the client registered once;
- the setup applied before it serves anything;
- startup to fail loudly when the setup cannot be applied.

The same setup is also wanted outside an application: from a deployment
pipeline (`varve apply`), and from an Aspire AppHost that declares the dataset.
The maintainer asked for one class behind all three, so that "applied" means
the same thing everywhere.

`AddVarve` needs `IServiceCollection` and `IHostedService`. No library in
`src/` references `Microsoft.Extensions.*` as a package today:
`Varve.Protocol` and `Varve.Server` see them through the ASP.NET Core shared
framework. A separate hosting package is not available either. A library
referencing the client (layer 5) would have to be layer 6, and layer 6 is
reserved to executables (ADR 0060).

## Decision

1. **`AddVarve(this IServiceCollection, Action<VarveClientOptions>)`** in
   `Varve.Protocol.Client`, namespace `Varve.Protocol.Client.Hosting`.
   - **The options:**
     - the server's address;
     - the dataset's name;
     - a token provider factory over `IServiceProvider` (ADR 0127);
     - the limits;
     - optionally a `DatasetSetup`, and whether to apply it at start (on by
       default when given).
   - **What it registers:**
     - a `SparqlHttpClient` singleton over an `HttpClient` from `OutboundHttp`;
     - when a setup is given, `DatasetSetupService`, an `IHostedService` whose
       `StartAsync` calls `ApplyAsync` and lets its `SetupException` escape.
   - **A failed apply fails startup** with the step, the status and the problem
     body in the message.
   - It shares a name with `Varve.Aspire`'s `AddVarve` on
     `IDistributedApplicationBuilder`. The receivers differ and the namespaces
     differ, so no call is ambiguous. One name for "add Varve" in either
     builder is the familiar pattern.
2. **Two packages are admitted for `Varve.Protocol.Client`**, under constraint
   4:
   - `Microsoft.Extensions.DependencyInjection.Abstractions` (MIT);
   - `Microsoft.Extensions.Hosting.Abstractions` (MIT).

   Both are managed, AOT-annotated and trimming-safe, ship no native asset,
   and are what every .NET host already loads. The versions are resolved from
   NuGet into the register. Nothing else in `src/` may reference them without
   its own decision.
3. **`varve apply <setup.yaml>`**, a command of the CLI (ADR 0105).
   - It takes the target options every remote command takes (the server's URL
     and the token sources).
   - It reads the file with `DatasetSetupFile.Read` (ADR 0130), and calls the
     same `ApplyAsync`.
   - It prints what committed. It exits 1 with the problem on a refusal, and 2
     with the line and column on a file outside the subset.
4. **`Varve.Aspire`: `WithDataset(…, setup: "path/to/setup.yaml")`.**
   - `Varve.Aspire` references nothing from Varve (layer 0, ADR 0117), so it
     cannot call `ApplyAsync`.
   - Instead it adds a container resource from the server's own image that
     runs `varve apply` once against the server. It waits for the server to
     be healthy, bind-mounts the file's directory read-only, and is named
     `{server}-setup-{dataset}`.
   - The application resource can `WaitForCompletion` on it.
   - Under `WithOidc`, `WithSetupClient(clientId, clientSecret)` gives the
     command its client-credentials identity. In anonymous run mode it needs
     none.
   - So all three entry points run one class: `ApplyAsync`.

## Alternatives considered

- **A `Varve.Protocol.Client.Hosting` package.** Forbidden by ADR 0060 as the
  Context says, short of superseding the layer rule for one extension method.
- **Applying the setup in the server at start**, from configuration. The
  server would gain a second way to write data, with its own identity, and
  "applied" would mean something different from what an application does.
- **`IHttpClientFactory` (`Microsoft.Extensions.Http`).** A third package for
  handler pooling the client does not need: one `HttpClient` per registration
  is the pattern ADR 0103 already uses.
- **YamlDotNet in the CLI.** See ADR 0130. Its reflection-based deserialiser
  is not AOT-safe without a source generator, and the subset is small.

## Consequences

- `Varve.Protocol.Client`'s closure gains two abstractions assemblies. A
  browser or console consumer that never calls `AddVarve` still loads them,
  and they are small.
- The sample applications and the AppHost sample use `AddVarve` and
  `WithDataset(setup:)`.
- Object storage (#92) also changes `Varve.Aspire`. The milestone that lands
  second reconciles it.

## Checks

- **Checked against the accepted ADRs** (0001–0130). Touches **0009** (the
  register), **0060** (why it is not a separate package), **0103**, **0105**
  (a new command), **0117** (amended). No conflict.
- **Layer ownership.** `Varve.Protocol.Client`, layer 5. `varve apply` is
  `Varve.Server`'s, layer 6. `Varve.Aspire`, layer 0 by dependency.
- **Analyzer rule.** None.
- **Open questions owned.** None.
