---
set: evaluation-hot-path-scope
namespace: varve
origin: "VARVE0003 findings on Varve.Sparql.Evaluation in session 3 of #43"
decisions:
  - key: SolutionCostsItsRow
    statement: "A join that produces a solution copies its working row into a new one, one ulong array per solution, because a solution is handed to the next operator and outlives the join's next step"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: ScanOpensTheSourcesCursor
    statement: "A scan asks the quad source for one cursor per graph it reads and disposes it when that graph is done, so a triple pattern costs the source's cursor once per binding of the patterns before it, never once per quad"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: FromMergeRemembersTriples
    statement: "A scan over the merge of several FROM graphs remembers each triple it has returned in a set, which grows with the triples, because the merge returns a triple in two of the graphs once"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: FromNamedIsASetLookup
    statement: "A scan restricted by FROM NAMED checks each graph against the query's set of named graphs, a hash set lookup under the source's term equality that allocates nothing"
  - key: MaterialisedArmOwnsItsTerms
    statement: "In the materialised arm, the evaluator externalises every handle a scan finds and interns the term locally, and looks a local term up in the source to join on it, because that arm measures the cost of owning every term"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: NestedPatternsExternalise
    statement: "A triple pattern with a nested triple pattern in it externalises the triple term a scan finds and matches the nested pattern against that term"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: ConstantsResolveOncePerExecution
    statement: "A constant in an expression looks its term up in the execution's source once per execution, interning it locally when the source does not hold it, and reuses the result for every solution"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: TermBuildingExpressionsAllocate
    statement: "A built-in function or a cast that makes a new term allocates the term it makes and the text it builds, and a computed number allocates its literal when it is bound or returned; comparisons, logic, arithmetic, variables and constants do not"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: ExistsRunsItsPattern
    statement: "EXISTS and NOT EXISTS open their pattern's operator against the solution being filtered, once per solution, with what that operator allocates"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
  - key: ExtensionFunctionsAreTheCallersCode
    statement: "A call to an extension function passes its arguments as an array of terms and runs the caller's code, which the evaluator does not hold to its own rules"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-29T00:00:00Z
---

# What the hot-path rules do not cover in the evaluator

**Unaccepted.** Filed by session 3 of #43, for the maintainer.

Session 3 held the evaluator's hot paths to `VARVE0003` (ADR 0064): the BGP
join, the scan cursor, the solution row, and expression evaluation, which is
per solution in a `FILTER`. `Varve.Analyzers` now holds an override of a
`[HotPath]` abstract member to the rule, as it already did an implementation of
a `[HotPath]` interface member. `Expr.Eval` is marked, so every expression node
is checked. The small members these call were marked. The rest are sorted here,
each cited where the path crosses it, by a private helper where there was one
to extract.

Two accepted decisions answered a finding without a new key.
`TypedValueAccessorAndTheBenchmarkForAdr0022.FalseMeansNotInline` is why
`Exec.Materialise` is on the value path: a handle that is not inline is
externalised and parsed. `QuadSourceTermHandle.SourceSuppliesEquality` is why
term equality calls the source's comparer.

**`SolutionCostsItsRow`.** `Rows.Copy`. The row is the 48 bytes per solution that
`AllocationTests` pins. The alternative, a pooled row returned by the consumer,
changes every operator's contract.

**`ScanOpensTheSourcesCursor`.** `ScanCursor.Match` and `.CloseSource`. The
cursor is the source's (`IQuadSource.Match`, ADR 0022), and the store's
allocates. `ScanCursor` itself is reused (its remarks said so already). The
alternative is a resettable cursor on the quad source contract.

**`FromMergeRemembersTriples`.** `ScanCursor.ForgetSeen` and `.FirstTime`. Only
with two or more `FROM` graphs. The alternative, a merge over sorted cursors,
needs an order the contract does not promise.

**`FromNamedIsASetLookup`.** `ScanCursor.IsNamed`. Filed by session 3 of #43, after
the maintainer admitted `ImmutableArray<T>` and `CancellationToken` by member but
not the collections: a `HashSet<T>` lookup does not allocate, but the type's other
members do, so the one call cites this rather than the allow-list admitting the
type. The alternative is a sorted handle array searched in place, which needs an
order the source's term equality does not give.

**`MaterialisedArmOwnsItsTerms`.** `Exec.MaterialiseFromSource` and
`.InternaliseLocal`. The arm exists to measure owning every term (ADR 0050);
avoiding the allocation would defeat it.

**`NestedPatternsExternalise`.** `BgpCursor.UnifyExternalised` and `.UnifyTerm`. RDF 1.2 triple
terms in a pattern. The alternative is a structural match over handles, which
the quad source contract has no member for.

**`ConstantsResolveOncePerExecution`.** `ConstExpr.Resolve`. Per execution, not
per solution: `ConstExpr.Eval` calls it only when the execution changes.

**`TermBuildingExpressionsAllocate`.** `FunctionExpr.Eval`, `CastExpr.Eval`, and
`Terms.Literal(XsdNumeric)`, which turns an arithmetic result into a term only
when it leaves the expression.
`STR`, `CONCAT`, `REGEX`, `IRI`, `BNODE`, the casts: each makes a term. The
exemption covers the function node as a whole, so a function that could be
allocation-free (`isIRI`, `BOUND`) is not checked either. The alternative is one
node per function.

**`ExistsRunsItsPattern`.** `ExistsExpr.Eval`. An operator tree run per
solution.

**`ExtensionFunctionsAreTheCallersCode`.** `ExtensionExpr.Eval`. The argument
array is `IExtensionFunction`'s signature (ADR 0056).
