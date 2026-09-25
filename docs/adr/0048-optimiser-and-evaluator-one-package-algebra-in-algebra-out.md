# 0048 — Optimiser and evaluator: one package, algebra in and algebra out

## Status

**Accepted.** 2026-09-24. Closes ADR 0003's open question 2 by a dated
amendment in that file. The position was taken by the maintainer at the close
of milestone 4 (traceability record
`2026-09-23-issue-8-milestone-4-in-memory-log.md`, *Decided after the report*)
and is written here at the start of milestone 5, before any SPARQL code.

## Context

ADR 0003 placed the SPARQL optimiser and evaluator together in layer 3 and left
open whether they are one package or two layers. The answer turned on one
thing: whether the evaluator consumes a plan type the optimiser owns. If it
does, the reference is same-layer and illegal, and the options were one
package, two layers, or a plan type living in layer 2 beside the algebra.

Milestone 5 has to answer it before the package list exists. Three facts narrow
it.

**The evaluator cannot sit above the store.** "Two layers" would put the
optimiser at layer 3 and the evaluator at layer 4, beside `Varve.Store`. ADR
0005's whole shape is that the evaluator is *below* the store and knows nothing
about it, running over the quad source contract at layer 1; layer 5 is the
first place that may know both. Splitting the layers is therefore not
available without moving the store, and nothing argues for that.

**A physical plan has nothing to be physical about.** The evaluator runs over
`IQuadSource`, which exposes one access path — `Match` over a pattern — and
nothing about index order, page layout or storage. A plan that named a join
algorithm or an index would be naming things the contract does not expose,
and every source would have to implement or ignore it. What an optimiser can
usefully do over this contract is reorder joins, push filters, and rewrite
patterns, all of which are expressible in the algebra itself.

**The statistics an optimiser wants come from the source at evaluation time,
not from a plan.** Cardinality is a property of the source being queried
(ADR 0049), and a plan computed against one source is stale against another.
An optimiser that asks the source while it runs needs no plan to carry the
answer in.

## Decision

### Two packages, one layer apart

| Package | Layer | Owns | References |
|---|---:|---|---|
| **`Varve.Sparql`** | 2 | The algebra: an immutable tree of node types for SPARQL 1.1 Query and Update, with SPARQL 1.2's additions present and marked. The parser, from text to algebra. The serialiser, from algebra to text. | `Varve.Rdf`, `Varve.Iri` |
| **`Varve.Sparql.Evaluation`** | 3 | The evaluator, over `IQuadSource`. The optimiser. | `Varve.Sparql`, `Varve.Rdf`, `Varve.Xsd` |

> **Amended 2026-09-25** (milestone 5b). `Varve.Sparql.Evaluation` also
> references **`Varve.Iri`** (layer 0), for `IRI()` / `URI()` (SPARQL 1.1
> §17.4.2.8), which resolve a relative IRI against the query's base by RFC
> 3986 §5, and for checking that a constructed IRI is one (RFC 3987). The
> reference is strictly downward and changes nothing else in the table; it is
> recorded here so that the table stays the complete list.

`Varve.Sparql` does not reference `Varve.Xsd`. A numeric literal in a query is
a lexical form until something evaluates it; constant folding is the
optimiser's, and the parser needs no value.

### The optimiser's output is algebra

The optimiser is a function from algebra to algebra. There is no plan type,
annotated or otherwise. It is a pass the evaluator applies by default, and a
caller may skip it — which is how the property "optimised and unoptimised
evaluation give the same solutions" is tested, and how a rewrite that turns
out wrong is bypassed without a release.

### The algebra's node types are C# records

**This is an exception to a practice, and the exception is stated here.** No
shipped Varve assembly uses a C# record: every public type in `Varve.Iri`,
`Varve.Rdf`, `Varve.Turtle` and `Varve.Store` is a struct or a sealed class
with hand-written equality, because those types sit on paths where equality is
a `[HotPath]` member comparing exactly the fields that matter and nothing
else, and where a generated `ToString` and `PrintMembers` are code size in a
browser download for no caller.

The algebra is different on both counts. **An algebra tree is allocated by
definition**, so nothing about records costs an allocation that was not
already being paid. And the serialiser's correctness claim — parse, serialise,
parse gives the *identical* tree — is a structural-equality claim over a
recursive tree of some forty node types. Records give that equality, and
`ImmutableArray<T>` children are compared element-wise by a small helper, for
free and without a hand-written `Equals` per node that would each be one more
place to forget a field. The property test that checks the round trip is only
as good as the equality it compares with, and generated equality cannot omit
a member.

The nodes are `sealed record` classes, immutable, and every node carries a
`SourceSpan`. Source positions are excluded from equality: two trees that
mean the same thing are equal wherever their text came from.

### Rewriting is by type switch, not reflection

The optimiser rewrites without reflection: an abstract `AlgebraRewriter` with
one virtual method per node type, and pattern matching by type. Both compile
to ordinary code under Native AOT.

## Alternatives considered

- **A plan type in layer 2, beside the algebra.** The option ADR 0003 named
  for the case where the plan is an annotated algebra. Rejected because the
  annotation it would carry — a cardinality — belongs to the source and not to
  the query (ADR 0049), and an annotated tree would be a tree plus a cache of
  answers that are wrong for the next source. The algebra is the plan.
- **Two layers, optimiser below evaluator.** Rejected above: the evaluator
  would land at layer 4 beside the store, which inverts ADR 0005.
- **A separate optimiser package at layer 3.** Same-layer references are
  violations (ADR 0003), and an optimiser that the evaluator does not call is
  a library nobody links.
- **Parser and evaluator in one package.** One fewer package. Rejected: a host
  that only needs to parse — a linter, a query editor, the CLI validating a
  request — would link an evaluator and its dependency on `Varve.Xsd`, and
  under Native AOT that is binary size a browser downloads.
- **Structs or hand-written classes for the algebra, as elsewhere.** Keeps the
  practice uniform. Rejected: the round-trip property needs whole-tree
  structural equality, and forty hand-written `Equals` methods are forty
  places for it to be quietly wrong. The practice exists for hot paths, and an
  algebra tree is not one.

## Consequences

- **The evaluator's entry point takes algebra**, and the optimiser is
  invisible to a caller who does not ask about it. The public shape of
  `Varve.Sparql.Evaluation` is decided in 5b against this ADR.
- **The algebra's shape is part of `Varve.Sparql`'s public API**, and every
  node is a baseline line. A rewrite the optimiser wants to express must be
  expressible in the algebra, which is a constraint on 5b and a deliberate
  one: an optimisation that needs a node the language does not have is an
  optimisation that has left the specification.
- **Records make the algebra's equality value equality and its `ToString` a
  debugging aid.** Neither is a promise of format; the serialiser is the
  format.
- **ADR 0003's open question 1 stays open.** `Varve.Shacl` and the evaluator
  are both at layer 3, and nothing here bears on it. Due at milestone 8.

## Checks

- **Checked against the accepted ADRs** (0001–0047) and the specification.
  Touches **0003** (open question 2, closed here by amendment; the layer
  table's "SPARQL optimiser and evaluator" row now names a package), **0005**
  (the evaluator stays below the store), **0022** (the evaluator runs over the
  handle contract; ADR 0050 is its measurement plan), **0024** (the algebra
  holds owned `RdfTerm`s, which is what an allocated tree may do), and
  **0049** (why no plan carries statistics). No conflict with any.
- **Layer ownership.** `Varve.Sparql` at **layer 2**; `Varve.Sparql.Evaluation`
  at **layer 3**. Both packable.
- **Analyzer rule.** None. `VARVE0001` already forbids the same-layer reference
  the rejected split would have needed.
- **Open questions owned.** None. Open question 1 of ADR 0003 remains with
  that ADR.
