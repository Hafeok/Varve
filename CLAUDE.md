# CLAUDE.md — Varve

Varve is a graph database and RDF/SPARQL toolkit for .NET, event-sourced from
the first commit: the transaction log is the source of truth and every index is
a projection of it. Scope matches Oxigraph — RDF model, IRI, XSD datatypes, the
syntax family, SPARQL algebra, optimiser and evaluator, the store, server, CLI,
SHACL. Oxigraph is the reference for *scope and conformance behaviour*, never
for architecture.

**`docs/brief.md` is the authority.** It is not summarised accurately anywhere,
including here. Read it before proposing anything structural. Where this file
and the brief disagree, the brief wins and this file is wrong.

## Hard constraints

1. **100% managed.** No P/Invoke, no native assets. A package shipping one is out.
2. **Native AOT and trimming compatible.** No reflection in hot paths, no
   `Reflection.Emit`; source generators where code generation is needed.
3. **Three hosts from one core**: embedded library, server, browser (WASM), so
   storage is an abstraction from day one.
4. **Minimal dependencies.** Prefer the BCL. Every third-party package needs an
   ADR — build-time and test-time ones included.
5. **Current LTS .NET, current C#.** `Span`, `Memory`, pipelines, `ref struct`s,
   pooled buffers. **Allocation per quad is a defect.**
6. **Permissive licence, no copied code.** Read Oxigraph and dotNetRDF for
   behaviour; write our own.

## Design principles

- **Stable dependencies.** Fixed layers, references strictly downward.
- **Low coupling.** Small contracts over `Varve.Rdf` types. No shared mutable
  state, no static registries. Public API surface minimal and tracked.
- **High cohesion.** One package, one reason to change. **No `Common`, `Core`,
  `Utils` or `Abstractions` packages**, ever.

## Layers

| Layer | Packages |
|---:|---|
| 0 | `Varve.Iri`, `Varve.Xsd` |
| 1 | `Varve.Rdf` — model and the abstract quad source contract |
| 2 | syntax: `Varve.Turtle`, `Varve.RdfXml`, `Varve.JsonLd`, `Varve.Sparql.Results`, SPARQL algebra and parser |
| 3 | SPARQL optimiser and evaluator, `Varve.Shacl` |
| 4 | `Varve.Store` — log, projection contract, pre-commit validator contract |
| 5 | integration and hosts |

**Same-layer references are violations**, not exceptions. Contracts live in the
lowest layer that can define them without knowing their implementers.
`Varve.Store` is SPARQL-free (ADR 0005). Two open questions in ADR 0003 are
recorded and **not resolved**; do not resolve them in passing.

Each project declares `<VarveLayer>` — 0–5, or `none` for a test, benchmark or
analyzer assembly. `VARVE0001` and `VARVE0002` enforce it.

## How we work

Specification, then ADR, then code. Small vertical slices ending in passing
tests, never broad scaffolding that compiles and does nothing.

- **Check every proposal against the accepted ADRs** in `docs/adr/`. If it
  contradicts one, say so and propose a **superseding** ADR. Never diverge
  quietly, never edit an accepted ADR.
- **When one artefact changes, list what else must change** — specification,
  ADR, code, rule page, baseline.
- Cite specification sections. If unsure what a spec says, say so; do not guess.
- Flag anything that breaks AOT, trimming or WASM **the moment it appears**.
- Record a contradiction in the brief as an open question in the relevant ADR.
- Disagree with reasons. Agreement without a reason is not useful here.
- Prose: plain and precise, no marketing tone. Diagrams as Mermaid or SVG.
- Never take a package version from memory. Resolve it from NuGet, and put it in
  `Directory.Packages.props` with `Adr="NNNN"` naming the decision that admits
  it — never a `Version` attribute in a project file. `dotnet run
  eng/dependency-register.cs` is the gate (ADR 0009).

**A new rule ships as analyzer, then tests, then doc page, in that order of
importance.** A rule that exists only in a document is not a rule. Ids are
allocated in ADR 0004; `VARVE0003`–`VARVE0008` are reserved and unbuilt.

**Suppressions cite an ADR number.** A repo-wide `NoWarn` for a `VARVE` or
IL-prefixed rule is not an allowed form.

## `Varve.Store` behaviour

**`docs/spec/log-and-projection-model.md` is the authority**, and it takes
precedence over the brief where they differ. ADRs 0010–0020 record the decisions
it presupposes; 0012, 0018 and 0020 are **Proposed** and deliberately bring
options rather than answers.

Vocabulary that must not drift:

| Term | Means |
|---|---|
| **commit** | the logical unit: one position, one effective delta. Never a record. |
| **record** | the physical unit of append. A commit is one or more; the last closes it. |
| **readable head** | the position of the last *closed* commit. Unclosed records are invisible, not filtered. |
| **pinned read** | a short-lived engine snapshot, the lifetime of one operation. **Not time travel.** |
| **as-of read** | time travel: any closed position, nearest checkpoint plus a delta overlay. |
| **checkpoint** | a derived, immutable, *directly queryable* materialisation of one position. Droppable without loss. There is **no compaction** in the destructive sense. |
| **erasure mode** | per-dataset, off by default. On: personal terms are encrypted per data subject and erasure destroys the key. |
| **private term** | a term encrypted under a subject key. Random id, never interned, never plaintext in `log/`. |

The log records **what changed, not what was asked for**. The log is **never
rewritten**. Keys **never** live in the dataset directory.

## Correctness

The W3C suites are the acceptance gate. A feature is not done until its manifest
entries pass or each failure has a written, justified exemption. Performance
claims need BenchmarkDotNet numbers and a stated dataset.

## Commands

```bash
dotnet build Varve.slnx -c Release                    # zero warnings, or it is broken
dotnet test --project tests/Varve.Analyzers.Tests/Varve.Analyzers.Tests.csproj -c Release
dotnet test --project tests/Varve.Conformance.Tests/Varve.Conformance.Tests.csproj \
  -c Release --report-trx --results-directory artifacts/conformance
dotnet run eng/ratchet.cs -- artifacts/conformance    # the gate; prints per-suite counts
dotnet run eng/dependency-register.cs                 # every package names an ADR
git submodule update --init --recursive               # required for conformance
```

Add an ADR: next free number, five sections (Status, Context, Decision,
Alternatives considered, Consequences), a row in `docs/adr/README.md`. Add a
rule page: `docs/rules/VARVE000n.md`, linked from the descriptor's `HelpLinkUri`
and back to its ADR, plus rows in `docs/rules/README.md` and
`AnalyzerReleases.Unshipped.md`.

## State

Milestone 2. **No production types exist yet, by design** — decisions come
first. `src/` holds only `Varve.Analyzers`. Every W3C case fails with "no
parser registered", which is the correct result (ADR 0007). The public-API and
banned-symbol analyzers are configured but only exercised by a fixture until
`Varve.Iri` exists at milestone 3. `docs/roadmap.md` has the rest, including
eight open questions with an owner and a due milestone each.

## Never

- A native dependency, or any package that ships a native asset.
- `System.Uri` where an IRI is meant. Use `Varve.Iri` (ADR 0004).
- Reflection, `Reflection.Emit` or `dynamic` in shipped code.
- A `Common` / `Core` / `Utils` / `Helpers` / `Abstractions` package.
- An upward or same-layer package reference.
- Code copied from Oxigraph or dotNetRDF.
- A package version taken from memory.
- An IL-prefixed warning in `NoWarn`.
- Editing an accepted ADR instead of superseding it.
- A placeholder body that is not explicitly marked as a stub and reported.
