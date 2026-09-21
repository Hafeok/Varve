# Log and projection model

Functional specification, draft 2. Target location: `docs/spec/log-and-projection-model.md`.

Status: all sections reflect positions agreed in design discussion. Section 12 lists the ADRs this specification presupposes; none of them exist yet.

Changes from draft 1: erasure is crypto-shredding, opt-in per dataset, and no longer rewrites the log (section 9). The dataset directory is assumed to be copied to places the store cannot reach (section 2). Commit headers are hash-chained for divergence detection (I6). The dictionary gains a private term class (section 1, I3).

Scope: the abstract state machine of `Varve.Store`. No byte-level format, no API shape, no SPARQL. Everything here is stated over dictionary-encoded quads and must hold for every storage backend (memory, file, browser).

## 1. Domains

- **Term**: an RDF 1.2 term (IRI, blank node, literal, triple term).
- **TermId**: an opaque fixed-width identifier with a class: *canonical*, *blank* or *private*.
- **Dictionary** `D`: a partial map from `TermId` to an entry.
  - A canonical entry is a term. `D` restricted to canonical ids is injective: one id per term.
  - A blank id is its own identity. Two blank nodes are equal iff their ids are equal.
  - A private entry is `(KeyId, ciphertext)`, where the ciphertext covers the whole term encoding (kind, datatype, language, lexical form). Private ids are random and not interned: two occurrences of the same term under different keys, or under the same key, may have different ids. Private entries exist only in datasets with erasure mode on.
- **KeyId**: a random identifier of a data-subject key. It identifies nobody by itself. The mapping from data subject to `KeyId`, and the key material, live in the key store (section 9), never in the dataset.
- **Quad**: `(s, p, o, g) ∈ TermId⁴`, where `g` may be the reserved default-graph id.
- **Position**: `P ∈ ℕ`. Position 0 is the empty dataset. Commit positions start at 1 and are dense.
- **Delta**: `δ = (A, R)` with `A, R ⊆ Quad` and `A ∩ R = ∅`.
- **Commit**: `c = (header, alloc, A, R)` with `header = (pos, kind, meta, prev, content)`.
  - `kind ∈ {Data, Erasure}`.
  - `meta`: timestamp, agent, cause, optional named-graph scope, optional attachments (for example a validation report reference). Agent and any other value that can identify a person is a `TermId`, never an inline string, so that it can be a private term.
  - `prev`: hash of the previous commit's header. `content`: hash of `(alloc, A, R)`.
  - `alloc`: a finite map of fresh `TermId → entry`.
- **Log** `L = c₁ … cₙ`. `head(L) = n`.
- **Record**: the physical unit of append. A commit is one or more records; the last carries a closing flag. The **readable head** is the position of the last closed commit. Records of an unclosed commit are invisible to every read, feed and projection. On recovery an unclosed tail is discarded.

## 2. Environment assumption

The dataset directory of the file backend consists of plain files. It will be copied, backed up, synchronised and checked into version control by people and tools the store knows nothing about. The store cannot reach those copies. Consequences that bind the design:

- The log is never rewritten. No feature may depend on removing bytes from it.
- Nothing that must stay secret or must be deletable is ever written inside the dataset directory. Keys live elsewhere, in the way a signing key is never checked into the repository it signs.
- The directory separates the source of truth (`log/`) from derived data (`derived/`: checkpoints, indexes, projection state). Derived data is reproducible and excluded from version control by an ignore file the store writes at creation.
- Sealed log segments are immutable. Only the active segment changes.
- Bytes are deterministic: the same log yields the same files on every machine.
- Default segment size stays below common hosting limits for single files.

Byte-level layout belongs to the storage ADR. The points above are requirements on it.

## 3. State

The dataset state at position `P` is `S(P) = (D_P, G_P)`:

```
D_0 = ∅                      D_P = D_{P-1} ∪ alloc_P
G_0 = ∅                      G_P = (G_{P-1} \ R_P) ∪ A_P
```

`S(P)` is a pure function of `L[1..P]`. Nothing else is a source of truth. Whether a private entry is readable is a function of the key store, not of `S(P)`.

## 4. Log invariants

- **I1 Dense positions.** `pos(cᵢ) = i`.
- **I2 Effective delta.** `A_P ∩ G_{P-1} = ∅`, `R_P ⊆ G_{P-1}`, `A_P ∩ R_P = ∅`. The log records what changed, not what was requested.
- **I3 Dictionary closure.** Every id in `A_P`, `R_P` and `meta_P` is in `dom(D_P)`. Ids in `alloc_P` are fresh. Every id in `alloc_P` occurs in `A_P` or `meta_P`. Canonical ids are injective over terms; private ids are exempt from injectivity.
- **I4 Non-empty.** A `Data` commit has `A_P ∪ R_P ≠ ∅`.
- **I5 Monotone time.** `ts(c_P) ≥ ts(c_{P-1})`. The sequencer assigns `max(clock, ts(head))`. As-of by timestamp `t` resolves to the greatest `P` with `ts(c_P) ≤ t`.
- **I6 Header chain.** `prev(c_P) = hash(header(c_{P-1}))`, with a fixed value for `P = 1`. Two logs with a common prefix and different continuations are detectably divergent. A store that opens a log whose chain does not verify, or is asked to continue from a head that is not its own, refuses.

## 5. Transitions

### T1 Commit

Input: an ordered list of assert and retract operations over terms, metadata, an optional expected position, zero or more pre-commit validators, and in erasure mode a classifier (section 9).

There is one sequencer per dataset. It processes requests one at a time:

1. If an expected position is given and differs from the readable head: reject with `Conflict(head)`. No state change.
2. Resolve terms to ids. Unknown terms get provisional fresh ids. Blank node labels in the request are scoped to the request: each distinct label maps to one fresh id. An existing blank node is addressed by its store identity (Q1). In erasure mode the classifier assigns term occurrences to `KeyId`s; those occurrences get private ids and encrypted entries.
3. Apply the operations in order to an overlay on `G_head` and take the net result as `δ`. Asserting a present quad, retracting an absent quad, and assert-then-retract of the same absent quad all contribute nothing. This matches SPARQL 1.1 Update 3.1.1 and 3.1.2, where both are without effect. For private terms, "present" is decided by value under the same key, not by id.
4. If `δ` is empty: return `NoChange(head)`. No commit, provisional ids discarded.
5. Run validators against `Overlay(G_head, δ)` and `δ` (R4). A validator may reject, or accept with an attachment.
6. On reject: return `Rejected(report)`. No commit, provisional ids discarded.
7. On accept: append the records, make them durable, close the commit. The readable head advances to `head + 1`. Return `Committed(head + 1)`.

A rejected or empty request leaves no trace in the log or the dictionary.

### T2 Checkpoint

`K_P` is a materialisation of `S(P)` as immutable, directly queryable sorted runs, including the dictionary. It is derived data, not a log entry. Private entries are stored in checkpoints as ciphertext, exactly as in the log.

- **I7 Checkpoint equivalence.** `K_P = fold(L[1..P])`.

Checkpoints may be created at any closed position, by any policy, in the background. Dropping a checkpoint loses nothing. The log before a checkpoint is retained.

### T3 Archive (later)

Moves `L[1..H]` and checkpoints below `H` to cold storage. Constraint fixed now: an as-of read or diff below `H` without the archive attached fails with an explicit error. It never returns a partial answer.

### T4 Erase

See section 9. Erasure appends one commit and destroys one key. It changes no existing byte of the log.

## 6. Reads

- **R1 Pinned read.** `Pin()` returns a quad source over `G_P` where `P` is the readable head at the time of the call. The view is stable until released, regardless of later commits. This is an engine-level snapshot with the lifetime of one operation (a query, an update's WHERE evaluation, a validation run). It is not time travel.
- **R2 As-of read.** For any closed `P` at or above the archive horizon: a quad source over `G_P`, served as `Overlay(K_Q, net(L(Q..P]))` for the greatest checkpoint `Q ≤ P` (or `K_0 = ∅`). Cost is proportional to the log distance `P − Q`, not to `|G_P|`.
- **R3 Diff.** `Diff(P₁, P₂) = net(L(P₁..P₂]) = (G_{P₂} \ G_{P₁}, G_{P₁} \ G_{P₂})`. Computed from the log alone.
- **R4 Overlay.** `Overlay(B, (A, R)) = (B \ R) ∪ A` as a quad source, merged at scan time. It is exact when `R ⊆ B` and `A ∩ B = ∅`, which I2 guarantees for every use above. One implementation serves R2 and pre-commit validation. It is defined over the quad source contract and knows nothing about the store.

Delta composition, used by R2 and R3:

```
(A₁, R₁) ; (A₂, R₂) = ((A₁ \ R₂) ∪ (A₂ \ R₁), (R₁ \ A₂) ∪ (R₂ \ A₁))
```

Deltas under `;` form a monoid with identity `(∅, ∅)`. `net(L(P..Q])` is the composition of the commits' deltas in order.

Term equality seen by readers: canonical and blank ids compare by id. A readable private term compares by decrypted value, against private and canonical terms alike. A shredded private term is equal only to itself.

## 7. Projections

A projection `π` is a state machine `(state, pos)` with `apply(commit)`.

- It receives closed commits in position order.
- Delivery is at-least-once. `apply` of a commit with `pos ≤ pos(π)` is a no-op, which makes the effect exactly-once.
- It persists `pos(π)` atomically with its state.
- It may be dropped and rebuilt from position 0 or from a checkpoint it knows how to read.
- **I8 Rebuild equivalence.** For every `P`, a projection rebuilt by replay to `P` is observationally equal to one maintained incrementally to `P`.
- A projection that holds plaintext derived from private terms (a full-text index, for example) records the `KeyId` with each such entry and purges those entries when it applies an `Erasure` commit for that key. Such state lives under `derived/` and nowhere else.

The default quad projection is updated synchronously in T1 step 7, so that `Pin()` directly after `Committed(P)` observes `P`. All other projections are asynchronous and may lag: `pos(π) ≤ readable head`.

## 8. Subscriptions

`Subscribe(from: P, filter)` delivers closed commits with position `> P`, in order, at-least-once. The consumer owns its position. A filter (graph or quad pattern) restricts each delivered delta; commits whose filtered delta is empty are skipped, and the next delivered commit carries its true position, so resumption is unaffected. `Erasure` commits are always delivered, regardless of filter.

## 9. Erasure by crypto-shredding

Erasure mode is a per-dataset setting, off by default. When off, no private ids exist, no key store is consulted, and nothing in this section costs anything beyond the reserved id class.

- **Key store.** A contract owned by `Varve.Store`: create a key for a data subject, resolve subject to `KeyId`, fetch key material by `KeyId`, destroy a key. Implementations live outside the dataset directory by construction; the file backend refuses a key store path inside it. The key store is the only mutable, deletable component in the system and needs its own backup policy, which must itself honour destruction.
- **Classifier.** A contract owned by `Varve.Store`, free of SPARQL and SHACL: given the pending operations and the pinned quad source, assign term occurrences to data subjects. Implementations derived from annotated shapes or from queries are layer 5 integrations.
- **Classification gate.** Unclassified personal data that reaches the log can never be erased. In erasure mode, a pre-commit validator should therefore reject literals under properties that no shape has classified as either private or not personal. This is validator policy in the integration layer; the store only provides the hook. It cannot catch personal data inside free text that a shape has declared not personal.
- **Selector.** A function from a quad source and a data subject to a set of quads, contract in `Varve.Store`. Used for access requests, and by T4 to clean the current graph.
- **T4 Erase(subject).**
  1. Optionally commit a normal `Data` commit retracting the selector's result from `G_head`, so the current graph holds no dangling structure.
  2. Append an `Erasure` commit whose metadata carries the `KeyId`, agent and cause. It has an empty delta and is exempt from I4.
  3. Destroy the key in the key store.
  Replicas and subscribers receive the `Erasure` commit through the log and destroy their copy of the key. A party that was given a key and keeps it is outside technical control; towards such parties erasure is a contractual obligation.
- **I9 Plaintext confinement.** At all times, not only after erasure: no file in `log/`, no checkpoint and no record delivered to a filtered subscription contains the plaintext of a private term or any key material. Plaintext derived from private terms exists only in memory and under `derived/`, tagged with its `KeyId`.
- **I10 Erasure soundness.** After `Erase`, every private entry under the destroyed key is unreadable in the log, in every checkpoint, and in every copy of the dataset directory made at any time. I1 to I8 are unaffected, because no log byte changed.
- **What survives.** Structure survives: shredded ids, the predicates between them, and links from shredded nodes to canonical terms (a shredded subject that `worksFor` a public organisation). This is a known limitation. Re-identification from structure is a documented attack on anonymised graphs. Where a link is itself identifying, the classifier can make the object occurrence private as well. Whether the residue counts as anonymous is a legal question.
- **Guarantee for time travel.** As-of reads remain structurally stable forever. Draft 1's "stable modulo erasure" is withdrawn. What changes after erasure is readability of private terms, at every position at once.

## 10. Property tests

| Invariant | Test |
|---|---|
| I2 | For arbitrary request sequences, every committed delta satisfies the three conditions against the model fold. |
| I3 | No id appears before its allocation; no allocation is unreferenced; rejected and empty requests leave the dictionary unchanged. |
| I5 | As-of by timestamp equals as-of by the resolved position. |
| I6 | Any single-byte change to a header breaks verification; two continuations of one prefix are reported as divergent. |
| I7 | Checkpoint at `P` plus log tail to `Q` equals full replay to `Q`. |
| I8 | Rebuilt projection equals incrementally maintained projection at every position. |
| R1 | A pinned source returns identical results before and after arbitrary later commits. |
| R2, R4 | As-of via overlay equals as-of via full replay. |
| R3 | `Overlay(G_{P₁}, Diff(P₁, P₂)) = G_{P₂}`; delta composition is associative. |
| Records | A crash at any record boundary recovers to the last closed commit. |
| Determinism | The same request sequence on two machines yields byte-identical `log/` directories (with injected clock and randomness). |
| I9 | For generated private literals with high-entropy markers, a byte scan of `log/` and checkpoints never finds a marker. |
| I10 | After erasure, reads at every sampled position return the shredded form for every term under the key, and structure is unchanged. |

## 11. Open questions

- **Q1** External form of store-scoped blank node identity at API and protocol boundaries (a skolem IRI scheme per RDF 1.1 Concepts 3.5 is the obvious candidate).
- **Q2** Bulk load and I2: normalising a multi-billion-quad commit needs an index lookup per quad. Loading into an empty dataset is trivial; loading into a populated one needs a stated strategy.
- **Q3** Bulk load and validators: the overlay of a multi-record commit does not fit in memory. Either validators are disabled for bulk commits, or the overlay spills.
- **Q4** How a shredded term appears in SPARQL results and serialisations: unbound, or an opaque IRI in a reserved scheme.
- **Q5** Lookup by private value (`?x foaf:name "Emil"`) cannot use an id. Either scan and decrypt, or a keyed blind index. A blind index is deterministic across subjects and its key is not per subject, so it weakens I10 and needs justification.
- **Q6** Cipher and availability per host. `AesGcm` is, as far as known, unsupported on browser WASM, where AES-CBC and HMAC are available. Verify before the ADR. Determinism (section 2) also requires nonces derived deterministically or injected.
- **Q7** Whether access requests default to `G_head` or to every quad ever asserted. Retained history is still processing; this needs legal input.
- **Q8** Granularity of keys: one per data subject is assumed. Data about two subjects in one term (a joint account label) has no single owner.

## 12. ADRs this specification presupposes

1. Commit model and effective deltas (I2, I4, T1 steps 3 and 4).
2. Concurrency: single sequencer with optional expected position.
3. Term dictionary, id classes (canonical, blank, private), id scheme, blank node identity.
4. Records versus commits; bulk load as a multi-record commit.
5. Header chain and divergence detection (I6).
6. Checkpoints, pinned reads, as-of reads, archive horizon.
7. Projection contract; synchronous default projection; erasure handling in projections.
8. Pre-commit validator contract and the overlay quad source, including which layer owns the overlay.
9. Storage abstraction (memory, file, browser) and the dataset directory requirements of section 2.
10. Erasure by crypto-shredding: key store, classifier and selector contracts, erasure mode, cipher choice.
