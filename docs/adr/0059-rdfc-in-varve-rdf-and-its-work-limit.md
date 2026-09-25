# 0059 — RDFC-1.0 in `Varve.Rdf`, bounded by a work limit; the isomorphism check stays as a cross-check

## Status

**Accepted.** 2026-09-25. Decided by the maintainer on the milestone 5c plan.
**Supersedes the deletion clause of ADR 0030** ("The isomorphism check will
be deleted, not promoted", in its Consequences, and the sentence of its §3
that makes RDFC-1.0 the replacement); the rest of ADR 0030 stands.
Specification: [`docs/spec/rdf-canon.md`](../spec/rdf-canon.md).

## Context

The brief puts RDFC-1.0 in the RDF model (oxrdf's scope), and the roadmap has
carried it since 3a. ADR 0030 decided two things about it in advance:

- that dataset isomorphism does **not** belong in `Varve.Rdf`, because the
  harness's backtracking search "is exponential in an input nobody bounds",
  and a library function with that cost is not an API to hand anyone;
- that when RDFC-1.0 lands the harness switches to canonical labelling and
  **the backtracking check is deleted**, so that there are never two answers
  and a question of which is authoritative.

Canonicalisation has the first problem too. Hash N-Degree Quads (§4.8)
permutes related blank nodes, and its cost is exponential in the worst case —
the suite's poison test, a ten-node clique, is there to show it. The
specification's answer is §4.4.3: "implementations MUST defend against
potential denial-of-service attacks by raising suitable exceptions and
terminating early".

The second decision turned out to throw away something worth keeping. The
two checks are independent implementations of one relation — one a search
over bijections, one a labelling — and a disagreement between them on any
dataset is a defect in one of them that neither alone would show.

## Decision

1. **RDFC-1.0 is public API in `Varve.Rdf`**, over `IQuadSource`, returning
   the canonical N-Quads bytes and the issued identifiers map, with the hash
   algorithm selectable (SHA-256 by default, SHA-384 as §3.1 requires,
   SHA-512).
2. **A work limit bounds it**: the calls to Hash N-Degree Quads, as a
   multiple of the blank nodes that need them, default 12, configurable;
   exceeding it throws `CanonicalisationLimitException`. **This is the answer
   to ADR 0030's objection**: the cost is bounded by a stated number the
   caller can see and change, and an input that would exceed it fails
   explicitly instead of running without end. The default is set from the
   suite's measured maxima, not guessed.
3. **Canonical equality is the harness's comparison** for `CONSTRUCT` and
   `DESCRIBE` results and for update results. **The backtracking check runs
   alongside on every such case and must agree**: when both give a verdict
   and they differ, the case fails and the message names both. Its
   `Inconclusive` — the search ran out of budget — is kept distinct: it is
   not a disagreement, the canonical verdict decides, and the report counts
   such cases. The check stays in `Varve.Conformance.Tests`, test code, as ADR
   0030 placed it.
4. **The property `iso(A, B) ⇔ canon(A) = canon(B)`** over generated datasets
   is the two implementations' differential test, in both directions.
5. **The canonical N-Quads writer is `Varve.Rdf`'s own**, internal, because
   the N-Quads writer is at layer 2. It writes RDFC-1.0 Appendix A's form,
   which is not `n-triples.md` §5's; the difference is recorded in the
   specification and reported, not resolved here.
6. **A triple term with a blank node inside it is refused.** RDFC-1.0 is
   defined over RDF 1.1 datasets, and an extension to RDF 1.2 would be ours,
   not the specification's.

## Alternatives considered

- **Delete the backtracking check**, as ADR 0030 said. One answer, no
  question of authority. Rejected by the maintainer: agreement between two
  independent implementations is evidence neither gives alone, and the
  property that states it costs a few lines. Authority is not in question —
  the canonical verdict is the comparison; the other must agree with it.
- **Keep RDFC-1.0 in the test project**, as ADR 0030 did for isomorphism.
  Rejected: the brief puts canonicalisation in the model, and it has uses
  beyond comparison — signing a dataset, hashing one for a content address —
  which is why it is a Recommendation.
- **No limit, only cancellation.** The caller passes a token and decides.
  Rejected: §4.4.3 says the implementation MUST defend itself, and a host
  that canonicalises data it received is the caller least able to choose a
  timeout. The token is honoured as well.
- **A time limit** instead of a call count. Rejected: not deterministic —
  the same dataset would canonicalise on one machine and fail on a slower
  one — and a timer is ambient state the model does not otherwise need.
- **Extend RDFC-1.0 to blank nodes inside triple terms** by treating them as
  mentioned by the quad. Plausible, and it would be the first to do so;
  rejected until the specification says how, because two implementations that
  extend it differently would each call their output canonical.

## Consequences

- **`Varve.Rdf` gains a type that is an algorithm, not a model type**, and
  its first use of `System.Security.Cryptography`. SHA-256 and SHA-384 are
  both available in the browser (ADR 0020's table; the smoke app now measures
  SHA-384).
- **Two answers exist to one question**, which ADR 0030 wanted to avoid. The
  harness makes their agreement a gate, which is the cost that makes keeping
  both worth it.
- **An RDF 1.2 dataset with a blank node inside a triple term cannot be
  canonicalised**; the harness comparison falls back to the backtracking check
  for such a dataset and says so.

## Checks

- **Checked against the accepted ADRs** (0001–0058) and the specification.
  Touches **0003** (no reference from layer 1 to the N-Quads writer at layer
  2, hence the internal writer), **0007** (the second submodule follows its
  pattern: pinned, guard counts, ratchet), **0022** (the input is the quad
  source contract; blank node identity is the source's comparer), **0024**
  (terms as the model defines them; no value canonicalisation), **0028** and
  **0020** (SHA-2 in the browser), **0030** (superseded in its deletion
  clause) and **0051** (lexical forms are kept, never canonicalised to values).
  No conflict with any beyond the supersession.
- **Layer ownership.** The canonicaliser is `Varve.Rdf`, **layer 1**. The
  cross-check is test code, no layer.
- **Analyzer rule.** None.
- **Open questions owned.** None of the store specification's. The
  canonicalisation specification owns two: triple terms containing blank
  nodes, and `n-triples.md`'s canonical form.
