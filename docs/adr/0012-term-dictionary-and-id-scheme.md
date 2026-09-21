# 0012 — Term dictionary, id classes, id scheme, blank node identity

## Status

**Proposed.** 2026-09-21.

The specification fixes the *shape* — three id classes, canonical injectivity,
blank ids as their own identity, private ids random and not interned
([spec](../spec/log-and-projection-model.md) §1, I3). It does not choose the
**scheme**: how canonical ids are allocated, how wide they are, whether small
values are encoded inline, or how the class is carried. This ADR brings the
options and does not pick.

## Context

The dictionary is the one component every other decision touches. Quads are
`(s, p, o, g) ∈ TermId⁴`; index scans are over ids; the log's `alloc` map is the
only place a term's bytes appear; and a checkpoint materialises the dictionary
alongside the quads. Getting the id scheme wrong is not a localised mistake.

Three things narrow the question more than they first appear.

**Only canonical ids are in question.** A blank id *is* its own identity — there
is no content to derive it from. A private id **must** be random: deriving it
from the term's content would make it a deterministic function of the plaintext,
which is precisely the equality leak crypto-shredding exists to prevent (ADR
0019, and Q5). So the scheme below is a scheme for canonical ids; the other two
classes are already decided by what they are for.

**A later log-merge tool is the sharpest discriminator.** The brief says
branching and merging of datasets is a later possibility that milestone 2 must
not block, and §2 of the specification says the log is never rewritten. Those
two together are unusually demanding on the id scheme, and the options differ
enormously on it. It is the axis this ADR weights most heavily, because it is
the one where a wrong choice cannot be corrected later without rewriting logs.

**Determinism is a stated property** (§2, §10): the same request sequence on two
machines must yield byte-identical `log/` directories. Any allocation scheme
that depends on wall-clock time, machine identity or hash-table iteration order
is out before the comparison starts.

## Options

### A — How canonical ids are allocated

**A1. Counter-allocated.** A monotone counter; the next unseen term takes the
next id, assigned inside the sequencer.

- Compact: ids are dense, so they pack well and a 64-bit width is generous.
- Fast: allocation is an increment; interning is one lookup in a hash map.
- Ordered by first appearance, which gives some locality for terms introduced
  together — a weak benefit, since RDF has no reason for co-introduced terms to
  be co-queried.
- **Merge cost: severe.** Two logs grown independently assign different ids to
  the same term and the same id to different terms. Merging requires remapping
  one side's entire id space, which rewrites every quad in every commit on that
  side — and §2 says the log is never rewritten. A merge would therefore have
  to produce a *new* log rather than join two, losing the original positions and
  every acceptance of them. This is the option that, if chosen, should be chosen
  in full knowledge that it makes merge a re-derivation rather than a join.

**A2. Content-derived.** The id is a truncated cryptographic hash of the term's
canonical encoding (kind, datatype, language, lexical form).

- **Merge cost: near zero.** The same term has the same id in every log, for
  ever, on every machine. Merging two logs is concatenation plus a union of the
  `alloc` maps, with no remapping and no rewriting. Branching becomes a question
  about quads rather than about identifiers.
- Deterministic by construction, which I3's injectivity and §10's determinism
  test both want.
- Costs width: collision resistance over a multi-billion-term dictionary needs
  more than 64 bits. A birthday bound of roughly 2⁶⁴ terms for a 128-bit id is
  comfortable; 96 bits is arguable; 64 is not.
- Costs locality: ids are uniformly distributed, so terms introduced together
  are scattered, and an index ordered by id has no relationship to anything a
  query cares about. Whether that matters depends on whether the index orders by
  id or by term — which is a milestone 6 question, not this one.
- A collision is a correctness failure, not a performance one: two terms sharing
  an id breaks I3's injectivity silently. The scheme needs a stated response
  (detect at allocation and refuse, or widen and never detect).

**A3. Hybrid.** Content-derived for canonical, counter-allocated for blank,
random for private — with the class distinguishing them.

- Gets A2's merge behaviour where it matters and A1's compactness where content
  derivation is impossible anyway.
- Costs a non-uniform id space: ids no longer have one meaning, and any code
  that reasons about an id has to know its class first. Given that the class
  must be carried anyway (option D), this may be no extra cost at all.

### B — Id width

| | Merge | Space per quad | Collision headroom |
|---|---|---|---|
| **B1. 64-bit** | Fine for A1, unsafe for A2 | 32 bytes | ~2³² terms before birthday risk — too low for content-derived |
| **B2. 128-bit** | Fine for either | 64 bytes | ~2⁶⁴ terms — comfortable |
| **B3. Variable-length** | Fine for either | 8–40 bytes typical | Depends on the encoded value |

B3 deserves more than its row. A varint id is small for the common case and
wide when it needs to be, which suits a dictionary whose ids are mostly small
under A1 and mostly large under A2. It costs fixed-width addressing: an index
page of varint ids cannot be binary-searched without an offset table, and a
`ref struct` scan over them cannot stride. Constraint 5 — allocation per quad is
a defect — pushes toward fixed width for the hot path, which argues for fixed
width in memory and varint on disk, at the cost of a conversion at every
boundary.

### C — Inline encoding of small values

**C1. None.** Every term is a dictionary entry. Simple, uniform, and the
dictionary grows with every distinct integer and date in the data.

**C2. Tagged inline.** Reserve part of the id space so that small integers,
booleans, short dates and short strings *are* their own id, with no dictionary
entry at all.

- Removes the most common cause of dictionary growth. Real datasets are full of
  distinct numeric literals that are never looked up by value.
- **Merges free**, by construction: an inline value is content-derived, so it is
  the same id everywhere.
- Costs a tag, which eats id space, and costs a branch on every id
  interpretation — which is the hot path constraint 5 is about.
- Costs a decision about exactly which datatypes qualify, and that decision is
  hard to change later because it changes the id of every affected term.

### D — How the class is carried

**D1. Tag bits in the id.** Two bits say canonical, blank or private. Cheapest
to read. Costs id space and composes awkwardly with C2's tag.

**D2. Disjoint ranges.** Each class owns a region. No bit-twiddling; a range
check answers the question. Works naturally with A1, poorly with A2, whose ids
are uniform by construction.

**D3. A side table.** The dictionary itself says which class an entry is. No id
space cost; a lookup where the other two need none. Bad for the hot path, where
the reader wants to know "is this private" before deciding whether to decrypt.

## What is not in question

Fixed by the specification, recorded here so that a later reader does not
reopen them as part of choosing a scheme:

- **`D` restricted to canonical ids is injective.** One id per term.
- **Blank ids are their own identity.** Two blank nodes are equal iff their ids
  are. Blank node labels in a request are request-scoped: each distinct label
  maps to one fresh id, and an existing blank node is addressed by its store
  identity.
- **Private ids are random and not interned.** Two occurrences of the same term,
  under the same key or different keys, may have different ids. Private entries
  exist only in datasets with erasure mode on; when it is off, the class is
  reserved and costs nothing.
- **Ids in `meta` are ids.** The commit's agent, and any other value that can
  identify a person, is a `TermId` and never an inline string — so that it can
  be a private term.

## Consequences

**A tension worth naming now, because the scheme does not resolve it.** The
quad source contract lives at layer 1 and is expressed over `Varve.Rdf` terms,
not over `TermId`s: `TermId` is an encoding detail of `Varve.Store` at layer 4
and cannot appear in a layer 1 contract without inverting the layering. But
index scans want ids, and a scan that only needs to count or join has no use for
materialised terms. Either the contract materialises and some queries pay for
terms they never look at, or something opaque crosses the boundary and layer 1
learns a store concept. This is not a dictionary question, it is a quad-source
question, and it lands on whoever writes the evaluator's access path at
milestone 5. Flagged here because the id scheme is what makes it expensive or
cheap.

**Whatever is chosen, it is chosen for the life of every log ever written.**
There is no migration that does not rewrite logs, and §2 says logs are not
rewritten. This is the strongest argument for weighting merge cost heavily even
though merge is not on the roadmap.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0011). Touches
  **0005** (the dictionary is store-internal and SPARQL-free), **0010** (ids are
  allocated inside the serialised region and discarded when a request produces
  no commit), and **0019/0020** (the private class, and why it cannot be
  content-derived). No conflict with any.
- **Layer ownership.** The dictionary, `TermId` and the id scheme are owned by
  `Varve.Store` at **layer 4** and are not part of any lower-layer contract. The
  terms themselves are `Varve.Rdf`, layer 1.
- **Analyzer rule.** None. The one rule-shaped obligation — no ambient clock or
  randomness in the allocator, so that §10's determinism test can hold — is the
  banned-symbols entry already noted in ADR 0011.
- **Open questions owned.** **Q1**, the external form of store-scoped blank node
  identity at API and protocol boundaries. A skolem IRI in a reserved scheme,
  per RDF 1.1 Concepts §3.5, is the obvious candidate and is what the resolution
  should argue for or against. **Due by milestone 4**, when the store first has
  an API for anything to cross.
