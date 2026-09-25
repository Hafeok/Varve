# 0049 — Cardinality estimates on the quad source

## Status

**Accepted.** 2026-09-24. Widens the quad source contract of ADR 0022 by one
member, as that ADR's *Consequences* anticipated: "if that contract turns out
to be too narrow to carry the statistics an optimiser needs, the answer is to
widen the contract deliberately, in a superseding ADR — not to move the
evaluator down a layer". This widens; it does not supersede, because nothing
0022 decided changes.

## Context

An optimiser that reorders joins needs to know which pattern is small. Over
`IQuadSource` as it stood it could find out only by scanning, which is the
cost it exists to avoid.

The store knows the answer cheaply. Its default projection is sorted runs in
six orders (ADR 0041), so every combination of bound positions is a prefix
range in some order, and a range's size is two binary searches. An in-memory
dataset with no index knows it only by counting. An overlay knows it if its
base does. Nothing in the contract let any of them say so.

The question is what the member promises, because an estimate that can be
wrong in unknown ways is worth less than no estimate: an optimiser that trusts
a bad number picks a bad order, and the failure is a slow query nobody can
attribute.

## Decision

`IQuadSource` gains:

```csharp
CardinalityEstimate Estimate(TermHandle subject, TermHandle predicate, TermHandle @object, GraphPattern graph);
```

taking exactly the pattern `Match` takes, and returning a `readonly struct`:

| Member | Meaning |
|---|---|
| `Count` | The estimate, when there is one |
| `IsExact` | `Count` is the number of quads `Match` would yield for this pattern, at this source's current state |
| `IsUnknown` | The source cannot say; `Count` is meaningless |

**Three promises**, and they are the contract:

1. **An exact estimate is exact.** A source that says `IsExact` is answering
   the same question `Match` answers, and a test may assert equality.
2. **An unknown is honest.** A source that cannot estimate says so rather than
   returning a guess. A consumer that gets `Unknown` falls back to whatever it
   would have done without the member.
3. **An estimate that is neither is a count the source has reason to believe**,
   and the source's documentation says how it was derived. No source in this
   milestone returns one; the case exists so that a backend that can only
   sample (a remote endpoint, a projection with summary statistics) has a
   truthful answer available.

**The cost is bounded by the source's documentation**, and a consumer may
call it freely only where that documentation says it is cheap. `Match` is not
a permitted implementation of it in a source that claims to be an index.

### The implementations in this milestone

- **`InMemoryDataset`** counts by scan and reports it exact. It is linear,
  which its documentation already says of `Match`: it exists to hold a parsed
  file and to give layer 3 something to run against, not to be an index.
- **The store** (`DatasetView` and the sources behind it) sums, over its runs,
  the size of the prefix range in the order that serves the pattern, asserted
  keys minus retracted keys. Each run is a delta exact against the state the
  older runs produce (I2, ADR 0010; merged runs preserve it, ADR 0041), so the
  sum is the count and the answer is **exact**, at `O(runs × log n)`.
  `AnyNamed` on a pattern that is not graph-first is `Any` minus
  `DefaultGraph`, both prefix ranges. A future backend whose runs are not a
  chain of exact deltas marks its answer estimated instead; the arithmetic is
  the same.
- **`QuadOverlay`** takes its base's estimate and adjusts it by the delta —
  for each delta quad matching the pattern, plus one if asserted and not in
  the base, minus one if retracted and in the base — and stays exact when the
  base is, at `O(|delta|)` lookups. An unknown base is an unknown overlay.

### Where it lives

`Varve.Rdf`, layer 1, beside `Match`: a fact about quad sources, and the
lowest layer that can define it without knowing its implementers (ADR 0003).

## Alternatives considered

- **Statistics carried on a plan** the optimiser builds once. Rejected by ADR
  0048: a count is a property of the source being queried, and a plan computed
  against one source is stale against the next commit and wrong against a
  different source.
- **A separate `IQuadStatistics` interface** a source may also implement.
  Keeps `IQuadSource` unchanged. Rejected: the optimiser would test for it at
  every source, an overlay would have to implement it conditionally on its
  base, and a source that forgot it would silently become un-optimisable. One
  member with an `Unknown` answer says the same thing without the type test.
- **An estimate with error bounds** — a range rather than a count. More
  honest for a sampling backend. Rejected for now as a shape with no
  implementer; the struct can gain bounds additively when one exists, and
  `IsExact` already says when they would be zero.
- **`long Count(...)` returning -1 for unknown.** Fewer types. Rejected: a
  sentinel is exactly the ambiguity ADR 0022's amendment removed from the
  graph position, and "exact" is a separate fact from "known".
- **Widening only the store's `DatasetView`**, not the contract. Rejected: the
  evaluator at layer 3 sees `IQuadSource` and nothing else.

## Consequences

- **Adding a member to a public interface breaks every implementer.** Nothing
  is shipped and both baselines are unshipped (ADR 0035); the four
  implementers in this repository change in the same commit; an implementer
  outside it does not yet exist.
- **The store's answer is exact only because its runs are effective deltas.**
  That is I2 doing work at read time, which ADR 0010 said it would, and it is
  a claim the store's tests assert by comparing `Estimate` with a counted
  `Match` over generated datasets.
- **The optimiser in 5b has a number to plan with**, and the property that
  optimised evaluation equals unoptimised evaluation is what catches a plan
  that trusted a wrong one.
- **A `Count` at layer 3 is not a SPARQL `COUNT`.** The estimate is over the
  source's quads; an aggregate is over solutions. Nothing here shortcuts the
  other, and an evaluator that used an estimate as an answer would be wrong
  on the first overlay with a duplicate.

## Checks

- **Checked against the accepted ADRs** (0001–0048) and the specification.
  Touches **0022** (widened, as its consequences said it might be), **0010**
  and **0041** (why the store's sum is exact), **0017** (the overlay adjusts
  its base), **0005** (the evaluator asks the source, never the store), and
  **0048** (no plan carries this). No conflict with any.
- **Layer ownership.** `CardinalityEstimate` and the member are **`Varve.Rdf`,
  layer 1**. The store's implementation is `Varve.Store`, layer 4, internal.
- **Analyzer rule.** None.
- **Open questions owned.** None.
