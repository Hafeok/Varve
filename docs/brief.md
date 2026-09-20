# Project prompt: Varve, a .NET-native, event-sourced RDF store

## What this project is

We are building a graph database and RDF/SPARQL toolkit with the same scope as Oxigraph, written entirely in managed .NET. The difference from a port is the storage thesis: the store is event-sourced from the first commit. The transaction log is the source of truth, and every index is a projection of it.

Oxigraph is the reference for scope and for conformance behaviour. It is not the reference for architecture. Do not translate Rust to C#; design for the CLR and for the log-first model.

## Who you are working with

Emil: founder and architect, long experience with RDF, SHACL, SPARQL, OWL and Oxigraph, and with event sourcing, DDD, Marten and the .NET/Azure stack. Skip introductions to any of these. Assume spec-level familiarity and argue at that level. When you disagree with a design choice, say so and give the reason; agreement without a reason is not useful here.

## Hard constraints

1. 100% managed code. No P/Invoke, no native binaries, no RocksDB, no LMDB, no SQLite. If a dependency ships a native asset, it is out.
2. Native AOT and trimming compatible. No runtime reflection in hot paths, no Reflection.Emit, source generators where code generation is needed.
3. Runs in three hosts from the same core: embedded library, standalone server, and browser (Blazor WASM / wasi). The storage backend is therefore an abstraction from day one, with in-memory, file, and browser implementations.
4. Minimal dependencies. Prefer the BCL. Every third-party package needs an ADR.
5. Current LTS .NET and current C#. Use Span, Memory, pipelines, ref structs, and pooled buffers in parsers, the term dictionary, and index scans. Allocation per quad is a defect.
6. Open source, permissive licence. No code copied from Oxigraph or dotNetRDF; read them for behaviour, write our own.

## Design principles

Three principles govern package and type boundaries. They are checked, not aspired to; a proposal that violates one needs an ADR stating why.

1. Stable dependencies. Dependencies point toward the more stable package. The package graph is a DAG with fixed layers, and a package may only reference packages in a lower layer:
   - Layer 0: `Varve.Iri`, `Varve.Xsd`. No Varve dependencies.
   - Layer 1: `Varve.Rdf`. The model and the abstract quad source contract.
   - Layer 2: syntax packages (`Varve.Turtle`, `Varve.RdfXml`, `Varve.JsonLd`, `Varve.Sparql.Results`) and the SPARQL algebra and parser.
   - Layer 3: SPARQL optimiser and evaluator, `Varve.Shacl`. Both run against the quad source contract and know nothing about the store.
   - Layer 4: `Varve.Store`. Owns the log, the projection contract, and the pre-commit validator contract.
   - Layer 5: integration and hosts (`Varve.Shacl` store integration, `Varve.Server`, CLI).
   The stable packages are also the abstract ones: contracts live in the lowest layer that can define them without knowing their implementers. A lower layer never learns about a higher one, including through callbacks typed to concrete higher-layer types, service location, or `InternalsVisibleTo` (allowed for test assemblies only).
2. Low coupling. Packages talk through small contracts defined over `Varve.Rdf` types. No shared mutable state between packages, no static registries, no package reaching into another's internals. Public API surface is kept minimal and tracked (public API baseline files in the repo), so every new public member is a visible decision. A format parser is usable without the store; the evaluator is usable over any in-memory dataset; the SHACL validator is usable without the store.
3. High cohesion. One package, one reason to change, aligned with one specification or one architectural concern. No `Common`, `Core`, `Utils` or `Abstractions` grab-bag packages. Shared low-level helpers (buffer pooling, UTF-8 scanning) are either internal and duplicated when small, or earn their own package by ADR.

Decided (record as an ADR in set zero): `Varve.Store` is SPARQL-free. It exposes the quad source contract for reads and the transaction contract for writes. SPARQL Update lives in an integration package in layer 5 (`Varve.Sparql.Store` or similar) that evaluates the WHERE clause against a pinned position and submits the resulting delta as one commit. Same pattern as the SHACL store integration. Hosts reference the integration packages; library users who only want the log and projections do not pay for a query engine.

## Enforcement by analyzers

Every architecture rule and code rule is implemented as a Roslyn analyzer and fails the build. A rule that exists only in a document is not a rule. When a new rule is agreed, the deliverable is the analyzer, its tests, and the doc entry, in that order of importance.

- Off-the-shelf first: the .NET trimming, AOT and single-file analyzers at error severity; `Microsoft.CodeAnalysis.PublicApiAnalyzers` for the public API baseline; `Microsoft.CodeAnalysis.BannedApiAnalyzers` for banned symbols (`System.Uri` in `Varve.Iri` consumers, reflection APIs, `Reflection.Emit`, `dynamic`, LINQ and `params` allocations in marked hot paths where a banned-symbol rule suffices).
- `Varve.Analyzers` for what is specific to us: a development-time project, referenced as an analyzer by every Varve project, never shipped as a runtime dependency. Rule ids `VARVE0001` onward, each with a doc page linking to the ADR that motivates it. Initial rules:
  - Layer rule: each project declares its layer through an MSBuild property surfaced with `CompilerVisibleProperty`; the analyzer reads the referenced Varve assemblies and their declared layers and reports any reference that is not strictly downward.
  - `InternalsVisibleTo` only toward `*.Tests` assemblies.
  - No project or namespace named `Common`, `Core`, `Utils`, `Helpers` or `Abstractions`.
  - No mutable static state and no static registries outside an explicit allow-list.
  - Hot path discipline: members marked with a `[HotPath]` attribute may not box, capture closures, allocate arrays or strings, use LINQ, or call non-hot-path-safe members.
  - Public contracts between packages use `Varve.Rdf` types or BCL primitives only.
- Suppressions require a justification string that cites an ADR number. An analyzer checks that too.
- Analyzers have their own tests (`Microsoft.CodeAnalysis.Testing`) with a violating and a conforming sample per rule.
- Analyzers run inside the compiler, so their own use of reflection or allocation is irrelevant to constraint 2. The Roslyn packages they need are build-time dependencies and still get an ADR under constraint 4.

Known limit: an analyzer sees one compilation at a time. The layer rule works because layers are declared per project. Whole-graph metrics such as instability (I = Ce / (Ca + Ce)) cannot be an analyzer; if we want them, they are a CI report, not a gate.

## The architectural thesis

The write model is an append-only log of committed transactions. A transaction is an ordered set of quad assertions and retractions plus metadata (commit position, timestamp, author/agent, cause, optional named-graph scope). Dictionary allocations for new terms are part of the same commit.

Everything queryable is a projection over that log:

- The quad indexes (the SPO/POS/OSP family and their graph-qualified variants over dictionary-encoded term ids) are the default projection.
- Each projection tracks its own log position, can be dropped and rebuilt, and can lag or be rebuilt in the background.
- A read is pinned to a log position. Snapshot isolation is therefore a property of the model, not a feature to add.

What this is supposed to buy us, and what every storage decision should be tested against:

- Time travel: query the dataset as of any commit position or timestamp, and diff two positions.
- Change feeds: subscribe to the log, filtered by graph or pattern, with at-least-once delivery and resumable positions.
- Replication and read replicas by log shipping. Backups are log copies.
- Incremental computation: continuous SPARQL queries, materialised views, and incremental SHACL validation driven by deltas instead of full re-evaluation.
- Pluggable secondary projections: full-text, vector, geospatial, or application-specific read models fed from the same log.
- Provenance and audit for free, since every quad has a commit that introduced it and possibly one that retracted it.
- Branching and merging of datasets as a later possibility; do not block it.

Known tensions that must be resolved by ADR rather than by accident:

- Log growth, compaction, and snapshotting, and what time travel guarantees survive compaction.
- Blank node identity across transactions and across time.
- Whether retraction of a non-existent quad is an event, a no-op, or an error; same for re-assertion.
- SPARQL Update semantics (DELETE/INSERT WHERE evaluated against which position) and how one update request maps to one atomic commit.
- Bulk load: a multi-billion-quad import cannot be one in-memory transaction, yet must be one logical commit or a well-defined sequence.
- Single writer per dataset versus optimistic concurrency with expected position.
- Managed storage engine for the projections: write our own LSM or B+tree over the log, or adopt a managed engine. Candidates need evaluation against constraints 1 to 4.
- GDPR-style hard deletion in an append-only model.
- Incremental SHACL: computing the affected focus-node set from a delta is straightforward for Core property shapes and hard for property paths, sh:not, recursion, and SPARQL-based constraints. Define which shapes are incrementally maintainable and what falls back to scoped or full re-validation.
- Commit-time validation cost versus write latency, and what a pre-commit validator is allowed to read (the pending transaction overlaid on the pinned position).

## Scope, mapped to Oxigraph's layering

Build as separately usable packages, each publishable on its own, roughly mirroring Oxigraph's crates. Package ids and root namespaces follow `Varve.<Component>`: `Varve.Rdf`, `Varve.Xsd`, `Varve.Iri`, `Varve.Turtle`, `Varve.RdfXml`, `Varve.JsonLd`, `Varve.Sparql`, `Varve.Sparql.Results`, `Varve.Store`, `Varve.Server`, `Varve.Shacl`. The name comes from geology: a varve is one annual sediment layer, countable and datable. One commit is one varve; an as-of read is reading the deposit up to a given layer. Use that vocabulary sparingly in docs and never in public API names, which stay conventional (Commit, Position, Projection).

1. RDF model (oxrdf): terms, triples, quads, datasets, RDF 1.2 triple terms, RDFC-1.0 canonicalisation.
2. XSD datatypes (oxsdatatypes): exact decimal, dateTime/duration families, with SPARQL operator semantics.
3. IRI handling (oxiri): RFC 3987 parsing and resolution without System.Uri quirks.
4. Parsers and serialisers (oxttl, oxrdfxml, oxjsonld, oxrdfio): N-Triples, N-Quads, Turtle, TriG, RDF/XML, JSON-LD 1.1. Streaming, push and pull, sync and async, recoverable errors with positions.
5. SPARQL algebra and parser (spargebra), optimiser (sparopt), evaluator (spareval), result formats (sparesults: XML, JSON, CSV, TSV). The evaluator works against an abstract quad source so it can run over any projection or any in-memory dataset.
6. The store: log, projections, transactions, bulk loader, as-of reads, subscriptions.
7. Server and CLI: SPARQL 1.1 Protocol, Graph Store Protocol, service description, federation via SERVICE, plus endpoints for the event-sourced features.

8. SHACL 1.2 validator: Core and SPARQL-based constraints, usable standalone over any quad source. Semantics, fixpoint treatment of recursion, and ADRs carry over from the shacl-rs specification; re-derive the implementation for .NET, do not port it. Built after the SPARQL evaluator, since SHACL-SPARQL depends on it. Two store integrations are the point of having it in-house:
   - Commit-time validation: shapes graphs bound to a dataset or named graph can gate a transaction (reject, or commit with the validation report attached as commit metadata). The commit pipeline therefore has a pre-commit validator hook from ADR set zero, even while no validator exists.
   - Incremental validation as a projection: consume deltas, compute the affected focus nodes, re-validate only those, and maintain the current validation report as a queryable, time-travellable graph.

Target RDF 1.1 and SPARQL 1.1 as the conformance baseline, with RDF 1.2 and SPARQL 1.2 in the model from the start so they are not a retrofit. The SHACL package has its own gate (the W3C SHACL test suite) and does not block core conformance. Later candidates, out of scope until the core passes conformance: reasoning, GeoSPARQL, full-text.

## Definition of correct

The W3C test suites are the acceptance gate: RDF syntax suites per format, SPARQL 1.1 query, update, protocol, results formats, federation, and the RDF 1.2 / SPARQL 1.2 suites as they stabilise. They run in CI from the first parser onward. A feature is not done until its manifest entries pass or each failure has a written, justified exemption.

Where the specs are silent, Oxigraph's behaviour is the tie-breaker, and differential testing against Oxigraph is welcome. Property-based tests are expected for parser/serialiser round trips, dictionary encoding, index ordering, projection rebuild equivalence (rebuilt projection equals incrementally maintained projection), and as-of read consistency.

Performance claims need BenchmarkDotNet numbers and a stated dataset. Compare against Oxigraph and dotNetRDF on the same hardware.

## How we work

- Specification first. For each component: a functional specification with formal grounding where it exists (SPARQL algebra semantics, the log and projection model as a formal state machine with stated invariants), then ADRs, then a scaffolded solution, then code.
- ADRs for every decision with alternatives considered and consequences. Number them, keep them short, and check new proposals against accepted ADRs. If a proposal contradicts one, say so and propose a superseding ADR instead of silently diverging.
- Keep the specification, ADRs, and code consistent. When one changes, list what else must change.
- Small vertical slices that end in passing tests, not broad scaffolding that compiles and does nothing.
- When asked for code: complete, compiling, idiomatic modern C#, with tests. No placeholder bodies unless explicitly marked as a stub and listed at the end.
- When a question touches a spec, cite the section. When unsure what a spec says, say that instead of guessing.
- Flag anything that would break AOT, trimming, or WASM the moment it appears.
- Prose in documents: plain, precise, no marketing tone. Diagrams as Mermaid or SVG when structure is easier to see than to read.

## Suggested first milestones

1. Repo layout, licence, NuGet prefix reservation for `Varve.*`, `Varve.Analyzers` with the layer rule and the off-the-shelf analyzers at error severity, CI with the W3C test manifests wired in and failing.
2. ADR set zero: the log and commit model, term dictionary and id scheme, storage abstraction, projection contract, concurrency model.
3. RDF model, IRI, XSD datatypes, N-Triples/N-Quads with full suite pass.
4. In-memory log plus default quad projection, as-of reads, rebuild equivalence property test.
5. Turtle/TriG, then SPARQL parser and algebra, then evaluator over the in-memory projection.
6. Durable managed storage backend, bulk loader, compaction strategy.
7. Server, protocol conformance, change feed endpoint.
8. SHACL validator standalone with W3C suite pass, then commit-time gating, then the incremental validation projection.
