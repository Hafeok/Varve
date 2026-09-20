# Roadmap

Milestones are from `docs/brief.md`. This file adds only the deferred items and
the point at which each becomes due.

## 1 — Foundation *(current)*

Repository layout, licence, NuGet prefix reservation for `Varve.*`, build
infrastructure with the off-the-shelf analyzers at error severity,
`Varve.Analyzers` with the layer rule, CI with the W3C manifests wired in and
failing.

Delivered: everything above except the NuGet prefix reservation, which is a
manual request to nuget.org and cannot be automated. It is not on the critical
path until milestone 3 produces the first packable project.

## 2 — ADR set zero

The log and commit model, the term dictionary and id scheme, the storage
abstraction, the projection contract, the concurrency model.

The known tensions in the brief are resolved here, by ADR rather than by
accident: log growth and compaction against what time travel survives it; blank
node identity across transactions and across time; whether retracting an absent
quad is an event, a no-op or an error; SPARQL Update position semantics; bulk
load as one logical commit; single writer versus optimistic concurrency with an
expected position; the managed storage engine choice; hard deletion in an
append-only model.

The pre-commit validator hook is specified in this set even though no validator
exists until milestone 8.

## 3 — RDF model, IRI, XSD datatypes, N-Triples and N-Quads

The first packable projects, and therefore the first time several milestone 1
mechanisms stop being inert:

- **Public API baselines.** `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`
  are wired in `Directory.Build.targets` but have nothing to track until
  `Varve.Iri` exists.
- **Banned symbols.** `eng/BannedSymbols.txt` applies to packable projects. Its
  `System.Uri` entry needs its scope revisited before layer 5 — see
  `docs/adr/0004-enforcement-by-analyzers.md`.
- **Native AOT smoke build.** *Deferred from milestone 1.* There is nothing to
  compile ahead of time until there is a type. Due here: a console project that
  references `Varve.Iri`, published with `PublishAot=true`, built in CI on
  ubuntu and windows. It gates on IL-prefixed warnings, which never enter
  `NoWarn`.
- **WASM smoke build.** *Deferred from milestone 1*, for the same reason. Due
  here: a `wasi-wasm` or Blazor WebAssembly project referencing `Varve.Iri`,
  published in CI. Constraint 3 says the browser is a first-class host; a host
  that is only checked at milestone 7 is a host that will not work.
- **Conformance turns green.** The N-Triples and N-Quads suites are the first to
  move off zero, and `tests/Varve.Conformance.Tests/baseline/passing.txt` starts
  ratcheting.

## 4 — In-memory log and default quad projection

As-of reads, and the property test that a rebuilt projection equals an
incrementally maintained one.

## 5 — Turtle and TriG, then the SPARQL parser and algebra, then the evaluator

The evaluator runs over the in-memory projection through the abstract quad
source contract. `Varve.Turtle` arriving here retires the test-only dotNetRDF
dependency in the conformance harness — see
`docs/adr/0007-w3c-conformance-harness.md`.

## 6 — Durable managed storage backend

Bulk loader and compaction strategy, against the decisions from milestone 2.

## 7 — Server and CLI

SPARQL 1.1 Protocol, Graph Store Protocol, service description, federation, and
the endpoints for the event-sourced features.

## 8 — SHACL

Standalone with a W3C suite pass, then commit-time gating, then the incremental
validation projection.

## Not scheduled

Reasoning, GeoSPARQL and full-text are out of scope until the core passes
conformance. Branching and merging of datasets is a later possibility that
milestone 2 must not block.

Whole-graph coupling metrics (instability, `I = Ce / (Ca + Ce)`) cannot be an
analyzer, because an analyzer sees one compilation at a time. If we want them
they are a CI report, never a gate.
