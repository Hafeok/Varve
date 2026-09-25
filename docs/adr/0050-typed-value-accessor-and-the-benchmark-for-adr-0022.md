# 0050 — A typed-value accessor beside the handle, and the benchmark ADR 0022 asked for

## Status

**Accepted.** 2026-09-24. Widens the quad source contract of ADR 0022 by one
member, as ADR 0049 does, and sets the measurement that decides 0022's
revisit condition. 0022 stands as written: this ADR builds the successor it
named so that the measurement can be made with both in hand.

## Context

ADR 0022 chose an opaque 64-bit handle for the quad source contract and named
its own falsifier: milestone 5 evaluator benchmarks showing the handle costs
more than it saves, "most plausibly through externalisation on every numeric
comparison". It named the likely successor too — "a narrow typed-value
accessor beside the handle" — and said it was a decision to take with the
benchmark in hand.

The store's inline ids already carry their value (ADR 0045: canonical
`xsd:integer` in 56 bits and `xsd:boolean`), so `FILTER(?o > 5)` over a store
could compare without allocating. The layout is deliberately private, so today
the only way to the value is `TryExternalise`, which allocates an `RdfTerm`
per candidate. That is the cost 0022 worried about, on exactly the path it
predicted.

The measurement cannot be made without the accessor existing, and 5b cannot
be planned without knowing whether it will. So the accessor is built now, in
5a, and 5b measures with it and without it.

## Decision

`IQuadSource` gains:

```csharp
bool TryGetInlineValue(TermHandle handle, out InlineValue value);
```

returning true when **the handle itself encodes the term's value** and false
otherwise. `InlineValue` is a `readonly struct`: a `Kind` in
`{ None, Integer, Boolean }`, a `long Integer` and a `bool Boolean`. BCL
primitives only, so `Varve.Rdf` takes no dependency on `Varve.Xsd` for it and
the contract stays within what the reserved VARVE0007 permits.

**False means "not inline", never "not a number".** A source that answers
false has said nothing about the term; the consumer externalises and parses
as it would have before. A source that answers true has handed over the
value of a literal whose lexical form is canonical — ADR 0012's amendment is
what makes that true, because only a canonical form takes an inline id.

- **The store** decodes the inline classes it allocates. Today that is
  integers within 56 bits and booleans; a datatype added to the inline set at
  milestone 6 adds a `Kind` here.
- **`InMemoryDataset`** has no inline ids and always answers false. It could
  parse its interned term's lexical form on demand, and deliberately does
  not: the member's name says what it promises, and a source that parsed
  behind it would make the benchmark below measure parsing rather than the
  handle.
- **`QuadOverlay`** delegates to its base, whose handles the delta's are.

### The benchmark plan for ADR 0022's revisit condition

Run in **5b**, when an evaluator exists, and reported in the benchmark README
with the machine stated (ADR 0027). Three arms, one code base, the arm chosen
by an evaluator option so that the same suite runs through each:

| Arm | Numeric comparison obtains its operand by |
|---|---|
| **accessor** | `TryGetInlineValue`, externalising only on false |
| **externalise** | `TryExternalise` and parsing the lexical form, always — the handle contract as 0022 shipped it |
| **materialised** | binding owned `RdfTerm`s instead of handles throughout the evaluator, so no handle is ever held — the term-based contract 0022 rejected, approximated inside the evaluator |

Two measurements per arm:

1. **The SPARQL 1.1 query evaluation suite's wall time**, whole, over the
   in-memory projection.
2. **A FILTER-heavy micro-benchmark**: a generated dataset of one million
   quads whose objects are inline integers; `FILTER(?o > n)` at selectivities
   of roughly 1%, 50% and 99%; `ORDER BY ?o`; and `FILTER(?o = "5"^^xsd:integer)`
   as the equality case. Throughput and allocated bytes per solution.

**The verdict rule, fixed before the numbers exist.** 0022's condition fires
if the *materialised* arm beats the *accessor* arm on both measurements by
more than run-to-run noise. It does not fire if the accessor arm is at least
as fast as the materialised arm: then the handle plus the accessor is the
design 0022 predicted, and the accessor stays. If the accessor arm beats the
externalise arm and loses to the materialised one, the finding is that the
handle is the wrong contract for numeric work and the successor ADR decides
what replaces it. The externalise arm exists to show how much the accessor
bought, not to decide anything.

## Alternatives considered

- **Measure without the accessor first, then decide whether to build it.**
  The cautious sequence, and 0022 left it open. Rejected because it makes the
  measurement a comparison of the handle against nothing: without the
  accessor the externalise arm is the only handle arm, and it would lose to
  materialisation for the reason 0022 already gave, proving what was already
  known.
- **A generic `TryGetValue<T>`**. Type-safe and extensible. Rejected on ADR
  0022's own argument against generic contracts: every instantiation is real
  code under Native AOT, and a generic virtual method on the contract is the
  construct that fights AOT hardest.
- **Return an `XsdInteger` / `XsdDecimal`** rather than BCL primitives. Puts
  the value in its proper type. Rejected: it makes `Varve.Rdf` reference
  `Varve.Xsd` for one struct, and the contract would then carry a layer 0 type
  the reserved VARVE0007 does not list. The evaluator converts `long` to the
  XSD type at zero cost.
- **Make the inline layout public** so the evaluator decodes handles itself.
  No new member. Rejected outright: the layout is milestone 6's to freeze
  (ADR 0012, 0045), an in-memory dataset's handles mean something else, and
  the point of the contract is that no consumer reads bits.
- **A `TryGetNumeric` returning decimal or double as well**, ahead of the
  store inlining them. Rejected as ADR 0012 rejected a larger inline set:
  start with what is provably useful and grow with evidence. The struct's
  `Kind` grows additively.

## Consequences

- **Two members join the contract in one milestone** (with ADR 0049), and
  four implementers change. Both are unshipped.
- **The evaluator in 5b is written with an option that turns the accessor
  off**, and that option is not removed until the measurement is reported.
  A benchmark that needs a branch to run is a benchmark that does not get
  re-run.
- **ADR 0022's revisit condition has a date and a rule now**: 5b, and the
  verdict rule above. Until then 0022 is neither confirmed nor fired.
- **The accessor is only as useful as the inline set**, which is two
  datatypes. A `FILTER` over decimals or dates externalises today. That is
  the evidence ADR 0012 asked for before growing the set, and the micro-
  benchmark is where it will be visible.

## Checks

- **Checked against the accepted ADRs** (0001–0049) and the specification.
  Touches **0022** (the successor it named, built so its condition can be
  judged; the condition and its due milestone are unchanged), **0012** and
  **0045** (inline ids carry canonical values; the layout stays private),
  **0024** (`TryExternalise` remains the materialising path and the only
  other one), **0027** (the benchmark discipline), and **0049** (the second
  widening in the same milestone). No conflict with any.
- **Layer ownership.** `InlineValue` and the member are **`Varve.Rdf`, layer
  1**. The store's decoding is `Varve.Store`, layer 4.
- **Analyzer rule.** None.
- **Open questions owned.** None. The revisit condition belongs to 0022.
