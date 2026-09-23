# 0047 — Delta composition over chains, and dictionary closure over triple terms; specification 1.3

## Status

**Accepted.** 2026-09-23. Moves the specification to **version 1.3**.
Decided by the maintainer on the two changes milestone 4 proposed
(traceability record `2026-09-23-issue-8-milestone-4-in-memory-log.md`).

## Context

Milestone 4's property tests found two statements in the specification that
the code could not satisfy as written.

**§6 said deltas under `;` form a monoid.** The composition property found a
counterexample (CsCheck seed `fIBTkyl27KJ4`), minimised to

```
a = (∅, {q})    b = (∅, {q})    c = ({q}, ∅)
(a ; b) ; c = (∅, ∅)
a ; (b ; c) = (∅, {q})
```

The operation is not associative over arbitrary deltas. It is associative over
a **chain of exact deltas** — each exact against the state the previous ones
produce — because over such a chain `;` computes the net change between two
states, and that does not depend on grouping. No log can hold `a` followed by
`b`: `b` retracts a quad `a` already removed, which I2 forbids. So every use
the specification makes of `;` (R2's `net(L(Q..P])`, R3's `Diff`) is over a
chain, and nothing built on it was wrong. The claim was.

**The monoid claim was the maintainer's error**, made when the specification
was written, and it is recorded as such in the specification's change entry.
It survived review because every example anyone had in mind was a chain.

**I3 said every id in `alloc_P` occurs in `A_P` or `meta_P`.** RDF 1.2 triple
terms break that literally. A triple term's identity depends on its
components' identities: a triple term around one blank node is a different
term from the same shape around another. So its components are allocated, and
its entry refers to them by id. A component that appears nowhere else then
occurs only inside the triple term's entry, not in `A_P` or `meta_P`. The store
and the reference model both already read I3 as *reachable* from `A_P` or
`meta_P` through entries.

## Decision

Specification 1.3:

1. **§6.** "`;` has identity `(∅, ∅)` and is associative over any chain of
   exact deltas — each exact against the state the previous ones produce —
   and in particular over any run of a log. It is not associative over
   arbitrary deltas", with the counterexample above.
2. **§4, I3.** "Every id in `alloc_P` is reachable through entries from `A_P`
   or `meta_P`: it occurs in one of them, or it is a component of an entry in
   `alloc_P` that is."
3. **§10, R3.** "delta composition is associative over chains of exact deltas".

The counterexample stays in the tests as
`composition_is_not_associative_over_deltas_no_log_could_hold`, so the
specification's own witness is a test that runs.

## Alternatives considered

- **Keep the monoid claim and restrict what a delta is**, so that `;` is only
  defined for pairs that could follow each other. That makes `;` a partial
  operation whose domain depends on a state it does not see, which is the
  same restriction said less plainly.
- **Allocate triple terms by value** — the whole term encoded in the entry,
  components not allocated — so that I3 holds literally. Rejected: a triple
  term containing a blank node would still need the blank node's id inside its
  entry, and injectivity would then rest on nested term equality rather than
  on ids.

## Consequences

- No code changes. The store composes only chains (its `DeltaChain`), and its
  allocations already satisfy I3 as now stated; the reference model already
  checks it that way.
- The documentation comments that called these "proposed changes" now point
  here.

## Checks

- **Checked against the accepted ADRs** (0001–0046). Touches **0010** (I2 is
  what makes a log's deltas a chain), **0012** (I3's closure and triple terms'
  identity), **0015** and **0017** (R2 and R3 use `;` over chains only), and
  **0043** (the model already reads I3 this way). No conflict with any.
- **Layer ownership.** `QuadDelta` is `Varve.Rdf`, layer 1; the dictionary is
  `Varve.Store`, layer 4. Neither changes.
- **Analyzer rule.** None.
- **Open questions owned.** None.
