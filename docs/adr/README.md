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
| [0012](0012-term-dictionary-and-id-scheme.md) | Term dictionary, id classes, id scheme, blank node identity | Accepted |
| [0013](0013-records-commits-and-bulk-load.md) | Records versus commits, and bulk load | Accepted |
| [0014](0014-header-chain-and-divergence.md) | Header chain and divergence detection | Accepted |
| [0015](0015-checkpoints-and-reads.md) | Checkpoints, pinned reads, as-of reads, archive horizon | Accepted |
| [0016](0016-projection-contract-and-subscriptions.md) | Projection contract, synchronous default projection, erasure in projections | Accepted |
| [0017](0017-validator-contract-and-overlay.md) | Pre-commit validator contract and the overlay quad source | Accepted |
| [0018](0018-storage-abstraction.md) | Storage abstraction: memory, file, browser | Accepted |
| [0019](0019-erasure-by-crypto-shredding.md) | Erasure by crypto-shredding | **Superseded by 0023** |
| [0020](0020-cipher-for-erasure-mode.md) | Cipher for erasure mode | Accepted, conditionally |
| [0021](0021-dataset-settings-as-a-commit-kind.md) | Dataset settings as a commit kind | Accepted |
| [0022](0022-quad-source-term-handle.md) | The quad source contract over an opaque term handle | Accepted |
| [0023](0023-erasure-and-access-requests.md) | Erasure by crypto-shredding, and access requests | Accepted |

**No ADR in this repository is `Proposed`.** Three were, and were completed in
place rather than superseded, because ADR 0001's no-edit rule binds accepted
decisions and they had never been accepted.

### Accepted ahead of the evidence

Four ADRs carry a **revisit condition**: a stated fact which, if it turns out to
be true, supersedes the ADR. It does not edit it.

| ADR | Condition | Due |
|---|---|---|
| [0012](0012-term-dictionary-and-id-scheme.md) | Milestone 4 benchmarks of index size and scan throughput contradict 64-bit counter-allocated ids. No bytes are frozen before milestone 6. | milestone 4 |
| [0018](0018-storage-abstraction.md) | The in-memory and browser backends cannot both implement the contract without leaking backend detail. Members fixed when the first backend is written. | milestone 4 |
| [0020](0020-cipher-for-erasure-mode.md) | AES-CBC, HMAC-SHA-256 and HKDF do not run on browser WASM when verified on a real build. **This is its acceptance condition.** | milestone 3a |
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
| **Q5** | Lookup by private value — scan and decrypt, or a keyed blind index | [0020](0020-cipher-for-erasure-mode.md) | milestone 9 |
| **Q6** | Cipher and availability per host | [0020](0020-cipher-for-erasure-mode.md) | **decided**; browser half verified at milestone 3a |
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
