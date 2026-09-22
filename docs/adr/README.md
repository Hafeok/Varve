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
| [0003](0003-package-layering.md) | Package layering and the strictly downward reference rule | Accepted |
| [0004](0004-enforcement-by-analyzers.md) | Enforcement by analyzers | Accepted |
| [0005](0005-store-is-sparql-free.md) | `Varve.Store` is SPARQL-free | Accepted |
| [0006](0006-build-and-test-dependencies.md) | Build-time and test-time dependencies | **Superseded by 0009** |
| [0007](0007-w3c-conformance-harness.md) | W3C conformance harness | Accepted |
| [0008](0008-target-framework-and-language.md) | Target framework and language version policy | Accepted |
| [0009](0009-dependency-policy-and-register.md) | Dependency policy and the enforced register | Accepted |

## Set zero — the log and projection model

| # | Title | Status |
|---:|---|---|
| [0010](0010-commit-model-and-effective-deltas.md) | Commit model and effective deltas | Accepted |
| [0011](0011-concurrency-single-sequencer.md) | Concurrency: one sequencer, optional expected position | Accepted |
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
| [0026](0026-hotpath-attribute.md) | Where the `[HotPath]` attribute lives | Accepted |
| [0027](0027-benchmarking.md) | Benchmarking | Accepted |

## Milestone 3a close-out

| # | Title | Status |
|---:|---|---|
| [0028](0028-deterministic-aead-from-hmac.md) | A deterministic AEAD built from HMAC-SHA-256 | Accepted; condition 1 **discharged** 2026-09-22, condition 2 open |
| [0029](0029-publishing-and-versioning.md) | Publishing and versioning | Accepted |

## Milestone 3b — Turtle and TriG

| # | Title | Status |
|---:|---|---|
| [0030](0030-turtle-recovery-and-prefixes.md) | Turtle's recovery unit, prefix exposure, and where isomorphism lives | Accepted |

## Stewardship — the Mind Over Machine standard

Adopted at [#16](https://github.com/Hafeok/Varve/issues/16). Four of these
reverse a decision this repository had already made, which is why each is a
superseding ADR rather than an edit.

| # | Title | Status |
|---:|---|---|
| [0031](0031-licence-mpl-2-0.md) | Licence: MPL-2.0 | Accepted; **supersedes 0002** |

**No ADR in this repository is `Proposed`.** Three were, and were completed in
place rather than superseded, because ADR 0001's no-edit rule binds accepted
decisions and they had never been accepted.

### Accepted ahead of the evidence

Five ADRs carry a **revisit condition**: a stated fact which, if it turns out to
be true, supersedes the ADR. It does not edit it.

**One has fired.** ADR 0020's condition was its acceptance condition, it was
tested on a real browser at milestone 3a, and it failed. Its successor, 0028,
carries a condition of its own that is not measurable by a build.

| ADR | Condition | Due |
|---|---|---|
| [0012](0012-term-dictionary-and-id-scheme.md) | Milestone 4 benchmarks of index size and scan throughput contradict 64-bit counter-allocated ids. No bytes are frozen before milestone 6. | milestone 4 |
| [0018](0018-storage-abstraction.md) | The in-memory and browser backends cannot both implement the contract without leaking backend detail. Members fixed when the first backend is written. | milestone 4 |
| [0020](0020-cipher-for-erasure-mode.md) | ~~AES-CBC, HMAC-SHA-256 and HKDF do not run on browser WASM when verified on a real build.~~ **Fired** at milestone 3a: `Aes.Create()` throws on browser-wasm and no symmetric cipher of any kind is available there. Superseded by 0028. | **fired**, superseded |
| [0028](0028-deterministic-aead-from-hmac.md) | External cryptographic review rejects the construction. The fallback is then that erasure mode does not run in the browser, in its own ADR. Its other condition — the primitives run in a browser — is measured and holds. | milestone 9, before shipping |
| [0022](0022-quad-source-term-handle.md) | Milestone 5 evaluator benchmarks show the opaque handle costs more than it saves. | milestone 5 |

## Open questions recorded, not resolved

From the specification's §11, each owned by the ADR that records the decision it
falls out of:

| | Question | Owner | Due |
|---|---|---|---|
| **Q1** | External form of store-scoped blank node identity at API and protocol boundaries | [0012](0012-term-dictionary-and-id-scheme.md) | milestone 4 |
| **Q2** | Bulk load and I2 — normalising a huge commit against a populated dataset | [0013](0013-records-commits-and-bulk-load.md) | milestone 6 |
| **Q3** | Bulk load and validators — an overlay that does not fit in memory | [0013](0013-records-commits-and-bulk-load.md), with [0017](0017-validator-contract-and-overlay.md) | milestone 6 |
| **Q4** | How a shredded term appears in SPARQL results and serialisations | [0023](0023-erasure-and-access-requests.md) | milestone 9 |
| **Q5** | Lookup by private value — scan and decrypt, or a keyed blind index | [0028](0028-deterministic-aead-from-hmac.md) | milestone 9 |
| **Q6** | Cipher and availability per host | [0028](0028-deterministic-aead-from-hmac.md) | **decided**, subject to 0028's two conditions: the primitives run in a browser (measured, holds) and the construction survives external review (milestone 9) |
| **Q7** | ~~Whether an access request defaults to `G_head` or every quad ever asserted~~ | [0023](0023-erasure-and-access-requests.md) | **closed** — by key id, scope a dataset setting defaulting to `AllHistory` |
| **Q8** | Key granularity when one term is about two data subjects | [0023](0023-erasure-and-access-requests.md) | milestone 9 |
| **Q9** | Whether the structure surviving shredding counts as anonymous — **a legal question** | [0023](0023-erasure-and-access-requests.md) | milestone 9 |

From ADR 0003, and still unresolved:

- **0003 open question 1** — `Varve.Shacl` and the SPARQL evaluator are both
  placed in layer 3, yet SHACL-SPARQL depends on the evaluator. Due milestone 8.
- **0003 open question 2** — the SPARQL optimiser and evaluator are both in
  layer 3. If the evaluator consumes a plan type the optimiser owns, they are one
  package or two layers. Due milestone 5.

## Obligations recorded against a later milestone

- **ADR 0004** — the `System.Uri` ban is wider than the brief scopes it. Narrow
  before layer 5 exists.
- **ADR 0011** — banned-symbols entry for ambient clock and randomness under
  `Varve.Store`, which §10's determinism test depends on. Due milestone 4.
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
