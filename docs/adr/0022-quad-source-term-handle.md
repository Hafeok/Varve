# 0022 — The quad source contract over an opaque term handle

## Status

**Accepted.** 2026-09-21.

Records [`docs/spec/log-and-projection-model.md`](../spec/log-and-projection-model.md)
§6, which states that equality is a property of the quad source and not of the
id.

**Revisit condition** (see *Consequences*): milestone 5 evaluator benchmarks
show the opaque handle costs more than it saves. Meeting it supersedes this ADR.

## Context

The quad source contract is the most load-bearing type in the design. The
evaluator runs over it (ADR 0005), as-of reads and pinned reads return it (ADR
0015), the overlay is defined over it (ADR 0017), and it lives at **layer 1** in
`Varve.Rdf` so that layer 3 can use it without knowing there is a store.

The question this ADR answers is what a quad *contains*. Everything in the
specification is stated over dictionary-encoded quads — `(s, p, o, g) ∈ TermId⁴`
— but `TermId` belongs to `Varve.Store` at layer 4 and cannot appear in a layer
1 contract without inverting the layering. ADR 0012 flagged exactly this and
left it open: either the contract materialises terms and some queries pay for
terms they never look at, or something opaque crosses the boundary.

ADR 0012 has since settled on **64-bit** ids, which changes the calculation.

## Decision

**The quad source contract works over an opaque 64-bit term handle defined in
`Varve.Rdf`**, with internalise, externalise, and an **equality comparer
supplied by the source**.

- **`TermHandle` is opaque and fixed at 64 bits.** It carries no meaning at
  layer 1: a store's handle is its `TermId`, an in-memory dataset's handle is an
  index into its own interning table, and neither is the other's business.
- **`TryInternalise(term) → handle`** turns a `Varve.Rdf` term into this
  source's handle. **`TryExternalise(handle) → term`** goes back, and returns
  false for a shredded private term.
- **Equality comes from the source**, not from the handle. §6: "a source
  supplies the comparison for the term handles it hands out". Canonical and
  blank ids compare by id; **a readable private term compares by decrypted
  value**, against private and canonical terms alike; a shredded one is equal
  only to itself. No consumer can get that right from the bits, and none has to.
- **An in-memory dataset without a store brings its own interning table.** It is
  a quad source like any other, and nothing about the contract presumes a log.

### Why fixed, and not generic

With ADR 0012 at 64 bits, a contract generic over the handle type buys nothing
and costs two things:

- **Code size.** A generic contract makes the evaluator generic, so it is
  compiled once per handle type instead of once. Under Native AOT every
  instantiation is real code in the binary, and constraint 3 puts that binary in
  a browser, where it is downloaded.
- **Generic virtual methods**, which a generic contract invites and which are
  the one construct that reliably fights AOT — they cannot always be resolved
  statically, and the fallback is exactly the runtime machinery constraint 2
  forbids.

A fixed 64-bit handle has neither problem. There is one evaluator, one overlay
and one set of call sites, and nothing about them is instantiated per source.

## Alternatives considered

- **A term-based contract** — quads of `RdfTerm`, no handles. Simplest by far,
  and the contract would have nothing opaque in it. Lost on cost in the place it
  matters most: a join materialises both sides, so every probe allocates a term
  and every comparison walks its bytes, on the hottest path in the system. It
  also makes the store externalise every term it touches, including the ones a
  query only counts. The materialising path still exists — `TryExternalise` is
  it — but as an opt-in rather than as the contract.
- **A contract generic over the handle type**, `IQuadSource<THandle>`. Type-safe,
  and it would let a store use a wider id later. Lost on code size and the
  generic-virtual risk above. It also gives up something subtle: a single
  non-generic contract means two sources of *different kinds* — a store and an
  in-memory dataset — are the same type, which is what lets the overlay compose
  them at all.
- **A handle wider than 64 bits**, sized for a future id scheme. Lost with ADR
  0012's own argument: 64-bit handles make a quad 32 bytes and 128-bit handles
  make it 64, and the doubling lands on every index, every checkpoint and every
  cache line. If ADR 0012 is ever superseded to a wider id, this ADR is
  superseded with it — which is the honest coupling rather than a hidden one.
- **Equality defined by the handle** — handles compare as integers, full stop.
  Cheapest possible comparison. Lost outright to §6: a private term compares by
  decrypted value, so identical values under one key can have different handles
  (ADR 0012's "not interned"), and an integer comparison would report them
  unequal. Getting this wrong would make erasure mode quietly return wrong query
  results rather than fail.

## Consequences

**`TermHandle` being 64 bits, like `TermId`, is deliberate alignment and not a
leak.** A store can use its `TermId` as its handle with no translation. But the
contract does not say so, and an in-memory dataset's handle means something
entirely different. If ADR 0012 changes the width, this changes with it.

**Every consumer must use `TermComparer`** and must not compare handles with
`==`. This is the one way to misuse the contract and get plausible wrong
answers rather than an error, and it is a natural candidate for an analyzer rule
once there is code to check — not reserved now, because inventing an id before
there is a rule to attach to it is what ADR 0004 warns against.

**Externalisation is where the cost could land.** A numeric `FILTER` that has to
externalise every candidate term pays the allocation the handle was meant to
avoid. **That is the revisit condition**: if milestone 5's evaluator benchmarks
show the handle costing more than it saves — most plausibly through
externalisation on every numeric comparison — this ADR is superseded. The likely
successor is not a term-based contract but a narrow typed-value accessor beside
the handle, and that is a decision to take with the benchmark in hand.

**The contract's members are fixed at milestone 4**, when the first store
implements it. What is fixed now is the shape: opaque fixed-width handle,
source-supplied equality, internalise and externalise.

## Checks

- **Checked against the accepted ADRs** (0001–0005, 0007–0021, 0023). Touches
  **0003 and 0005** (this is what allows the contract to sit at layer 1 while
  the store sits at layer 4), **0012** (the 64-bit width, and the alignment
  being deliberate), **0015** (pinned and as-of reads return quad sources, so
  they return handles), **0017** (the overlay's layer 1 placement rests on this
  — amended there to say so), and **0023** (private terms compare by decrypted
  value, which is why equality is the source's). No conflict with any.
- **Layer ownership.** `TermHandle`, the quad source contract, the term-handle
  comparer and the in-memory dataset are all **`Varve.Rdf`, layer 1**. Nothing
  in them names a store, a log, a position or a key.
- **Analyzer rule.** None reserved. The "never compare handles with `==`" rule
  above is a real candidate for a future id; it is not reserved now because
  there is no code for it to check and ADR 0004 allocates ids to rules, not to
  intentions.
- **Open questions owned.** None of Q1–Q9.
