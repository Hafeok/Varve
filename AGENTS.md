# AGENTS.md — Varve

A graph database and RDF/SPARQL toolkit for .NET, event-sourced from the first
commit: the log is the source of truth and every index is a projection of it.
Scope matches Oxigraph, which is the reference for *scope and conformance
behaviour* and never for architecture.

**`docs/brief.md` is the authority**, not summarised accurately anywhere
including here — read it before proposing anything structural. Where this file
disagrees, the brief wins. One departure is recorded: the licence, ADR 0031.

## Hard constraints

1. **100% managed.** No P/Invoke, no native asset in a shipped artifact.
2. **Native AOT and trimming compatible.** No reflection in hot paths, no
   `Reflection.Emit`; source generators where code generation is needed.
3. **Three hosts from one core**: embedded, server, browser.
4. **Minimal dependencies.** Prefer the BCL; every package needs an ADR.
5. **Current LTS .NET, current C#.** Spans, pipelines, pooled buffers.
   **Allocation per quad is a defect.**
6. **No copied code.** Read Oxigraph and dotNetRDF for behaviour, write our own.
   (Constraint 6's "permissive licence" half is superseded by ADR 0031.)

**Stable dependencies**, references strictly downward. **Low coupling**: no
shared mutable state, no static registries. **High cohesion**: one reason to change.

## Layers

| Layer | Packages |
|---:|---|
| 0 | `Varve.Iri`, `Varve.Xsd` |
| 1 | `Varve.Rdf` — model and the abstract quad source contract |
| 2 | syntax: `Varve.Turtle`, `Varve.RdfXml`, `Varve.JsonLd`, `Varve.Sparql.Results`, SPARQL algebra and parser |
| 3 | SPARQL optimiser and evaluator, `Varve.Shacl` |
| 4 | `Varve.Store` — log, projection contract, pre-commit validator contract |
| 5 | integration and hosts |

**Same-layer references are violations.** Contracts live in the lowest layer that
can define them without knowing their implementers; `Varve.Store` is SPARQL-free
(ADR 0005). ADR 0003's two open questions are **not resolved** — do not resolve
them in passing. Each project declares `<VarveLayer>` (0–5, or `none`).

## How we work

Specification, then ADR, then code. Small vertical slices ending in passing
tests, never scaffolding that compiles and does nothing.

- **Check every proposal against the accepted ADRs.** If it contradicts one, say
  so and propose a **superseding** ADR; never diverge quietly and never edit an
  accepted one. When one artefact changes, list what else must — specification,
  ADR, code, rule page, baseline.
- Cite specification sections; say so rather than guess. Flag anything breaking
  AOT, trimming or WASM **the moment it appears**. Disagree with reasons.
- Never take a version from memory: resolve it from NuGet into
  `Directory.Packages.props` with `Adr="NNNN"` (ADR 0009).
- **A new rule ships as analyzer, then tests, then doc page.** A rule only in a
  document is not a rule. **Suppressions cite an ADR number**; a repo-wide
  `NoWarn` for a `VARVE` or IL-prefixed rule is not allowed.

**`main` is the trunk** (ADR 0032). Commit directly or open a pull request, your
choice; work not ready for the trunk lives behind a feature flag or stays local,
**not on a long-lived branch**. The blocking review is the automated one — every
gate, no bypass. Human review gates a *release*, via the `release` environment.

## Every commit

Conventional commits, one logical change each, and three things in each:

- **`Refs #N` or `Closes #N`** in the body — enforced by `eng/issue-refs.cs`
  (ADR 0033). No issue covers the work? Open one first.
- **`Signed-off-by:`** — DCO, from every session without exception; it names the
  human who may contribute the code.
- **A signature**, for human committers. Cloud AI sessions are exempt by ruleset
  bypass; ADR 0034 records why, as a stated deviation.

**Every AI-assisted session files a record** in `docs/traceability/`, named
`YYYY-MM-DD-issue-N-<slug>.md`, with the prompt, **the tool and model named
exactly**, and the report. Prose says "developed with AI assistance under human
review" and **names no product**.

## `Varve.Store` behaviour

**`docs/spec/log-and-projection-model.md` is the authority** (version 1.3) and
beats the brief where they differ. ADRs 0010–0023 record what it presupposes and
**none is `Proposed`**. The cipher is [0028](docs/adr/0028-deterministic-aead-from-hmac.md),
superseding 0020, conditional on external review. Vocabulary that must not drift:

| Term | Means |
|---|---|
| **commit** | the logical unit: one position, one effective delta. Never a record. |
| **record** | the physical unit of append. A commit is one or more; the last closes it. |
| **readable head** | the position of the last *closed* commit. Unclosed records are invisible, not filtered. |
| **pinned read** | a short-lived engine snapshot, the lifetime of one operation. **Not time travel.** |
| **as-of read** | time travel: any closed position, nearest checkpoint plus a delta overlay. |
| **checkpoint** | a derived, immutable, *directly queryable* materialisation of one position. Droppable without loss; **no compaction** destructively. |
| **erasure mode** | per-dataset, off by default. On: personal terms encrypted per data subject, erasure destroys the key (ADR 0028). |
| **private term** | encrypted under a subject key. Counter id in its own class, never interned, never plaintext in `log/`. |

The log records **what changed, not what was asked for**, is **never rewritten**,
and keys **never** live in the dataset directory.

## Correctness

The W3C suites are the acceptance gate: a feature is not done until its manifest
entries pass or each failure has a justified exemption in
`baseline/exemptions.txt`. **Every streaming reader runs the chunk-boundary
oracle** — whole versus split at every offset, expected value computed
(`docs/testing.md` §2). **Detroit-style**: real components through public
contracts, doubles only at a true process boundary. Test data is
version-controlled and **never fetched at test time**.

## Commands

```bash
dotnet run eng/ci.cs                               # the whole pipeline, as CI runs it
dotnet run eng/ci.cs -- --list                     # and what it consists of, to run one
dotnet build Varve.slnx -c Release                 # zero warnings, or it is broken
dotnet run eng/dependency-register.cs -- --base origin/main
git submodule update --init --recursive            # needed for conformance
```

Every gate is also a job in `eng/ci.cs`. `CONTRIBUTING.md` has the shape of an
ADR, a rule, a suite and a dependency; `GOVERNANCE.md` has who decides.

## State

Milestone 4. `src/` holds `Varve.Analyzers`, `Varve.Iri` (0), `Varve.Rdf` (1),
`Varve.Turtle` (2) and **`Varve.Store` (4)** — the in-memory log, the default
quad projection as sorted runs, pinned and as-of reads, checkpoints, `Diff`,
settings, subscriptions and the failed state, over `MemoryStorage`
(durability `None`). §10's properties that do not concern erasure or the file
backend pass against a reference model (ADR 0043); a scan allocates zero bytes
per quad. **All 883** syntax cases pass, exemptions empty. AOT and the browser
both run the store. Specification **1.3**. **RDF 1.2 Turtle and TriG are not
accepted at all** — `turtle.md` §9. Nothing is published; the first tag is
`v0.1.0-preview.1` (ADR 0029). Not built: `Varve.Xsd`, canonicalisation, the
file and browser backends, erasure mode, SPARQL, SHACL. `docs/roadmap.md` has
the rest, an owner and a due milestone per open question.

## Never

- A native dependency, or any package that ships a native asset.
- `System.Uri` where an IRI is meant. Use `Varve.Iri` (ADR 0004).
- A commit with no issue reference or no DCO sign-off; a long-lived branch; a
  trunk left un-releasable.
- Reflection, `Reflection.Emit` or `dynamic` in shipped code.
- A `Common` / `Core` / `Utils` / `Abstractions` package, or an upward or
  same-layer reference. A `.cs` file without the MPL-2.0 notice (ADR 0031).
- Code copied from Oxigraph or dotNetRDF, a version taken from memory, an
  IL-prefixed warning in `NoWarn`, or editing an accepted ADR.
- **API keys, or any credential scheme other than OIDC bearer tokens** (ADR 0037).
- A placeholder body not explicitly marked as a stub and reported.
