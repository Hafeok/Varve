# 0006 — Build-time and test-time dependencies

## Status

Accepted. 2026-09-20.

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
| `Microsoft.CodeAnalysis.CSharp` | 5.9.0 | The compiler API. There is no BCL substitute for writing a Roslyn analyzer; this *is* the extension point. `PrivateAssets="all"`, and the analyzer project does not ship it. |
| `Microsoft.CodeAnalysis.Analyzers` | 5.9.0 | The `RS*` rules that check analyzer code itself — correct `Initialize` shape, release tracking (`RS2008`), the `CompilationEnd` tag (`RS1037`). Writing an analyzer without these is writing one that misbehaves in the IDE in ways that do not reproduce on the command line. |

**Version choice.** SDK 10.0.401 hosts Roslyn `5.9.0-1.26423.113`, which sorts
*below* stable `5.9.0`, so referencing 5.9.0 could in principle raise `CS9057`
— an analyzer built against a newer compiler than the host. It was tested
before this ADR was written: a 5.9.0 analyzer loads and reports under SDK
10.0.401 without `CS9057`. Stable 5.9.0 therefore stands, per the rule that a
version is resolved rather than remembered.

The cost of taking the latest is real and worth naming: an analyzer referencing
Roslyn *n* requires a host at *n* or later, so contributors on an older SDK or
an older Visual Studio will see the analyzer silently not load. `global.json`
pins the SDK, which makes the command-line case deterministic. It does not
constrain the IDE. If that becomes a problem the fix is to step the reference
down to the oldest Roslyn whose API we use — a superseding ADR, not a quiet edit.

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
| `dotNetRdf.Core` | 3.5.2 | **Test-only, temporary.** The W3C manifests are Turtle and we have no Turtle parser. See ADR 0007 for the exit criterion. |

**`Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` are deliberately
absent.** `dotnet test` runs in MTP mode, selected in `global.json`:

```json
"test": { "runner": "Microsoft.Testing.Platform" }
```

MTP v2 on the .NET 10 SDK no longer supports running under VSTest, so this is
not only the smaller option but the supported one. Two test-time packages that a
.NET test project normally carries are therefore not here at all.

**`dotNetRdf.Core` deserves the scrutiny.** It ships no native asset, so
constraint 1 is satisfied. It brings twelve transitive managed dependencies —
`AngleSharp`, `HtmlAgilityPack`, `Newtonsoft.Json`, `VDS.Common` and eight
`System.*` compatibility facades — to read a Turtle file. That is a poor trade
on its merits, and it is accepted only because it is confined to a test project,
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
