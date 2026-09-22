# CLAUDE.md — Varve

Varve is a graph database and RDF/SPARQL toolkit for .NET, event-sourced from
the first commit: the transaction log is the source of truth and every index is
a projection of it. Scope matches Oxigraph — RDF model, IRI, XSD datatypes, the
syntax family, SPARQL algebra, optimiser and evaluator, store, server, CLI,
SHACL — which is the reference for *scope and conformance behaviour*, never for
architecture.

**`docs/brief.md` is the authority.** It is not summarised accurately anywhere,
including here — read it before proposing anything structural. Where this file
and the brief disagree, the brief wins and this file is wrong.

## Hard constraints

1. **100% managed.** No P/Invoke, no native assets in a shipped artifact.
2. **Native AOT and trimming compatible.** No reflection in hot paths, no
   `Reflection.Emit`; source generators where code generation is needed.
3. **Three hosts from one core**: embedded library, server, browser (WASM), so
   storage is an abstraction from day one.
4. **Minimal dependencies.** Prefer the BCL; every third-party package needs an
   ADR, build-time and test-time ones included.
5. **Current LTS .NET, current C#.** `Span`, `Memory`, pipelines, `ref struct`s,
   pooled buffers. **Allocation per quad is a defect.**
6. **Permissive licence, no copied code.** Read Oxigraph and dotNetRDF for
   behaviour; write our own.

## Design principles

- **Stable dependencies.** Fixed layers, references strictly downward.
- **Low coupling.** Small contracts over `Varve.Rdf` types; no shared mutable
  state, no static registries; public API surface minimal and tracked.
- **High cohesion.** One package, one reason to change. **Never a `Common`,
  `Core`, `Utils` or `Abstractions` package.**

## Layers

| Layer | Packages |
|---:|---|
| 0 | `Varve.Iri`, `Varve.Xsd` |
| 1 | `Varve.Rdf` — model and the abstract quad source contract |
| 2 | syntax: `Varve.Turtle`, `Varve.RdfXml`, `Varve.JsonLd`, `Varve.Sparql.Results`, SPARQL algebra and parser |
| 3 | SPARQL optimiser and evaluator, `Varve.Shacl` |
| 4 | `Varve.Store` — log, projection contract, pre-commit validator contract |
| 5 | integration and hosts |

**Same-layer references are violations.** Contracts live in the lowest layer
that can define them without knowing their implementers, and `Varve.Store` is
SPARQL-free (ADR 0005). ADR 0003's two open questions are recorded and **not
resolved**; do not resolve them in passing. Each project declares
`<VarveLayer>` — 0–5, or `none` for a test, benchmark or analyzer assembly —
enforced by `VARVE0001`/`VARVE0002`.

## How we work

Specification, then ADR, then code. Small vertical slices ending in passing
tests, never broad scaffolding that compiles and does nothing.

- **Check every proposal against the accepted ADRs** in `docs/adr/`. If it
  contradicts one, say so and propose a **superseding** ADR; never diverge
  quietly or edit an accepted one. When one artefact changes, list what else
  must — specification, ADR, code, rule page, baseline.
- Cite specification sections; say so rather than guess. Flag anything that
  breaks AOT, trimming or WASM **the moment it appears**; record a contradiction
  in the brief as an open question in the relevant ADR.
- Disagree with reasons. Prose: plain and precise, no marketing tone.
- Never take a package version from memory. Resolve it from NuGet, and put it in
  `Directory.Packages.props` with `Adr="NNNN"` naming the decision that admits
  it — never a `Version` attribute in a project file. `dotnet run
  eng/dependency-register.cs` is the gate (ADR 0009).

**Nothing reaches `main` except through a pull request.** Branches are
`milestone/<id>` or `fix/<topic>`; one PR per milestone part; the PR body is the
report, not a changelog; a PR depending on an unmerged one branches from it,
opens as a draft and rebases when the parent merges. Only the owner merges.

**A new rule ships as analyzer, then tests, then doc page, in that order.** A
rule that exists only in a document is not a rule; ids are allocated in ADR
0004. **Suppressions cite an ADR number**, and a repo-wide `NoWarn` for a
`VARVE` or IL-prefixed rule is not an allowed form.

## `Varve.Store` behaviour

**`docs/spec/log-and-projection-model.md` is the authority** (version 1) and
takes precedence over the brief where they differ. ADRs 0010–0023 record the
decisions it presupposes, and **none is `Proposed`**. The cipher is
[0028](docs/adr/0028-deterministic-aead-from-hmac.md), which supersedes 0020 and
is conditional on an external review.

Vocabulary that must not drift:

| Term | Means |
|---|---|
| **commit** | the logical unit: one position, one effective delta. Never a record. |
| **record** | the physical unit of append. A commit is one or more; the last closes it. |
| **readable head** | the position of the last *closed* commit. Unclosed records are invisible, not filtered. |
| **pinned read** | a short-lived engine snapshot, the lifetime of one operation. **Not time travel.** |
| **as-of read** | time travel: any closed position, nearest checkpoint plus a delta overlay. |
| **checkpoint** | a derived, immutable, *directly queryable* materialisation of one position. Droppable without loss; **no compaction** in the destructive sense. |
| **erasure mode** | per-dataset, off by default. On: personal terms are encrypted per data subject and erasure destroys the key (ADR 0028). |
| **private term** | encrypted under a subject key. Counter id in its own class, never interned, never plaintext in `log/`. |

The log records **what changed, not what was asked for**, is **never rewritten**,
and keys **never** live in the dataset directory.

## Correctness

The W3C suites are the acceptance gate. A feature is not done until its manifest
entries pass or each failure has a justified exemption in
`baseline/exemptions.txt` — the ratchet fails one with no reason beside it, and
a feature shipped with no suite behind it is the defect A3 of the 3a close-out
found. **Every streaming reader also runs the chunk-boundary oracle over its
manifests** — whole versus split at every offset, expected value computed
(`docs/testing.md` §2). Performance claims need BenchmarkDotNet numbers, a
stated dataset *and* machine (`tests/Varve.Benchmarks/README.md` is the shape).

## Commands

```bash
dotnet build Varve.slnx -c Release                    # zero warnings, or it is broken
for p in Analyzers Iri Rdf Turtle; do dotnet test --project tests/Varve.$p.Tests/*.csproj -c Release; done
dotnet test --project tests/Varve.Conformance.Tests/*.csproj -c Release --report-trx --results-directory artifacts/conformance
dotnet run eng/ratchet.cs -- artifacts/conformance    # the gate; prints per-suite counts
dotnet run eng/dependency-register.cs                 # every package names an ADR
dotnet run eng/native-assets.cs                       # no native asset in a shipped closure
dotnet pack Varve.slnx -c Release -o artifacts/packages && dotnet run eng/package-metadata.cs -- artifacts/packages
dotnet publish tests/Varve.AotSmoke -c Release -o artifacts/aot && artifacts/aot/Varve.AotSmoke
git submodule update --init --recursive               # needed for conformance
```

Adding an ADR, a rule or a dependency: `CONTRIBUTING.md` has the shape of each.

## State

Milestone 3b. `src/` holds `Varve.Analyzers`, `Varve.Iri` (0), `Varve.Rdf` (1)
and `Varve.Turtle` (2) — N-Triples, N-Quads, **Turtle and TriG**, reader and
writer. **All 883** cases pass, exemptions empty: Turtle 313, TriG 357,
N-Triples 70, N-Quads 87, RDF 1.2 syntax 29 + 27. Parsing allocates **zero
bytes per quad** on every entry point in every syntax; AOT and the browser both
read and write Turtle. The conformance harness reads its own manifests with
`Varve.Turtle` (ADR 0007's exit criterion, met early); dotNetRDF remains only
in the benchmark project. **RDF 1.2 Turtle and TriG are not accepted at all** —
`turtle.md` §9 lists the constructs and the roadmap the cost. Nothing is
published: the metadata and workflow await the first `v0.1.0-preview.1` tag
(ADR 0029). Not built: `Varve.Xsd`, canonicalisation, the store, SPARQL, SHACL.
`VARVE0003`–`VARVE0008` reserved. `docs/roadmap.md` has the rest, with an owner
and a due milestone per open question.

## Never

- A native dependency, or any package that ships a native asset.
- `System.Uri` where an IRI is meant. Use `Varve.Iri` (ADR 0004).
- A commit on `main`, or a push to it. Every change arrives by pull request.
- Reflection, `Reflection.Emit` or `dynamic` in shipped code.
- A `Common` / `Core` / `Utils` / `Helpers` / `Abstractions` package, or an
  upward or same-layer package reference.
- Code copied from Oxigraph or dotNetRDF, or a package version taken from memory.
- An IL-prefixed warning in `NoWarn`, or editing an accepted ADR instead of
  superseding it.
- A placeholder body that is not explicitly marked as a stub and reported.