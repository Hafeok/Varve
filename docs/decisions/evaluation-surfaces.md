---
set: evaluation-surfaces
namespace: varve
origin: "DD0009, DD0012 and DD0016 findings on Varve.Sparql.Evaluation in session 3 of #43"
decisions:
  - key: QueryResultsIsAClosedHierarchy
    statement: "An evaluation returns a QueryResults, an abstract class closed to this assembly whose three sealed forms are SolutionResults, BooleanResult and TripleResults, told apart by Kind, and the caller disposes it"
  - key: AggregateDistinctIsTheKeyword
    statement: "IExtensionAggregate.CreateAccumulator takes the aggregate's DISTINCT keyword as a bool named after it, as the algebra records the keyword, and the evaluator has already removed duplicates when it is true"
  - key: OperatorsAreEnumerators
    statement: "The evaluator's operators yield solutions as IEnumerator of ulong[], as C# iterators do, and the one hand-written cursor, BgpCursor, throws on Reset as every iterator does"
---

# Contracts and shapes on Varve.Sparql.Evaluation's surface

**Unaccepted.** Filed by session 3 of #43, for the maintainer.

Five of the evaluator's six public abstractions cite the ADRs that made them:
`IExtensionFunction` and `IRandomSource` (ADR 0056's
`UnknownExtensionFunctionIsAnError` and `RandomnessIsARandomSource`),
`IExtensionAggregate` and `IAggregateAccumulator` (the aggregation ADR's
`CustomAggregatesFromOptions` and `AccumulatorPerAggregate`), and
`IServiceHandler` (`ServiceHandlerContract`). Two findings have no decision to
cite.

**`QueryResultsIsAClosedHierarchy`.** `QueryResults` has been what
`SparqlEvaluator.Evaluate` returns since milestone 5, and the specification
(`docs/spec/sparql-evaluation.md`) shows it, but no ADR decides its shape. It
is an abstract class with a `private protected` constructor, so only this
assembly derives from it, and its three forms are sealed. `Kind` says which
form it is, and disposing it releases what a solutions cursor holds.
`QueryResults` cites this. The alternative is three unrelated result types and
three `Evaluate` methods, which would make a caller know the query form before
evaluating.

**`AggregateDistinctIsTheKeyword`.** DD0016 reports the `distinct` parameter of
`IExtensionAggregate.CreateAccumulator`. It says whether the query wrote
`DISTINCT` in the call, which is how the algebra records the keyword
(`SparqlAlgebraSurfaces.GrammarKeywordsAreBools`), and the evaluator has
already removed duplicates by then, so an aggregate needs it only to report or
to refuse. The parameter cites this. The alternative is a two-member enum, or
two methods, on a public extension contract.

**`OperatorsAreEnumerators`.** DD0012 reports `BgpCursor.Reset` because it
throws `NotSupportedException` from `IEnumerator.Reset`. Every operator in the
evaluator yields `IEnumerator<ulong[]>`. Most are C# iterators, whose compiler-
generated `Reset` throws the same exception, but DD0012 cannot see that code.
`BgpCursor` is the one cursor written by hand, because the BGP join is the
evaluator's zero-allocation path. It cites this. The alternative is an
operator cursor interface with no `Reset` (as `IQuadCursor` is in Varve.Rdf),
which is a rewrite of every operator.
