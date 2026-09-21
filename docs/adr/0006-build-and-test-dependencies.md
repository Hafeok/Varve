# 0006 — Build-time and test-time dependencies

## Status

Superseded by [0009](0009-dependency-policy-and-register.md). 2026-09-21.

Accepted 2026-09-20. Superseded because an ADR that enumerates every package
will be amended forever, and because constraint 4 needed to be a gate rather
than a document. The package table below is a historical snapshot; the live
register is `Directory.Packages.props`.

## Context

`docs/brief.md`, constraint 4: minimal dependencies, prefer the BCL, every
third-party package needs an ADR. The brief is explicit that this covers
build-time packages too — "the Roslyn packages they need are build-time
dependencies and still get an ADR under constraint 4".

Constraint 1 applies separately and absolutely: if a dependency ships a native
asset, it is out. That check is on the package, not on how we intend to use it.

This ADR covers every package milestone 1 introduces. Each version below was
resolved from nuget.org on 2026-09-20 and is pinned centrally in
`Directory.Packages.props`. Project files carry no `Version` attribute.

## Decision

### Build-time — analyzer authoring

| Package | Version | Why the BCL does not suffice |
|---|---|---|
| `Microsoft.CodeAnalysis.CSharp` | ~~5.9.0~~ **5.0.0** | The compiler API. There is no BCL substitute for writing a Roslyn analyzer; this *is* the extension point. `PrivateAssets="all"`, and the analyzer project does not ship it. *(Changed 2026-09-21: taking the latest was the wrong policy for this package specifically — see the amended "Version choice" below. The policy it implies is stated in the ADR that supersedes this one.)* |
| `Microsoft.CodeAnalysis.Analyzers` | 5.9.0 | The `RS*` rules that check analyzer code itself — correct `Initialize` shape, release tracking (`RS2008`), the `CompilationEnd` tag (`RS1037`). Writing an analyzer without these is writing one that misbehaves in the IDE in ways that do not reproduce on the command line. |

**Version choice.** *Amended 2026-09-21. The original text is kept below with
the reason it was wrong, because the mistake is the instructive part.*

~~SDK 10.0.401 hosts Roslyn `5.9.0-1.26423.113`, which sorts *below* stable
`5.9.0`, so referencing 5.9.0 could in principle raise `CS9057` — an analyzer
built against a newer compiler than the host. It was tested before this ADR was
written: a 5.9.0 analyzer loads and reports under SDK 10.0.401 without
`CS9057`. Stable 5.9.0 therefore stands, per the rule that a version is
resolved rather than remembered.~~

**That test proved less than it appeared to.** `CS9057` compares *assembly*
versions, and a `5.9.0-1.x` host shares its assembly version with the 5.9.0
stable package — so the check could not have fired, on any input, and the green
result carried no information about the case that matters. The case that
matters is an *older* host, which the command line never sees because
`global.json` pins the SDK, and which an IDE does see because its compiler
comes from the IDE.

The rule "resolve the latest stable version" is wrong for this package. The
correct value is a **floor**: the Roslyn line shipped with the first .NET 10
SDK, 10.0.100, which is `5.0.0-2.25523.111`, and therefore stable **5.0.0**.
Verified on 2026-09-21 by building the analyzer against 5.0.0 and compiling a
consumer under both SDK 10.0.100 and SDK 10.0.401: it loads and reports under
both, with no `CS9057`. `Microsoft.CodeAnalysis.CSharp.Workspaces` follows the
same floor, so the test harness cannot pass against an API the shipped analyzer
would not have.

`Microsoft.CodeAnalysis.Analyzers` and the testing package stay on latest: they
run in our build, never in a consumer's host, so the floor does not apply to
them.

The general policy this implies — that "latest" is the default and a floor is a
named exception with a stated reason — belongs in a policy ADR rather than in a
package table, and is stated in the ADR that supersedes this one.

### Build-time — off-the-shelf enforcement

| Package | Version | Why the BCL does not suffice |
|---|---|---|
| `Microsoft.CodeAnalysis.PublicApiAnalyzers` | 5.6.0 | Tracks the public API surface in `PublicAPI.{Shipped,Unshipped}.txt` so that adding a public member is a reviewable diff line. Principle 2 requires the surface be "kept minimal and tracked"; nothing in the BCL tracks it. |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` | 5.6.0 | Enforces `eng/BannedSymbols.txt`. The alternative is writing `VARVE`-numbered rules for symbols Microsoft already lets us ban declaratively, which ADR 0004 rejects as a matter of policy. |

The trimming, AOT and single-file analyzers are **not** packages. They are in
the SDK, switched on by `IsAotCompatible`, and set to error in `.editorconfig`.
No dependency, so nothing to justify here beyond noting that the free option was
taken.

### Test-time

| Package | Version | Why the BCL does not suffice |
|---|---|---|
| `xunit.v3` | 4.0.1 | The test framework. `Microsoft.Testing.Platform` is a runner, not a framework, and the BCL has no assertion or discovery model. v3 chosen over v2 as the current line; it runs natively on MTP. |
| `Microsoft.Testing.Extensions.TrxReport` | 2.4.1 | Produces the TRX that `eng/ratchet.cs` reads. MTP does not build TRX in; the extension must be referenced explicitly or `--report-trx` fails the run with exit code 5. |
| `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` | 1.1.4 | Compiles a sample, runs an analyzer over it, and asserts the exact diagnostics with locations. Rebuilding this by hand means reimplementing `TestState`, `AdditionalProjects` and diagnostic matching — which is what ADR 0004 calls the deliverable for a rule. The framework-neutral `DefaultVerifier` is used, so this does not pull in an xUnit binding. |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | ~~5.9.0~~ **5.0.0** | *Added 2026-09-20, after this ADR was accepted, within the same milestone.* The testing package depends on it with a floating minimum of `1.0.1`, so without an explicit reference NuGet resolves a 2015-era Roslyn into the test project and the harness does not work. Referencing it explicitly pins the workspace layer to the same Roslyn the analyzer compiles against, which as of the 2026-09-21 amendment is the 5.0.0 floor. Build-time only; the analyzer project does not reference it. |
| `dotNetRdf.Core` | 3.5.2 | **Test-only, temporary.** The W3C manifests are Turtle and we have no Turtle parser. See ADR 0007 for the exit criterion. |

**`Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` are deliberately
absent.** `dotnet test` runs in MTP mode, selected in `global.json`:

```json
"test": { "runner": "Microsoft.Testing.Platform" }
```

MTP v2 on the .NET 10 SDK no longer supports running under VSTest, so this is
not only the smaller option but the supported one. Two test-time packages that a
.NET test project normally carries are therefore not here at all.

**One transitive pin.** `dotNetRdf.Core` asks for `AngleSharp` 1.4.0, which
NuGet audit flags as GHSA-pgww-w46g-26qg, and warnings are errors here. Central
transitive pinning raises it to 1.8.2. We never call into it — dotNetRDF uses
it for HTML and RDFa, and this repository reads Turtle manifests — but a
known-vulnerable package in the graph is not something to silence with
`NoWarn`. The pin leaves with `dotNetRdf.Core` at milestone 5. *(Recorded
2026-09-20, after this ADR was accepted, within the same milestone.)*

**`dotNetRdf.Core` deserves the scrutiny.** It ships no native asset, so
constraint 1 is satisfied. It brings twelve transitive managed dependencies —
`AngleSharp`, `HtmlAgilityPack`, `Newtonsoft.Json`, `VDS.Common` and eight
`System.*` compatibility facades — to read a Turtle file. That is a poor trade
on its merits, and the pin above is what it costs in practice, and it is accepted only because it is confined to a test project,
never referenced by anything packable, and has a stated end date. The meta-package
`dotNetRDF` was rejected in favour of `dotNetRdf.Core`: the meta-package adds
SHACL, SPIN, a Lucene full-text index and HTML schema writing, none of which
reads a manifest.

### Standing rules

- No package ships as a runtime dependency of a Varve package at milestone 1,
  because there are no Varve packages yet. When there are, the rule is that the
  published dependency list is empty unless an ADR says otherwise.
- `Varve.Analyzers` is referenced as an analyzer, never as a library, so neither
  it nor Roslyn appears in any consumer's dependency graph.
- A package that ships a native asset is refused, with no exception process.

## Alternatives considered

- **Writing the analyzer test harness ourselves.** Plausible for two rules, and
  it would remove `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`. Rejected:
  the part that is hard is not running the analyzer, it is building a *referenced
  project* in the test compilation so the rule has assembly metadata to read, and
  `TestState.AdditionalProjects` does exactly that. Hand-rolling it trades one
  build-time dependency for a piece of infrastructure we would then have to test.
- **NUnit or MSTest.** No material difference for this repository. xUnit v3 is
  the project prompt's default and nothing here argues against it.
- **VSTest mode** (`Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio`). Not
  available: MTP v2 refuses to run under VSTest on the .NET 10 SDK. Even if it
  were, it is two more packages for the same outcome.
- **Pre-converting the W3C manifests to N-Triples with an external tool** and
  checking the result in, removing `dotNetRdf.Core` entirely. Rejected in ADR
  0007: it puts a non-.NET tool in CI and makes the checked-in conversion a
  thing that can silently drift from the submodule.
- **A hand-written Turtle subset parser for manifests only.** Tempting, and it
  would be perhaps two hundred lines. Rejected: a parser written to read the
  files that judge our parser is a conflict of interest, and the moment it has a
  bug we would be debugging the harness instead of the implementation. The real
  `Varve.Turtle` replaces it, judged by the same suite.

## Consequences

Nine packages enter the repository and eight of them are permanent. The ninth,
`dotNetRdf.Core`, has an exit criterion in ADR 0007 and must be removed when it
is met; if it is still here after milestone 5, that is a defect to raise, not a
fact to accept.

`global.json` now pins both the SDK version and the test runner. A contributor
on a different SDK gets a clear error rather than a different test experience.

Central package management means a version bump is one line in
`Directory.Packages.props` and applies everywhere. It also means a project
cannot quietly pin a different version, which is the point.
