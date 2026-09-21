# 0017 — Pre-commit validator contract and the overlay quad source

## Status

Accepted. 2026-09-21.

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
R4 and T1 steps 5 and 6, and the classification gate of §9.

## Context

`docs/brief.md` lists as a tension: "Commit-time validation cost versus write
latency, and what a pre-commit validator is allowed to read (the pending
transaction overlaid on the pinned position)." It also fixes, in the SHACL
section, that the commit pipeline has a pre-commit validator hook from ADR set
zero even while no validator exists.

The parenthesis in the brief is the answer to half of it. The other half — the
cost — follows from where the hook is: inside the sequencer, so validation is
write latency by construction.

What the brief does not settle, and what this ADR has to, is **where the overlay
lives**. It is used in two places that sit four layers apart: an as-of read
composes a checkpoint with a log tail (R2, layer 4), and a validator sees the
pending delta over the pinned state (T1, layer 4) — but the evaluator at layer 3
has the same need whenever it runs over anything but a raw dataset, and a layer
3 package may not reference layer 4.

## Decision

### The overlay is a layer 1 combinator

```
Overlay(B, (A, R)) = (B \ R) ∪ A
```

as a **quad source**, merged at scan time rather than materialised. It is exact
when `R ⊆ B` and `A ∩ B = ∅`, which ADR 0010's effective-delta invariant
guarantees for every use in the specification.

**It lives in `Varve.Rdf`, layer 1 — with the quad source contract, not in
`Varve.Store`.** *(Amended 2026-09-21: this now rests on a decision that did not
exist when it was written — see the amendment below.)* Everything it needs is already there: a quad source, and a pair
of quad sets over the same terms. It contains no store concept, no position, no
commit. Putting it at layer 4 would put it out of reach of the layer 3
evaluator, and would mean as-of reads and pre-commit validation each grew their
own copy of the same three-line definition — which is how two implementations of
one rule end up disagreeing.

**One implementation serves R2, R3 and pre-commit validation.**

### The pre-commit validator contract

Owned by `Varve.Store` at layer 4. A validator is given:

- the quad source `Overlay(G_head, δ)` — what the dataset *would* look like;
- the delta `δ` itself — what is changing.

It returns **accept**, optionally with an attachment, or **reject** with a
report. On reject the request returns `Rejected(report)` and leaves no trace:
no commit, no dictionary allocation, no position consumed (ADR 0010).

**That pair is the whole of what a validator may read.** Not an arbitrary
position, not another dataset, not the network. A decision about whether *this*
commit may land can rest on the state it would produce and on what it changes,
and on nothing else — which is what makes the decision reproducible when the
commit is later replayed or audited.

**The contract is free of SPARQL and SHACL**, per ADR 0005. A SHACL-backed
validator, or one driven by a query, is a **layer 5** integration that
implements this contract — the same pattern 0005 applies to SPARQL Update over
the store.

### An attachment is metadata, not a second delta

Accepting with an attachment is how a validation report is carried on the commit
(`meta` in §1). A validator changes nothing about the data; it decides.

### The classification gate is policy, not mechanism

§9 says that in erasure mode a pre-commit validator *should* reject literals
under properties no shape has classified as either private or not-personal,
because unclassified personal data that reaches the log can never be erased.
That is a **validator policy in the integration layer**. The store provides the
hook and takes no view. It is recorded here so that the hook's existence is not
mistaken for the store having an opinion about personal data.

### Amendment, 2026-09-21 — the dependency this placement rests on

Putting the overlay at layer 1 was argued above from what it needs: "a quad
source, and a pair of quad sets over the same terms". **That argument is only
sound because the quad source contract deals in an opaque term handle defined in
`Varve.Rdf` itself** ([ADR 0022](0022-quad-source-term-handle.md)). Had the
contract been expressed over the store's `TermId`, the overlay would have needed
a layer 4 type and could not have lived at layer 1 at all; had it been generic
over the handle type, the overlay would have been generic too, and the evaluator
would have paid for it in code size.

The decision is unchanged. What changes is that it now has a named dependency
instead of an implicit assumption, and a superseding change to ADR 0022 is a
change to this one.

## Alternatives considered

- **Overlay in `Varve.Store`.** The obvious home, since the store is what
  composes checkpoints with tails. Rejected by ADR 0003: layer 3 could not use
  it, so the evaluator would need its own, and R2 and validation would drift
  apart. The overlay is a fact about quad sources, not about logs.
- **A separate package for the overlay**, at layer 1 or 2. Keeps `Varve.Rdf`
  strictly a model. Rejected by principle 3 in the other direction: a package
  for one combinator that changes only when the quad source contract changes is
  too thin to earn its own release, and the brief's warning about grab-bag
  packages cuts both ways.
- **Materialise the overlay** instead of merging at scan time. Much simpler and
  faster for small deltas. Rejected: the overlay of a multi-record bulk commit
  does not fit in memory, which is **Q3**. Scan-time merging is what makes
  spilling an implementation choice rather than a redesign.
- **Let a validator read any position, or any dataset.** Would allow
  cross-dataset referential checks, which people will ask for. Rejected: it
  makes the decision depend on state the commit does not name, so the same
  commit can validate today and not tomorrow, and a replay cannot reproduce the
  verdict. A cross-dataset check belongs above the store, before the commit is
  submitted.
- **Validate after commit and compensate.** Keeps the write path fast, which is
  the brief's stated tension. Rejected: the invalid state was real, was visible
  to every reader in between, and "reject" becomes a compensating commit that
  cannot un-say what the log said. For a validator whose cost genuinely cannot
  be paid at write time, the right shape is the incremental validation
  *projection* (§9, milestone 8) — asynchronous by design, and honest about
  being after the fact.
- **A validator that may rewrite the delta.** Tempting for normalisation. Rejected:
  it would make the committed delta differ from the decided one, so an accept
  would no longer be a statement about the thing that landed.

## Consequences

**Validation is write latency.** The hook is inside the sequencer, so every
validator's cost is added to every commit, and there is one sequencer per
dataset (ADR 0011). That is the design and it is the answer to the brief's
tension: cheap structural checks at commit time, anything expensive as an
asynchronous projection.

**`Varve.Rdf` gains a type that is not part of the RDF model.** Worth being
explicit, because principle 3 invites the objection. The overlay is part of the
*quad source contract's* vocabulary rather than of the term model — it is what
the contract composes with — and it changes exactly when that contract changes.
That is one reason to change, which is the test.

**One implementation means one bug.** A defect in the overlay shows up in as-of
reads, diff and validation at once. That is the argument for it, and it is also
why §10 tests it from two directions: as-of via overlay equals as-of via full
replay, and `Overlay(G_{P₁}, Diff(P₁, P₂)) = G_{P₂}`.

**The hook exists before any validator does.** Nothing implements this contract
until milestone 8. Building the seam now is the brief's instruction, and the
cost of getting it wrong is low precisely because there is no implementation to
migrate — but the shape is fixed from here, and widening what a validator may
read later would be a superseding ADR.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0016). Touches
  **0003** (the overlay's placement is decided by the layering rule, and this is
  the first contract in the set to be pushed *down* by it), **0005** (the
  validator contract is the pre-commit hook 0005 anticipated, and is
  SPARQL-free), **0010** (exactness of the overlay depends on I2), and **0015**
  (R2 is built from this). No conflict with any.
- **Layer ownership.** The **overlay quad source is `Varve.Rdf`, layer 1** — and
  explicitly not `Varve.Store`. The **pre-commit validator contract** is
  `Varve.Store`, **layer 4**. Validator *implementations* driven by SHACL or
  SPARQL are **layer 5**.
- **Analyzer rule.** None new. The reserved **VARVE0007** — public contracts
  between packages use `Varve.Rdf` types or BCL primitives only — is the rule
  that will police the validator contract's signature, and it is exactly the
  shape that tends to acquire a store type by accident.
- **Open questions owned.** None. **Q3** — the overlay of a multi-record commit
  not fitting in memory — is owned by **ADR 0013** and cross-references this
  decision, because the answer is either "validators are disabled for bulk
  commits" (a store policy) or "the overlay spills" (an overlay implementation
  concern).
