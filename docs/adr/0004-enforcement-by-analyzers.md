# 0004 — Enforcement by analyzers

## Status

Accepted. 2026-09-20.

## Context

`docs/brief.md`: every architecture rule and code rule is implemented as a
Roslyn analyzer and fails the build. A rule that exists only in a document is
not a rule. When a new rule is agreed, the deliverable is the analyzer, its
tests, and the doc entry, in that order of importance.

This ADR fixes the mechanics that every rule then inherits: where rules come
from, how ids are allocated, what severity they carry, and what it takes to
suppress one.

## Decision

### Off-the-shelf first

We write an analyzer only for what is specific to Varve. Everything already
solved is taken from Microsoft:

- The **trimming, AOT and single-file analyzers**, enabled through
  `IsAotCompatible` on packable projects, at **error** severity. The
  IL-prefixed diagnostics are listed explicitly in `.editorconfig` at `error`
  and **never enter `NoWarn`**. Constraint 2 is not a preference; a warning that
  can be ignored is not an enforcement of it.
- **`Microsoft.CodeAnalysis.PublicApiAnalyzers`** with `PublicAPI.Shipped.txt`
  and `PublicAPI.Unshipped.txt` per packable project, so that every new public
  member is a reviewable line in a diff rather than an accident.
- **`Microsoft.CodeAnalysis.BannedApiAnalyzers`** with `eng/BannedSymbols.txt`,
  each entry carrying a message that cites this ADR.

### Id scheme

`VARVE` followed by four digits, allocated in order from `VARVE0001`. An id is
never reused, and a retired rule's id stays retired — a suppression in someone's
code that names a retired id should read as stale, not as suppressing something
else.

Each rule has a page at `docs/rules/VARVE000n.md`. The analyzer's
`HelpLinkUri` points at that page, and the page links back to the ADR that
motivates the rule. The chain from a build error to the reasoning behind it is
two clicks and is not allowed to break.

Rules are tracked in `AnalyzerReleases.Shipped.md` and
`AnalyzerReleases.Unshipped.md`; `RS2008` enforces that a new rule appears in
one of them.

### Id reservation table

| Id | Rule | Motivated by | State |
|---|---|---|---|
| `VARVE0001` | Layer direction: a reference must be to a strictly lower layer | ADR 0003 | **Implemented** |
| `VARVE0002` | Layer declaration: missing, invalid, or worked around — including `none` on a packable assembly and the analyzer referenced as a library | ADR 0003 | **Implemented** |
| `VARVE0003` | `InternalsVisibleTo` only toward `*.Tests` assemblies | ADR 0003 | Reserved |
| `VARVE0004` | No project or namespace named `Common`, `Core`, `Utils`, `Helpers` or `Abstractions` | brief, principle 3 | Reserved |
| `VARVE0005` | No mutable static state and no static registries outside an explicit allow-list | brief, principle 2 | Reserved |
| `VARVE0006` | Hot path discipline: a `[HotPath]` member may not box, capture, allocate arrays or strings, use LINQ, or call members not marked hot-path-safe | brief, constraint 5 | Reserved |
| `VARVE0007` | Public contracts between packages use `Varve.Rdf` types or BCL primitives only | brief, principle 2 | Reserved |
| `VARVE0008` | A suppression's justification must cite an ADR number | this ADR | Reserved |

Reserved means the id is allocated and the rule is agreed in principle. It is
not a rule until the analyzer exists. Nothing in this repository relies on a
reserved rule.

> **Amended 2026-09-25** (milestone 5b, ADRs 0048 and 0055). `VARVE0007`'s
> reserved wording is widened to read: *public contracts between packages use
> `Varve.Rdf` types, BCL primitives, or the SPARQL algebra's node types from
> `Varve.Sparql`*. The narrower wording predates the algebra. ADR 0048 made
> the algebra the evaluator's input — its entry point takes a `Query` — and
> ADR 0055's `IServiceHandler` receives a `Service` node, so both would be
> violations of the rule as first worded and of nothing the rule was for:
> the algebra is at layer 2, below every consumer, immutable, and exists to be
> the contract between the parser and whatever evaluates or rewrites. The
> principle the rule protects — a contract names nothing a consumer would have
> to take a dependency on beside the one it already has — is unchanged. The
> rule is still reserved; when its analyzer is written, it admits exactly
> these three sources.

### Severity

Architectural rules — anything that would let the package graph rot — are
**error**, not warning. `VARVE0001` and `VARVE0002` are errors.

The repository builds with `TreatWarningsAsErrors`, so the practical difference
is narrow. It is not zero: a warning can be silenced repo-wide by a single
`NoWarn` edit, and an error cannot. Rules that protect a constraint from the
brief are declared at error so that relaxing one is a visible act.

### Suppression policy

A suppression carries a `Justification` that cites an ADR number:

```csharp
[SuppressMessage("Varve", "VARVE0001:Layer direction",
    Justification = "ADR 0003: recorded exception, see open question 1.")]
```

Three forms are allowed, in decreasing order of preference: the attribute at the
narrowest possible scope; a `GlobalSuppressions.cs` entry naming a specific
symbol; and, for a whole project, an `.editorconfig` severity override in that
project's directory with a comment citing the ADR. A repo-wide `NoWarn` for a
`VARVE` rule or an IL-prefixed rule is not an allowed form.

`VARVE0008` will enforce the citation. Until it exists this is a review
obligation, and `CONTRIBUTING.md` states it.

## Alternatives considered

- **Documented conventions, enforced in review.** The brief rejects this
  outright, and rightly: review catches a layer violation only when the reviewer
  holds the whole package graph in their head.
- **An architecture-test library** — NetArchTest, ArchUnitNET — run as unit
  tests. Genuinely capable, and it sees the whole assembly graph, which an
  analyzer does not. Rejected as the primary mechanism for two reasons. It is a
  runtime dependency of the test projects, so constraint 4 applies to it for no
  gain over what we can write. And it reports at test time rather than at the
  keystroke; the brief's model is that the compiler is where a rule binds. Worth
  revisiting for the whole-graph checks an analyzer genuinely cannot do.
- **Gating on the published package dependency graph in CI.** A good second net
  and the only thing that sees what a consumer actually resolves. Complementary,
  not a substitute — see ADR 0003's alternatives. Not built at milestone 1.
- **Warning severity with `TreatWarningsAsErrors` doing the work.** Rejected: it
  makes a repo-wide `NoWarn` sufficient to disable an architectural rule, and
  that edit is one line in a shared file.
- **One analyzer assembly per rule family.** Rejected as premature. Eight rules
  do not justify the packaging. Revisit if `Varve.Analyzers` grows to the point
  where an unrelated rule change forces every project to rebuild.

## Consequences

`Varve.Analyzers` is referenced by every Varve project as an analyzer
(`OutputItemType="Analyzer"`, `ReferenceOutputAssembly="false"`), so it is never
a runtime dependency and never appears in a published package's dependency list.
Its own use of reflection and allocation is irrelevant to constraint 2, because
it runs inside the compiler and is never published.

Adding a rule means touching four things — the analyzer, its tests, the release
tracking file, and the doc page. That is deliberate friction proportional to
adding a rule that will fail other people's builds.

**~~Known limit: the `Varve.Analyzers` exemption is by name.~~** *Amended
2026-09-21: withdrawn, because it was wrong. The original text is kept below.*

> ~~`VARVE0002` exempts an assembly literally named `Varve.Analyzers` from
> carrying layer metadata, because it is a build-time component with no
> meaningful layer. A future assembly could take that name and skip the layer
> check. An analyzer cannot see whether an assembly is packable or whether it
> was referenced as an analyzer, so there is no better discriminator available
> at the point where the rule runs. Recorded as a limit rather than papered
> over. If it ever matters, the fix is the CI-side package-graph check, which
> can see what an analyzer cannot.~~

The claim that no better discriminator exists was false on both counts, and the
loophole is closed rather than documented:

- **An analyzer can see whether an assembly is packable.** `IsPackable` is an
  MSBuild property like any other; it only needed
  `<CompilerVisibleProperty Include="IsPackable" />`. `VARVE0002` now refuses
  `none` from any packable assembly, whatever it is named. Being packed is the
  question layering actually turns on, and a name was only ever a proxy for it.
- **An analyzer can tell an analyzer reference from a library reference.** An
  analyzer reaches the compiler as `/analyzer:` and never as `/reference:`, so
  it cannot appear in `Compilation.SourceModule.ReferencedAssemblySymbols` at
  all under this repository's wiring. `VARVE0002` now reports `Varve.Analyzers`
  appearing there, from every assembly except `Varve.Analyzers.Tests`, which
  instantiates the rule types and is the one compilation with a reason to.

The name check survives beside the packable check because the two catch
different things — a library that is not yet packable is still held to its name
— and it is the pair that leaves no way through. The CI-side package-graph
check remains worth having for what an analyzer genuinely cannot see (ADR 0003),
but it is no longer the answer to this.

The withdrawn text is kept rather than deleted because the reasoning that
produced a wrong conclusion is the part worth being able to find again. See the
matching amendment in `docs/rules/VARVE0002.md`.

**Consequence to discharge: the `System.Uri` ban is currently wider than the
brief supports.** The brief scopes the ban to "`System.Uri` in `Varve.Iri`
consumers". `eng/BannedSymbols.txt` bans it for all packable projects, which is
correct today — there are none — and will be wrong at layer 5, where
`Varve.Server` speaks HTTP and legitimately needs `System.Uri` for transport
addresses that are not RDF IRIs. The narrowing is due before layer 5 exists, and
`docs/roadmap.md` records it against milestone 3. The narrowing itself will be a
per-project `BannedSymbols.txt` at layer 5, not a suppression at each call site.

**Milestone 1 delivers these mechanisms partly inert.** PublicApiAnalyzers and
BannedApiAnalyzers apply to packable projects, and milestone 1 has none by
design. They are configured and unexercised until `Varve.Iri` exists at
milestone 3. Configured is not the same as proven, and this ADR says so rather
than letting the configuration read as coverage.
