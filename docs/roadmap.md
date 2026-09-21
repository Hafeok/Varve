# Roadmap

Milestones are from `docs/brief.md`. This file adds only the deferred items and
the point at which each becomes due.

## 1 — Foundation *(complete)*

Repository layout, licence, NuGet prefix reservation for `Varve.*`, build
infrastructure with the off-the-shelf analyzers at error severity,
`Varve.Analyzers` with the layer rule, CI with the W3C manifests wired in and
failing.

Delivered: everything above except the NuGet prefix reservation, which is a
manual request to nuget.org and cannot be automated. It is not on the critical
path until milestone 3 produces the first packable project.

## 2 — ADR set zero *(complete)*

`docs/spec/log-and-projection-model.md` is the functional specification and the
authority for `Varve.Store` behaviour. ADRs [0010–0020](adr/README.md) record the
decisions it presupposes.

### The brief's tensions, and where each stands

| Tension | Resolution |
|---|---|
| Log growth, compaction, and what time travel survives it | **Resolved** — ADR 0015. There is no compaction in the destructive sense; checkpoints give bounded read cost and the log is retained. |
| Blank node identity across transactions and across time | **Resolved in shape** — ADR 0012: store-scoped identity allocated at commit, request labels request-scoped. The external form is **Q1**. |
| Retraction of a non-existent quad; re-assertion | **Resolved** — ADR 0010. Neither event nor error; the log records the effective delta. |
| SPARQL Update semantics and one request to one commit | **Resolved** — ADR 0005 places Update at layer 5; it evaluates `WHERE` against a pinned position and submits the delta as one commit (ADRs 0010, 0011). |
| Bulk load as one logical commit | **Resolved** — ADR 0013: a commit is one or more records, closed by a flag in the log. Remaining: **Q2**, **Q3**. |
| Single writer versus optimistic concurrency | **Resolved** — ADR 0011: both. One sequencer, optional expected position per request. |
| Managed storage engine for the projections | **Open** — `docs/research/managed-storage-engines.md` narrows it; ADR 0018 states the contract requirements. Decided at milestone 6. |
| GDPR-style hard deletion in an append-only model | **Resolved** — ADRs 0023 and 0020: crypto-shredding, opt-in per dataset, access by key id. Remaining: **Q4**, **Q5**, **Q8**, **Q9**. |
| Commit-time validation cost versus write latency, and what a validator may read | **Resolved** — ADR 0017: the overlay of the pending delta on the pinned state, and nothing else; the hook is inside the sequencer, so validation is write latency by construction. |
| Incremental SHACL — which shapes are incrementally maintainable | **Open** — not addressed by this set. Due milestone 8. |

The pre-commit validator hook is specified here even though no validator exists
until milestone 8, and the key store, classifier and selector contracts are
specified here even though erasure is not implemented until the milestone
proposed below.

## 3 — RDF model, IRI, XSD datatypes, N-Triples and N-Quads *(current: 3a)*

**3a**: `Varve.Iri`, `Varve.Rdf`, and N-Triples and N-Quads in `Varve.Turtle`, to a
full suite pass. **3b**: `Varve.Xsd` and RDFC-1.0 canonicalisation — neither is
needed for the N-Triples and N-Quads suites.

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

Due here: **Q1** (blank node identity at the API boundary, ADR 0012), and the
banned-symbols entry for ambient clock and randomness under `Varve.Store` that
§10's determinism test depends on (ADR 0011).

## 5 — Turtle and TriG, then the SPARQL parser and algebra, then the evaluator

The evaluator runs over the in-memory projection through the abstract quad
source contract. `Varve.Turtle` arriving here retires the test-only dotNetRDF
dependency in the conformance harness — see
`docs/adr/0007-w3c-conformance-harness.md`.

## 6 — Durable managed storage backend

Bulk loader against the decisions from milestone 2. Not a compaction strategy:
ADR 0015 decides there is none in the destructive sense, and a storage engine
that merges and discards superseded records is right for `derived/` and wrong
for `log/`. `docs/research/managed-storage-engines.md` is the note that informs
the choice, and it argues the two halves should be decided separately.

Due here: **Q2** and **Q3** (bulk load against I2, and an overlay that does not
fit in memory — ADR 0013), and the version discriminator in the storage format
that ADR 0014 requires from the first byte written.

**The first durable format reserves erasure's shape even though erasure is not
built until milestone 9**, because retrofitting any of it would be a format
change: the **private id class** (ADR 0012), the **private entry layout** —
`(KeyId, ciphertext)` covering the whole term encoding — and the file backend's
**refusal of a key store path inside the dataset directory** (ADR 0023). None of
them costs anything while erasure mode is off.

## 7 — Server and CLI

SPARQL 1.1 Protocol, Graph Store Protocol, service description, federation, and
the endpoints for the event-sourced features.

Layer 5 exists from here, which makes the `System.Uri` ban in
`eng/BannedSymbols.txt` due for narrowing (ADR 0004): a server speaks HTTP and
needs transport addresses that are not RDF IRIs.

## 8 — SHACL

Standalone with a W3C suite pass, then commit-time gating, then the incremental
validation projection.

## 9 — Erasure mode

Crypto-shredding: the key store, classifier and selector contracts, private
terms, `T4 Erase`, access requests by key id, and the classification gate
(ADRs 0020, 0021, 0023).

**After SHACL, not before the server.** The shape-derived classifier and the
classification gate both depend on the validator, so erasure cannot be honestly
finished before milestone 8 — a classifier with no shapes to derive from can
only be hand-written, and the gate that stops unclassified personal data
reaching the log is validator policy. Most datasets will also never turn
erasure mode on, which is the second reason it does not belong earlier.

Milestone 6 reserves what a format change would otherwise cost: see there.

Due here: **Q4** (how a shredded term appears in SPARQL results and
serialisations), **Q5** (lookup by private value — scan and decrypt, or a blind
index that weakens I10), **Q8** (key granularity when one term is about two data
subjects), and **Q9** (whether the surviving structure counts as anonymous).

**Q9 is a legal question and Q4 has a legal edge.** The engineering answer to Q9
is the classifier's ability to make identifying links private; whether that is
enough is not an engineering answer at all.

**Q6 is decided** (ADR 0020) and its browser half is verified at milestone 3a,
because ADR 0020's acceptance depends on it.

## Not scheduled

Reasoning, GeoSPARQL and full-text are out of scope until the core passes
conformance. Branching and merging of datasets is a later possibility that
milestone 2 must not block.

Whole-graph coupling metrics (instability, `I = Ce / (Ca + Ce)`) cannot be an
analyzer, because an analyzer sees one compilation at a time. If we want them
they are a CI report, never a gate.
