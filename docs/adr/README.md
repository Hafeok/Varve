# Architecture decisions

Format, numbering and the supersession rule are in
[0001](0001-record-architecture-decisions.md). An accepted ADR is not edited; a
change of mind is a new ADR whose Status names the one it supersedes.

`docs/spec/log-and-projection-model.md` is the authority for `Varve.Store`
behaviour. ADRs 0010–0020 record the decisions it presupposes; where they refine
or depart from `docs/brief.md`, each says so in its Context.

## Set zero — foundation

| # | Title | Status |
|---:|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-licence.md) | Licence: Apache-2.0 | **Superseded by 0031** |
| [0003](0003-package-layering.md) | Package layering and the strictly downward reference rule | Accepted; layer table **superseded by 0060**; declaration **by 0064**; suppression escape **by 0062** |
| [0004](0004-enforcement-by-analyzers.md) | Enforcement by analyzers | Accepted; reservation table **superseded by 0062** |
| [0005](0005-store-is-sparql-free.md) | `Varve.Store` is SPARQL-free | Accepted |
| [0006](0006-build-and-test-dependencies.md) | Build-time and test-time dependencies | **Superseded by 0009** |
| [0007](0007-w3c-conformance-harness.md) | W3C conformance harness | Accepted |
| [0008](0008-target-framework-and-language.md) | Target framework and language version policy | Accepted |
| [0009](0009-dependency-policy-and-register.md) | Dependency policy and the enforced register | Accepted; **amended by 0063** |

## Set zero — the log and projection model

| # | Title | Status |
|---:|---|---|
| [0010](0010-commit-model-and-effective-deltas.md) | Commit model and effective deltas | Accepted |
| [0011](0011-concurrency-single-sequencer.md) | Concurrency: one sequencer, optional expected position | Accepted; primitive results **superseded by 0065** |
| [0012](0012-term-dictionary-and-id-scheme.md) | Term dictionary, id classes, id scheme, blank node identity | Accepted |
| [0013](0013-records-commits-and-bulk-load.md) | Records versus commits, and bulk load | Accepted |
| [0014](0014-header-chain-and-divergence.md) | Header chain and divergence detection | Accepted |
| [0015](0015-checkpoints-and-reads.md) | Checkpoints, pinned reads, as-of reads, archive horizon | Accepted |
| [0016](0016-projection-contract-and-subscriptions.md) | Projection contract, synchronous default projection, erasure in projections | Accepted |
| [0017](0017-validator-contract-and-overlay.md) | Pre-commit validator contract and the overlay quad source | Accepted |
| [0018](0018-storage-abstraction.md) | Storage abstraction: memory, file, browser | Accepted |
| [0019](0019-erasure-by-crypto-shredding.md) | Erasure by crypto-shredding | **Superseded by 0023** |
| [0020](0020-cipher-for-erasure-mode.md) | Cipher for erasure mode | **Superseded by 0028** |
| [0021](0021-dataset-settings-as-a-commit-kind.md) | Dataset settings as a commit kind | Accepted |
| [0022](0022-quad-source-term-handle.md) | The quad source contract over an opaque term handle | Accepted |
| [0023](0023-erasure-and-access-requests.md) | Erasure by crypto-shredding, and access requests | Accepted |

## Milestone 3a — the model and the syntax

| # | Title | Status |
|---:|---|---|
| [0024](0024-rdf-term-representation.md) | RDF term representation | Accepted |
| [0025](0025-property-based-testing.md) | CsCheck for property-based testing | Accepted |
| [0026](0026-hotpath-attribute.md) | Where the `[HotPath]` attribute lives | Accepted; placement **superseded by 0064** |
| [0027](0027-benchmarking.md) | Benchmarking | Accepted |

## Milestone 3a close-out

| # | Title | Status |
|---:|---|---|
| [0028](0028-deterministic-aead-from-hmac.md) | A deterministic AEAD built from HMAC-SHA-256 | Accepted; condition 1 **discharged** 2026-09-22, condition 2 open |
| [0029](0029-publishing-and-versioning.md) | Publishing and versioning | Accepted |

## Milestone 3b — Turtle and TriG

| # | Title | Status |
|---:|---|---|
| [0030](0030-turtle-recovery-and-prefixes.md) | Turtle's recovery unit, prefix exposure, and where isomorphism lives | Accepted; deletion clause **superseded by 0059** |

## Stewardship — the Mind Over Machine standard

Adopted at [#16](https://github.com/Hafeok/Varve/issues/16). Four of these
reverse a decision this repository had already made, which is why each is a
superseding ADR rather than an edit.

| # | Title | Status |
|---:|---|---|
| [0031](0031-licence-mpl-2-0.md) | Licence: MPL-2.0 | Accepted; **supersedes 0002** |
| [0032](0032-trunk-based-development.md) | Trunk-based development and non-blocking review | Accepted; amended 2026-09-24 (the gate runs after the push); **amended by 0066** |
| [0033](0033-commit-traceability.md) | Commit traceability and AI-session records | Accepted |
| [0034](0034-commit-signing-and-the-sandbox-exception.md) | Commit signing and the sandbox exception | Accepted; revisit condition; **amended by 0066** |
| [0035](0035-semantic-versioning.md) | Semantic versioning | Accepted; complements 0029 |
| [0036](0036-containerised-development.md) | Containerised development and the local pipeline | Accepted |


## Milestone 4 — the in-memory log

Issue [#8](https://github.com/Hafeok/Varve/issues/8). The first code against the
specification, which moved to version 1.2 with 0046 and to 1.3 with 0047.

| # | Title | Status |
|---:|---|---|
| [0040](0040-storage-contract-members-and-the-memory-backend.md) | The storage contract's members, and where the memory backend lives | Accepted; member types **superseded by 0065** |
| [0041](0041-sorted-runs-for-the-default-projection-and-checkpoints.md) | Sorted runs for the default projection and for checkpoints | Accepted |
| [0042](0042-subscriptions-pull-from-the-log.md) | Subscriptions pull from the log | Accepted |
| [0043](0043-the-reference-model-as-a-test-asset.md) | The reference model is a test asset | Accepted |
| [0044](0044-blank-node-identity-in-process.md) | Blank node identity at the in-process boundary (Q1, split) | Accepted |
| [0045](0045-the-provisional-in-memory-log-encoding.md) | The provisional log encoding, and the in-memory id layout | Accepted, provisional by design |
| [0046](0046-settings-commits-reach-every-subscriber.md) | Settings commits reach every subscriber; specification 1.2 | Accepted; **amends 0016** |
| [0047](0047-delta-composition-and-closure-over-triple-terms.md) | Delta composition over chains, and dictionary closure over triple terms; specification 1.3 | Accepted |

## Milestone 5a — `Varve.Xsd`, the SPARQL algebra and parser

Issue [#9](https://github.com/Hafeok/Varve/issues/9). The maintainer's
positions from the close of milestone 4, written as decisions before the
first SPARQL line; 0051 and 0052 also carry the answers to the plan's
questions.

| # | Title | Status |
|---:|---|---|
| [0048](0048-optimiser-and-evaluator-one-package-algebra-in-algebra-out.md) | Optimiser and evaluator: one package, algebra in and algebra out; closes 0003's open question 2 | Accepted |
| [0049](0049-cardinality-estimates-on-the-quad-source.md) | Cardinality estimates on the quad source | Accepted; widens 0022; `Count`'s type **superseded by 0065** |
| [0050](0050-typed-value-accessor-and-the-benchmark-for-adr-0022.md) | A typed-value accessor beside the handle, and the benchmark ADR 0022 asked for | Accepted; widens 0022, measurement due 5b |
| [0051](0051-varve-xsd-scope-and-precision.md) | `Varve.Xsd`: scope, precision policy, canonical forms, and value comparison | Accepted; dateTime order verified in 5b |
| [0052](0052-pinned-read-lifetime.md) | A pinned read lives for one query execution | Accepted; refines 0015 |

## Milestone 5b — the evaluator and the optimiser

Issue [#9](https://github.com/Hafeok/Varve/issues/9). The five questions 5a
left open, decided by the maintainer on the 5b plan. The dateTime order stays
ADR 0051's and ADR 0050's benchmark stays as written; both are carried out in
`docs/spec/sparql-evaluation.md`. Dated notes the same day: 0004 (VARVE0007's
reserved wording admits algebra types), 0027 (dotNetRDF generates the RDF/XML
fixtures offline), 0048 (the evaluation package references `Varve.Iri`).

| # | Title | Status |
|---:|---|---|
| [0053](0053-aggregation-by-hash-grouping-and-accumulators.md) | Aggregation: hash grouping, one accumulator per aggregate | Accepted |
| [0054](0054-property-paths-normalised-then-evaluated-by-alp.md) | Property paths: normalised to joins and unions, closures by `ALP`; closes `sparql-algebra.md` open question 1 | Accepted |
| [0055](0055-service-through-a-handler-the-default-refuses.md) | `SERVICE` through a handler, and the default refuses | Accepted |
| [0056](0056-evaluator-options-extension-functions-clock-and-randomness.md) | Evaluator options: extension functions, the clock and randomness are injected; extends 0011's ban | Accepted |


## Milestone 5c — result writers, SPARQL Update, canonicalisation

Decided by the maintainer on the 5c plan.

| # | Title | Status |
|---:|---|---|
| [0057](0057-sparql-update-one-request-one-commit.md) | SPARQL Update over the store: one request, one commit | Accepted; amended 2026-09-25 (release before submit) |
| [0058](0058-staging-view-and-dataset-validators.md) | A staging view over a pinned read, and validators bound to a dataset | Accepted; refines 0017 |
| [0059](0059-rdfc-in-varve-rdf-and-its-work-limit.md) | RDFC-1.0 in `Varve.Rdf`, bounded by a work limit; the isomorphism check stays as a cross-check | Accepted; supersedes 0030's deletion clause |

## After milestone 5c — hosts and the canonical form

Decided by the maintainer on the 5c report.

| # | Title | Status |
|---:|---|---|
| [0060](0060-hosts-at-layer-6-the-composition-root.md) | Hosts at layer 6: the composition root, reserved to executables | Accepted; supersedes 0003's layer table |
| [0061](0061-canonical-n-triples-is-rdf-1-2s.md) | Canonical N-Triples and N-Quads follow RDF 1.2; where 1.1 and 1.2 differ, 1.2 wins | Accepted |

## Adopting `DecisionDriven.Analyzers`

Issue [#43](https://github.com/Hafeok/Varve/issues/43). Decided by the
maintainer on the adoption plan. Every ADR is also a decision set in
[`docs/decisions/`](../decisions/), which code cites as a type.

| # | Title | Status |
|---:|---|---|
| [0062](0062-adopting-decisiondriven-analyzers.md) | Adopting `DecisionDriven.Analyzers`: generic rules from a package, Varve's own rules only in `Varve.Analyzers` | Accepted; supersedes 0004's reservation table and 0003's suppression escape |
| [0063](0063-build-time-analyzer-packages.md) | Build-time packages for the analyzers, and what `BannedSymbols.txt` must cite | Accepted; amends 0009 |
| [0064](0064-varve-configuration-and-hot-path-rules.md) | Varve's configuration of the `DD` rules; the hot-path rules `VARVE0003` and `VARVE0004`; the layer declaration `VARVE0005` | Accepted; supersedes 0026's placement and 0003's declaration |
| [0065](0065-wrapper-types-and-the-store-log-namespace.md) | Positions, ids and sizes as wrapper types; the log's values in `Varve.Store.Log` | Accepted; supersedes 0011, 0040 and 0049 in part |
| [0066](0066-expected-red-pull-requests.md) | Expected-red pull requests: citing a decision nobody has accepted yet | Accepted; amends 0032 and 0034 |
| [0067](0067-inmemorydataset-is-a-value-built-by-a-builder.md) | `InMemoryDataset` is an immutable value, assembled by `InMemoryDatasetBuilder` | Accepted |

## Milestone 7 — the server

Accepted ahead of the milestone, because the decision bears on what the server
is allowed to become and the cheapest time to reject API keys is before anyone
has written one.

| # | Title | Status |
|---:|---|---|
| [0037](0037-server-authentication.md) | Authentication and authorisation for the server | Accepted |

## Conformance — differential testing against Oxigraph

| # | Title | Status |
|---:|---|---|
| [0038](0038-upstream-contribution-policy.md) | Upstream contribution to Oxigraph and the shared test suites | Accepted; refines the brief's tie-breaker |

## Tooling — repo-standard

Built here and moving to a repository of its own; see
[#23](https://github.com/Hafeok/Varve/issues/23).

| # | Title | Status |
|---:|---|---|
| [0039](0039-repo-standard.md) | repo-standard: repository settings as code, built here and moving out | Accepted |

**No ADR in this repository is `Proposed`.** Three were, and were completed in
place rather than superseded, because ADR 0001's no-edit rule binds accepted
decisions and they had never been accepted.

### Accepted ahead of the evidence

Six ADRs carry a **revisit condition**: a stated fact which, if it turns out to
be true, supersedes the ADR. It does not edit it.

**One has fired.** ADR 0020's condition was its acceptance condition, it was
tested on a real browser at milestone 3a, and it failed. Its successor, 0028,
carries a condition of its own that is not measurable by a build.

| ADR | Condition | Due |
|---|---|---|
| [0012](0012-term-dictionary-and-id-scheme.md) | Milestone 4 benchmarks of index size and scan throughput contradict 64-bit counter-allocated ids. No bytes are frozen before milestone 6. | **did not fire** at milestone 4: 192 bytes per quad for six orders, 37–45 M quads/s scanned (`tests/Varve.Benchmarks/README.md`). The on-disk locality hypothesis waits for milestone 6 |
| [0018](0018-storage-abstraction.md) | The in-memory and browser backends cannot both implement the contract without leaking backend detail. Members fixed when the first backend is written. | members **fixed** by [0040](0040-storage-contract-members-and-the-memory-backend.md); the memory backend and a second one outside the assembly implement them with nothing leaked. The browser half waits for milestone 6 |
| [0020](0020-cipher-for-erasure-mode.md) | ~~AES-CBC, HMAC-SHA-256 and HKDF do not run on browser WASM when verified on a real build.~~ **Fired** at milestone 3a: `Aes.Create()` throws on browser-wasm and no symmetric cipher of any kind is available there. Superseded by 0028. | **fired**, superseded |
| [0028](0028-deterministic-aead-from-hmac.md) | External cryptographic review rejects the construction. The fallback is then that erasure mode does not run in the browser, in its own ADR. Its other condition — the primitives run in a browser — is measured and holds. | milestone 9, before shipping |
| [0022](0022-quad-source-term-handle.md) | Milestone 5 evaluator benchmarks show the opaque handle costs more than it saves. The accessor it named as its successor is built in 5a ([0050](0050-typed-value-accessor-and-the-benchmark-for-adr-0022.md)), which fixes the three arms and the verdict rule. | milestone 5b |
| [0034](0034-commit-signing-and-the-sandbox-exception.md) | A route appears by which a sandbox commit is signed by a key the project controls, or by GitHub itself. Two are identified and neither is available: GraphQL `createCommitOnBranch`, blocked by the session proxy rather than by GitHub, and a per-installation signing key. The REST contents API was tested and is **not** one — it produces unsigned commits. | whenever it fires |

## Open questions recorded, not resolved

From the specification's §11, each owned by the ADR that records the decision it
falls out of:

| | Question | Owner | Due |
|---|---|---|---|
| **Q1** | External form of store-scoped blank node identity at protocol boundaries. The in-process half is **decided** by [0044](0044-blank-node-identity-in-process.md): by handle | [0012](0012-term-dictionary-and-id-scheme.md) | milestone 7, with the server |
| **Q2** | Bulk load and I2 — normalising a huge commit against a populated dataset | [0013](0013-records-commits-and-bulk-load.md) | milestone 6 |
| **Q3** | Bulk load and validators — an overlay that does not fit in memory | [0013](0013-records-commits-and-bulk-load.md), with [0017](0017-validator-contract-and-overlay.md) | milestone 6 |
| **Q4** | How a shredded term appears in SPARQL results and serialisations | [0023](0023-erasure-and-access-requests.md) | milestone 9 |
| **Q5** | Lookup by private value — scan and decrypt, or a keyed blind index | [0028](0028-deterministic-aead-from-hmac.md) | milestone 9 |
| **Q6** | Cipher and availability per host | [0028](0028-deterministic-aead-from-hmac.md) | **decided**, subject to 0028's two conditions: the primitives run in a browser (measured, holds) and the construction survives external review (milestone 9) |
| **Q7** | ~~Whether an access request defaults to `G_head` or every quad ever asserted~~ | [0023](0023-erasure-and-access-requests.md) | **closed** — by key id, scope a dataset setting defaulting to `AllHistory` |
| **Q8** | Key granularity when one term is about two data subjects | [0023](0023-erasure-and-access-requests.md) | milestone 9 |
| **Q9** | Whether the structure surviving shredding counts as anonymous — **a legal question** | [0023](0023-erasure-and-access-requests.md) | milestone 9 |

From ADR 0003:

- **0003 open question 1** — `Varve.Shacl` and the SPARQL evaluator are both
  placed in layer 3, yet SHACL-SPARQL depends on the evaluator. Due milestone 8.
- **0003 open question 2** — ~~the SPARQL optimiser and evaluator are both in
  layer 3. If the evaluator consumes a plan type the optimiser owns, they are one
  package or two layers.~~ **Closed** 2026-09-24 by
  [0048](0048-optimiser-and-evaluator-one-package-algebra-in-algebra-out.md):
  no plan type, one layer 3 package.

## Obligations recorded against a later milestone

- **ADR 0038** — the differential harness enforces the four exemption checks
  (category, required references, no `varve-defect`, stale entry), each with a
  violating and a conforming test. Due with the differential harness. Its open
  question (whether a `spec-gap` entry must cite a W3C suite PR) is due then too.
- **ADR 0004** — the `System.Uri` ban is wider than the brief scopes it. Narrow
  before layer 5 exists.
- **ADR 0014** — the storage format must carry a version discriminator from the
  start, so a change to what is hashed is a migration rather than a corruption.

## Closed

- **ADR 0007 / 0027** — `dotNetRdf.Core`'s *manifest-reading* use. Closed
  2026-09-22 at milestone 3b, two milestones early: `Varve.Turtle` passes
  `rdf/rdf11/rdf-turtle` 313 of 313 and `rdf/rdf11/rdf-trig` 357 of 357
  unexempted, the harness reads its own manifests, and
  `The_harness_does_not_reference_another_rdf_implementation` keeps it out. The
  package stays for the benchmark baseline on ADR 0027's separate
  justification, and its register citation moved from 0007 to 0027 with this
  closure.
- **ADR 0002** — the copyright holder. Closed 2026-09-21: Emil Okkels Klein,
  named in `NOTICE`. The `LICENSE` appendix stays unedited, because it is the
  per-file boilerplate template and not a record of ownership.
- **ADR 0004** — the known limit that `VARVE0002`'s analyzer exemption was by
  name and could not be closed. Withdrawn 2026-09-21: it was wrong on both
  counts, and the rule now checks `IsPackable` and refuses the analyzer as a
  library reference.
