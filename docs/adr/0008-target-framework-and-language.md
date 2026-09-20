# 0008 — Target framework and language version policy

## Status

Accepted. 2026-09-20.

## Context

`docs/brief.md`, constraint 5: current LTS .NET and current C#. Use `Span`,
`Memory`, pipelines, `ref struct`s and pooled buffers in parsers, the term
dictionary and index scans; allocation per quad is a defect.

That constraint is not about being current for its own sake. The APIs the
storage thesis depends on — `Span<T>` over pooled buffers, `ref struct`
enumerators that cannot escape to the heap, `System.IO.Pipelines` for streaming
parsers, and the generic-math and inline-array features the term dictionary will
want — arrive in the runtime and are sharpened every release. Targeting an older
framework means writing the allocation-free path by hand.

Constraint 3 pulls the other way, slightly: the browser host. A target framework
is only usable if `wasi-wasm` and Blazor WebAssembly support it.

## Decision

**Target framework: `net10.0`**, the current LTS, set once in
`Directory.Build.props`. The SDK is pinned in `global.json` to `10.0.401` with
`rollForward: latestFeature`, so a contributor on a later 10.0.4xx patch builds,
and one on .NET 11 does not silently get a different compiler.

**Language version: `latest`**, not a pinned number. With the SDK pinned,
`latest` is deterministic — it resolves to the C# version that ships with
10.0.4xx — and it means a language feature becomes available with the SDK bump
rather than needing a second edit that someone will forget.

**One exception, and it is structural.** `Varve.Analyzers` targets
`netstandard2.0`. That is not a preference: Roslyn analyzers load into the
compiler and into Visual Studio, which is a .NET Framework process, and
`netstandard2.0` is the only target that loads in both. It is required by
`EnforceExtendedAnalyzerRules`. The consequence is that analyzer code cannot use
`Span`-era APIs or modern BCL surface, and must not, even though the C# language
version is still `latest` there. Constraint 2 does not apply to it at all: it
runs inside the compiler, is never published, and its allocations are irrelevant
(ADR 0004).

**Repo-wide compiler settings**, all in `Directory.Build.props`:

- `Nullable` enable. Non-negotiable; a nullable-oblivious parser is a source of
  exactly the defects the conformance suite is bad at catching.
- `ImplicitUsings` **disable**. Usings are explicit. The cost is a few lines per
  file; the benefit is that a file's dependencies are visible in the file, which
  matters when the layering rule is the thing being enforced.
- `TreatWarningsAsErrors` true, `AnalysisLevel` `latest-recommended`,
  `EnforceCodeStyleInBuild` true.
- `GenerateDocumentationFile` true. It is what makes `IDE0005` work, and it is
  what a published package needs; enabling it now means the XML-comment
  obligation is not a surprise at milestone 3.
- `Deterministic` true; `ContinuousIntegrationBuild` true when `CI` is set;
  `EmbedUntrackedSources` true. Two builds of one commit produce the same bytes,
  which is a precondition for trusting anything the conformance run says.

### When we move

To the next LTS — `net12.0` — during its release window, once the WASM and AOT
smoke builds (`docs/roadmap.md`, milestone 3) pass on it. We do not target STS
releases: an STS bump would move every published package's target framework for
eighteen months of support, and consumers pay that cost.

Multi-targeting is not done and needs its own ADR if it is ever proposed. It
doubles the build matrix, doubles the conformance surface, and means the
`#if`-guarded paths are each tested half as much.

## Alternatives considered

- **`netstandard2.0` or `netstandard2.1` for the packages**, for maximum reach.
  Rejected outright by constraint 5, and it would defeat the thesis: no
  `Span`-based BCL surface, no pipelines, no generic math, no inline arrays. The
  allocation-free quad path would have to be hand-written and would still be
  slower.
- **The current STS release.** Newer features sooner. Rejected: published
  libraries on an STS target hand consumers an eighteen-month support window,
  and the brief says LTS.
- **Multi-targeting `net10.0` and `netstandard2.0`.** Would let downstream
  .NET Framework consumers in. Rejected as above; it is a decision that can be
  taken later if a real consumer needs it, and it should be taken deliberately
  with its cost stated.
- **Pinning `LangVersion` to a number, `14.0` or similar.** Marginally more
  explicit. Rejected: with the SDK pinned it is already deterministic, and a
  pinned number is a second thing to bump that silently blocks features until
  someone notices.
- **`ImplicitUsings` enable.** Less ceremony per file. Rejected: the implicit set
  is per-SDK and invisible in the source, and in a repository whose central rule
  is about what a compilation is allowed to reference, hiding references is the
  wrong default.

## Consequences

`global.json` pinning means a contributor without SDK 10.0.4xx gets an
immediate, clear failure rather than a subtly different build. `rollForward:
latestFeature` keeps that from being a patch-by-patch treadmill.

`TreatWarningsAsErrors` with `AnalysisLevel: latest-recommended` means an SDK
bump can fail the build on new analyzer rules. That is the intended behaviour —
the alternative is a growing set of warnings nobody reads — but it means an SDK
bump is its own commit, not a rider on something else.

The `netstandard2.0` island under `src/Varve.Analyzers/` will feel dated from
the inside. It is the price of the rule running in the compiler rather than in a
test, and there is no version of this where the analyzer targets `net10.0` and
still loads in Visual Studio.
