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
| 2 | syntax: `Varve.Turtle`, `Varve.RdfXml`, `Varve.JsonLd`, `Varve.Sparql.Results`, and `Varve.Sparql` (the algebra, parser and serialiser) |
| 3 | `Varve.Sparql.Evaluation` (optimiser and evaluator, one package — ADR 0048), `Varve.Shacl` |
| 4 | `Varve.Store` — log, projection contract, pre-commit validator contract |
| 5 | integrations: `Varve.Sparql.Store`, `Varve.Store.Browser` |
| 6 | hosts — every executable, and only executables: the server, the CLI, the smoke apps, the benchmarks (ADR 0060) |

**Same-layer references are violations.** Contracts live in the lowest layer that
can define them without knowing their implementers; `Varve.Store` is SPARQL-free
(ADR 0005). ADR 0003's open question 1 (`Varve.Shacl` and the evaluator) is
**not resolved** — do not resolve it in passing; question 2 was closed by ADR 0048. Each project declares `<ArchLayer>` (0–6; a test assembly declares none), and a layer-6 host also sets `<ArchCompositionRoot>` (ADR 0064).

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
  document is not a rule. A suppression of any other rule cites an ADR number;
  a `DD` or `VARVE` rule is never suppressed (below), and a repo-wide `NoWarn`
  for a `VARVE` or IL-prefixed rule is not allowed.

## Rules: `DD` and `VARVE`

`DecisionDriven.Analyzers` (`DD0001`–`DD0019`, development-time only, ADR 0062)
and `Varve.Analyzers` (`VARVE0003`–`VARVE0005`, ADR 0064) run at error
severity on every project in `src/`. Every ADR is also a decision set in
`docs/decisions/`, and code cites a ruling as a type:
`[Contract(typeof(<Set>.<Key>), Role = "...")]`,
`[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]`,
`[DesignDecision(typeof(<Set>.<Key>), Scope = ExceptionScope.<Scope>)]`.

- **A finding is fixed, or answered by a decision.** The `Decide:` line names
  both paths. Take the design change when the only reason for the code is that
  it already looked like this. Otherwise put `[DesignDecision]` on the
  narrowest symbol, citing a filed decision with the scope that matches the
  rule (`HotPath` for `VARVE0003`, `Pool` for `DD0004`, `Boundary` for the
  primitive rules).
- **Never `#pragma`, `[SuppressMessage]` or an `.editorconfig` downgrade** for
  a `DD` or `VARVE` id; `DD0008` reports each.
- **A new decision is filed unaccepted.** Its citations are `CS0618` until the
  maintainer adds `accepted-by`, which **a session never writes**; the pull
  request is red on that alone (ADR 0066). An accepted ADR changes by a dated
  amendment (ADR 0068), never by editing.
- **A finding about the analyzers themselves** (a false positive, a wrong
  message, a missing exemption) is an issue in the analyzer repository, with
  the reproduction, never a local workaround. That repository never names
  Varve. A finding about `VARVE0003`–`0005` is fixed in `src/Varve.Analyzers/`.
- **`[HotPath]` marks what runs per quad, per row or per term**, not the
  orchestration around it. Mark it, then fix what `VARVE0003` reports. The
  BCL allow-list (`varve_hot_path_allowed_types`) is configuration, by type or
  by member, and every entry is admitted by a decision in `HotPathScope`.

**Every milestone ends in a release** (ADR 0085): `dotnet run eng/changelog.cs
-- --release <version>`, commit, and the maintainer tags that commit. A change
that adds a line to `eng/changelog-sections.txt` starts the next milestone and
is red on **`release pending`** until the previous one is tagged.

**`main` is the trunk, and accepts only checked commits** (ADR 0088): ruleset 1
requires every gate by job name, `dco` and `agent review` included, with no
bypass. A session lands through a pull request from its branch, which needs an
approval on its head from the session's responsible human or a delegate (ADR
0087): a conversation comment `approve <head sha>`, at least 12 characters. That
form is **not a valid workflow**, only tolerated until the ledger's review gate
replaces it. `agent review` is judged by main's script on trusted triggers and
posted by the gates App; a required check is spoofable by name unless pinned to
an App. A human pushes to a `land/` branch and fast-forwards `main`
to its checked head, or merges a review-free pull request. Work not ready for
the trunk lives behind a feature flag or stays local, **not on a long-lived
branch**. Human review gates a *release*, via the `release` environment.

## Every commit

Conventional commits, one logical change each, and three things in each:

- **`Refs #N` or `Closes #N`** in the body — enforced by `eng/issue-refs.cs`
  (ADR 0033). No issue covers the work? Open one first.
- **`Signed-off-by:`** — DCO, from every session without exception; it names the
  human who may contribute the code: for a session's commit, its responsible
  human in `eng/identities.json` or a delegate. Enforced by `eng/dco.cs`
  (ADR 0087).
- **A signature**, for human committers. Cloud AI sessions are exempt by ruleset
  bypass; ADR 0034 records why, as a stated deviation.

**Every AI-assisted session files a record** in `docs/traceability/`, named
`YYYY-MM-DD-issue-N-<slug>.md`, with the prompt, **the tool and model named
exactly**, and the report. The model is the identifier the session's metadata
reports. A tool that will not write it says so in the record, and the maintainer
fills it in: a blank model is an incomplete record. **A commit message never
carries a model's identifier** (the string an API takes to select a model); a
tool's co-author trailer naming the product is not one (ADR 0033, amended
2026-10-02). Prose says "developed with AI assistance under human review" and
**names no product**.

## `Varve.Store` behaviour

**`docs/spec/log-and-projection-model.md` is the authority** (version 1.5) and
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

Every gate is also a job in `eng/ci.cs`. `eng/` is the home of the Varve-specific
gates and hosts the generic ones until they are ported (ADR 0086): process gates
to [how-we-work#1](https://github.com/mindovermachine-dev/how-we-work/issues/1),
ledger gates to
[decision-driven-analyzers#84](https://github.com/Hafeok/decision-driven-analyzers/issues/84)
(provenance in #65). `CONTRIBUTING.md` has the shape of an ADR, a rule, a suite
and a dependency; `GOVERNANCE.md` has who decides.

## State

Milestone 6c. `src/` holds `Varve.Analyzers`, `Varve.Iri` and `Varve.Xsd`
(0), `Varve.Rdf` (1), `Varve.Turtle`, `Varve.Sparql` and
`Varve.Sparql.Results` (2), `Varve.Sparql.Evaluation` (3), `Varve.Store` (4)
and **`Varve.Sparql.Store`** and **`Varve.Store.Browser`** (5). The evaluator answers SPARQL 1.1 queries,
with the 1.2 additions, over any `IQuadSource`; the results package reads and
**writes** XML, JSON, CSV and TSV; **one SPARQL Update request is one commit**
— or none when its net effect is empty — evaluated operation by operation over
the pinned head and the overlay of the operations before it (ADR 0057), with
dataset-level validators and a staging view in the store (ADR 0058); and
`Varve.Rdf` canonicalises datasets with **RDFC-1.0**, SHA-256 or SHA-384, under
a work limit (ADR 0059), which the harness now uses to compare datasets. All
547 query evaluation cases that are not blocked pass over both subjects (5b's
544 and the three CSV cases 5b never wired), all
94 update cases, all 86 `rdf-canon` cases and ten writer checks; the ratchet
holds **2,803** lines, exemptions empty; canonical N-Triples and N-Quads are
RDF 1.2's (ADR 0061), gated by its 82 `c14n` cases. AOT and the browser both execute an
update request through `Varve.Sparql.Store`, write results JSON and
canonicalise; they are layer 6 hosts (ADR 0060). **The store is durable**
(milestone 6a, ADRs 0070–0077): `FileStorage` keeps `log/` in **format version
1** (`docs/spec/storage-format.md`, read for ever from the first prerelease
that writes it) and the default projection's runs in `derived/`, read through a
synchronous blob read; the failure-injection suite in
`tests/Varve.Store.Tests/Faults/` crashes it at every operation and byte, loses
power with writes reordered, and copies it mid-write, and is a gate. Since
milestone 6c (ADRs 0078–0084) `derived/` is **derived format 2**: runs carry
the term dictionary (opening loads none) and their keys compressed in blocks;
checkpoints and merges are streamed, written by an optional
`CheckpointPolicy`; **bulk loads** (`Dataset.BeginBulkLoadAsync`) sort
outside memory and commit once, crash-tested at every operation;
`Dataset.ShipAsync` bootstraps a replica by copying files; and
`Varve.Store.Browser` stores a dataset in OPFS through synchronous access
handles in a worker, or in IndexedDB, tested in headless Chromium with
`log/` byte-identical to the desktop's. **RDF 1.2
Turtle and TriG are not accepted at all** — `turtle.md` §9. Nothing is
published; the first tag is `v0.1.0-preview.1` (ADR 0029). Not built: HTTP for
`LOAD` and `SERVICE`, archive, erasure mode, SHACL, the server.
`docs/roadmap.md` has the rest, an owner and a due milestone per open
question.

`tools/repo-standard/` is **repo-standard** (ADR 0039): a CLI and a GitHub
Action that apply a declared set of GitHub repository settings. Built here,
moving to its own repository: its own solution, nothing in `src/` references
it and it references nothing there, and it is held to every rule above.
`.github/workflows/repo-standard.yml` is inert until `.github/repo-standard.yaml`
exists.

## Never

- A native dependency, or any package that ships a native asset.
- `System.Uri` where an IRI is meant. Use `Varve.Iri` (ADR 0004).
- A commit with no issue reference or no DCO sign-off, or one whose message
  carries a model's identifier; a long-lived branch; a trunk left
  un-releasable.
- Reflection, `Reflection.Emit` or `dynamic` in shipped code.
- A `Common` / `Core` / `Utils` / `Abstractions` package, or an upward or
  same-layer reference. A `.cs` file without the MPL-2.0 notice (ADR 0031).
- Code copied from Oxigraph or dotNetRDF, a version taken from memory, an
  IL-prefixed warning in `NoWarn`, or editing an accepted ADR.
- A suppression of a `DD` or `VARVE` rule, an `accepted-by` written by a
  session, or a workaround for an analyzer's false positive in place of an
  upstream issue.
- **API keys, or any credential scheme other than OIDC bearer tokens** (ADR 0037).
- A placeholder body not explicitly marked as a stub and reported.
