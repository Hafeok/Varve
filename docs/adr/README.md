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
| [0002](0002-licence.md) | Licence: Apache-2.0 | Accepted |
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
| [0012](0012-term-dictionary-and-id-scheme.md) | Term dictionary, id classes, id scheme, blank node identity | **Proposed** |
| [0013](0013-records-commits-and-bulk-load.md) | Records versus commits, and bulk load | Accepted |
| [0014](0014-header-chain-and-divergence.md) | Header chain and divergence detection | Accepted |
| [0015](0015-checkpoints-and-reads.md) | Checkpoints, pinned reads, as-of reads, archive horizon | Accepted |
| [0016](0016-projection-contract-and-subscriptions.md) | Projection contract, synchronous default projection, erasure in projections | Accepted |
| [0017](0017-validator-contract-and-overlay.md) | Pre-commit validator contract and the overlay quad source | Accepted |
| [0018](0018-storage-abstraction.md) | Storage abstraction: memory, file, browser | **Proposed** |
| [0019](0019-erasure-by-crypto-shredding.md) | Erasure by crypto-shredding | Accepted |
| [0020](0020-cipher-for-erasure-mode.md) | Cipher for erasure mode | **Proposed** |

The three `Proposed` ADRs bring options and deliberately do not choose. Each
names what would have to be true for a choice to be made.

## Open questions recorded, not resolved

From the specification's §11, each owned by the ADR that records the decision it
falls out of:

| | Question | Owner | Due |
|---|---|---|---|
| **Q1** | External form of store-scoped blank node identity at API and protocol boundaries | [0012](0012-term-dictionary-and-id-scheme.md) | milestone 4 |
| **Q2** | Bulk load and I2 — normalising a huge commit against a populated dataset | [0013](0013-records-commits-and-bulk-load.md) | milestone 6 |
| **Q3** | Bulk load and validators — an overlay that does not fit in memory | [0013](0013-records-commits-and-bulk-load.md), with [0017](0017-validator-contract-and-overlay.md) | milestone 6 |
| **Q4** | How a shredded term appears in SPARQL results and serialisations | [0019](0019-erasure-by-crypto-shredding.md) | before erasure ships; no later than milestone 7 |
| **Q5** | Lookup by private value — scan and decrypt, or a keyed blind index | [0020](0020-cipher-for-erasure-mode.md) | before erasure ships |
| **Q6** | Cipher and availability per host | [0020](0020-cipher-for-erasure-mode.md) | before erasure ships |
| **Q7** | Whether an access request defaults to `G_head` or every quad ever asserted — **needs legal input** | [0019](0019-erasure-by-crypto-shredding.md) | before erasure ships |
| **Q8** | Key granularity when one term is about two data subjects | [0019](0019-erasure-by-crypto-shredding.md) | before erasure ships |

Raised while writing set zero and not in §11:

- **Durability levels in the storage contract.** Flushing to disk and committing
  an IndexedDB transaction are not the same guarantee.
  [0018](0018-storage-abstraction.md) owns it. Due milestone 6.

From ADR 0003, and still unresolved:

- **0003 open question 1** — `Varve.Shacl` and the SPARQL evaluator are both
  placed in layer 3, yet SHACL-SPARQL depends on the evaluator. Due milestone 8.
- **0003 open question 2** — the SPARQL optimiser and evaluator are both in
  layer 3. If the evaluator consumes a plan type the optimiser owns, they are one
  package or two layers. Due milestone 5.

## Obligations recorded against a later milestone

- **ADR 0004** — the `System.Uri` ban is wider than the brief scopes it. Narrow
  before layer 5 exists.
- **ADR 0007 / 0009** — `dotNetRdf.Core` is a temporary test-only dependency.
  Remove when `Varve.Turtle` passes `rdf/rdf11/rdf-turtle`, at milestone 5.
- **ADR 0011** — banned-symbols entry for ambient clock and randomness under
  `Varve.Store`, which §10's determinism test depends on. Due milestone 4.
- **ADR 0014** — the storage format must carry a version discriminator from the
  start, so a change to what is hashed is a migration rather than a corruption.

## Closed

- **ADR 0002** — the copyright holder. Closed 2026-09-21: Emil Okkels Klein,
  named in `NOTICE`. The `LICENSE` appendix stays unedited, because it is the
  per-file boilerplate template and not a record of ownership.
- **ADR 0004** — the known limit that `VARVE0002`'s analyzer exemption was by
  name and could not be closed. Withdrawn 2026-09-21: it was wrong on both
  counts, and the rule now checks `IsPackable` and refuses the analyzer as a
  library reference.
