# 0012 — Term dictionary, id classes, id scheme, blank node identity

## Status

**Accepted.** 2026-09-21.

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
§1 and I3.

This ADR was **Proposed** earlier on 2026-09-21 and brought options without
choosing. It was never accepted, so it is completed in place rather than
superseded (ADR 0001's no-edit rule binds accepted decisions). The options that
lost are kept below, and two pieces of analysis in the Proposed version were
**wrong**; they are corrected under *Corrections* rather than deleted, because
the reasoning that produced them is the part worth being able to find again.

**Revisit condition** (see *Consequences*): milestone 4 benchmarks of index size
and scan throughput contradict the choice. Meeting it supersedes this ADR; it
does not edit it.

## Context

The dictionary is the one component every other decision touches. Quads are
`(s, p, o, g) ∈ TermId⁴`; index scans are over ids; `alloc` is the only place a
term's bytes appear in the log; a checkpoint materialises the dictionary beside
the quads.

Three things narrow the question more than they first appear.

**Only canonical ids were ever in question.** A blank id *is* its own identity —
there is no content to derive one from. A private id must be independent of
content (see *Corrections*). So an allocation scheme is a scheme for canonical
ids; the other classes are decided by what they are for.

**There is exactly one sequencer per dataset** (ADR 0011), and it already
serialises every write. Coordination-free allocation therefore buys nothing
that is not already free.

**I3 forces a dictionary lookup on write either way.** Canonical ids are
injective over terms, so the sequencer must ask "have I seen this term" for
every term in every commit, whatever the id is derived from. A content-derived
id does not avoid that lookup; it only changes what the answer is compared
against.

## Decision

**64-bit `TermId`, with the class carried in the high bits.**

| Class | Allocation | Notes |
|---|---|---|
| **canonical** | counter, by the sequencer | injective over terms (I3) |
| **blank** | counter, by the sequencer | its own identity; equal iff ids are equal |
| **private** | counter, by the sequencer, **in its own class** | independent of content, never interned |
| **inline** | none — the value *is* the id | small values with no dictionary entry |

- **64 bits**, so a quad key is **32 bytes** rather than the 64 that
  collision-resistant content-derived ids would need. That halves every index and
  every checkpoint, which matters most where memory is least negotiable — the
  browser.
- **Counters allocated by the sequencer.** It is already the one place that
  serialises writes and already performs the I3 lookup.
- **Small values encoded inline**, with no dictionary entry: the id *is* the
  value, tagged. Real datasets are full of distinct numeric literals that are
  never looked up by value, and each one would otherwise grow the dictionary
  for ever.
- **The class is a tag in the high bits**, read with a mask. A reader must know
  an id's class before it can decide whether to decrypt it, so the answer has to
  be in the id itself and not behind a lookup.

Which datatypes qualify for inline encoding, and the exact bit layout, are
**milestone 6** decisions. No bytes are frozen before then.

## Corrections to the Proposed version

**Private ids do not need to be random.** They need to be *independent of
content* and *not interned*. A counter in its own class gives both: two
occurrences of the same term get different ids, and the id says nothing about
the plaintext. What it leaks is allocation order — which the log already
records, in the open, as the order of `alloc`. Randomness bought nothing beyond
that and cost something real: the determinism property test in §10 would have
needed an injected random source. With counters, the only injected thing in
erasure mode is the key store.

**The merge analysis was overstated in both directions.** The Proposed version
called counter allocation's merge cost "severe" and content-derived ids'
"near zero". Both were wrong, for the same reason:

- **A merge always re-derives commits.** Two logs that diverged have different
  positions and different header chains (ADR 0014), and the merged delta has to
  be re-normalised against the merged state to satisfy I2 (ADR 0010). So a merge
  is never a concatenation, whatever the ids are. Content-derived ids would not
  have made it one.
- **Counter ids cost a translation table, not a rewrite.** Merging maps one
  side's ids onto the other's while re-deriving the commits that are being
  re-derived anyway. That is a side table proportional to the dictionary, not a
  rewrite of the log — and §2's "the log is never rewritten" was never in
  danger, because the merge writes a new log rather than editing either input.

The Proposed version weighted merge most heavily on the grounds that it was the
axis where a wrong choice could not be corrected. That grounds was itself the
error.

## Alternatives considered

- **Content-derived 128-bit ids** — a truncated hash of the canonical term
  encoding. Lost on space: 64-byte quad keys double every index and every
  checkpoint, and the merge advantage that was supposed to pay for it does not
  exist (above). It also has a failure mode nothing else here has: a collision
  breaks I3's injectivity *silently*, so the scheme would need a stated
  detect-and-refuse step on every allocation — which is the lookup counters
  perform anyway. And uniformly distributed ids destroy any relationship between
  id order and anything a query cares about.
- **Hybrid** — content-derived canonical, counter blank, random private. Lost
  with its content-derived half, and it bought a non-uniform id space whose only
  justification was that half.
- **Variable-length ids.** Small under counters, so attractive. Lost on the hot
  path: a page of varint ids cannot be binary-searched without an offset table
  and a scan cannot stride, which is exactly what constraint 5 is about. If the
  on-disk format wants varints at milestone 6, that is a compression decision
  behind a fixed-width in-memory representation, not an id scheme.
- **A side table for the class**, instead of tag bits. No id-space cost, one
  lookup where tag bits need none — on the path where a reader is deciding
  whether a term needs decrypting.

## What the specification fixes, not this ADR

Recorded so that a later reader does not reopen them while revisiting the scheme:

- `D` restricted to canonical ids is injective. One id per term.
- Blank ids are their own identity; request labels are request-scoped, and an
  existing blank node is addressed by its store identity (Q1).
- Private entries are `(KeyId, ciphertext)` covering the whole term encoding,
  and exist only with erasure mode on. With it off the class is reserved and
  costs nothing.
- Every id in `meta` is an id — the agent is a `TermId`, never an inline string,
  so that it can be a private term.

## Consequences

**This ADR does not bind milestone 3.** `Varve.Rdf` defines terms; ids are the
store's business. Nothing in the RDF model, the parsers or the serialisers sees
a `TermId`, and milestone 3 can be built and shipped without this decision being
right.

**Revisit condition, stated as the ADR's own falsifier.** Milestone 4 benchmarks
of index size and scan throughput are what would contradict this. If they do,
this ADR is superseded — not edited. **The locality and compression arguments
above are hypotheses until those benchmarks exist**, and should be read as
hypotheses: 32-byte keys are certainly half of 64-byte keys, but whether that
converts into scan throughput depends on a layout nobody has written yet.

**No bytes are frozen before milestone 6.** The in-memory representation is
64 bits from milestone 4; the on-disk encoding, the inline datatype set and the
exact tag layout are the durable format's to decide, and the format carries a
version discriminator from its first byte (ADR 0014).

**Inline encoding is the hardest part to change later**, because it changes the
id of every affected term. It is also the one with the clearest payoff. The
qualifying set should start small — the values that are provably never looked up
by value — and grow with evidence.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0011, 0013–0018, 0020–0023).
  Touches **0010** (ids are allocated inside the serialised region and discarded
  when a request produces no commit), **0011** (the sequencer is the allocator,
  which is what makes counters free), **0023** and **0020** (the private class,
  and why independence from content is the requirement), and **0022** (the quad
  source contract's opaque 64-bit handle is the same width, deliberately). No
  conflict with any.
- **Layer ownership.** The dictionary, `TermId` and the id scheme belong to
  `Varve.Store` at **layer 4**. Terms are `Varve.Rdf`, layer 1. The *handle* that
  crosses the quad source contract is layer 1 and opaque (ADR 0022); that it is
  also 64 bits is a deliberate alignment, not a leak of the store's scheme.
- **Analyzer rule.** None. The one rule-shaped obligation — no ambient clock or
  randomness in the allocator, which §10's determinism test needs — is the
  banned-symbols entry recorded in ADR 0011, and counter allocation makes it
  easier to keep, not harder.
- **Open questions owned.** **Q1**, the external form of store-scoped blank node
  identity at API and protocol boundaries. A skolem IRI in a reserved scheme
  (RDF 1.1 Concepts §3.5) is the candidate to argue for or against. **Due by
  milestone 4.**
