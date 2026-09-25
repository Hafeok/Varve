# 0053 — Aggregation: hash grouping, one accumulator per aggregate

## Status

**Accepted.** 2026-09-25. Decided by the maintainer on the milestone 5b plan
(traceability record `2026-09-25-issue-9-milestone-5b-evaluation.md`), and
written here before the evaluator. Settles the first of the five questions
milestone 5a left to 5b (the aggregate representation, 5a record, *What 5b
needs*, item 1).

## Context

`sparql-algebra.md` §4.6 keeps each `AggregateExpression` where the author
wrote it, inside the `Extend`, `Filter` or `OrderBy` above a `Group`, and
leaves the extraction SPARQL 1.1 Query §18.2.4.1 describes to the evaluator.
The evaluator has to decide three things the algebra does not: how groups are
formed and keyed, how each aggregate is computed per group, and what happens
when a value in a group is an error. It also has to say what bounds the
memory a `GROUP BY` over a large input takes, because in memory it takes one
entry per group and nothing in this milestone spills.

SPARQL 1.1 Query §18.5.1 defines the operators precisely: `Group` partitions
the solutions by `ListEval` of the key expressions, retaining errors as key
values; `Aggregation` applies a set function to the multiset of `ListEval` of
the aggregate's argument per group, through `Distinct` first when `DISTINCT`
is written; §18.5.1.2–§18.5.1.8 define `Count`, `Sum`, `Avg`, `Min`, `Max`,
`GroupConcat` and `Sample` one by one. §11.5 shows the consequence for an
error inside a group: the aggregate's value is an error, and the `Extend`
that binds it leaves the variable unbound — the solution is kept, the binding
is not.

## Decision

**Evaluation follows §18.5.1 literally.**

- **Extraction.** Every `AggregateExpression` reachable from the operators
  between a `Group` and the next `Project` (or the next `Group`, or the top of
  the query), without entering an `ExistsExpression`'s pattern or a
  sub-`SELECT`, is collected once, given a slot the author cannot name, and
  replaced in its expression by a read of that slot. A variable read in such
  an expression that is not a group key is read as `SAMPLE(v)`, the 1.2
  draft's rule (`sparql-algebra.md` §4.6); the parser's §11.4 checks leave
  only the cases the draft allows.
- **Grouping.** The key of a solution is the list of its key expressions'
  values. An error — an unbound variable included (`ListEval((unbound), μ) =
  (error)`) — is a key value of its own, equal to every other error in the
  same position, and the key variable of such a group is unbound in the
  output. Keys compare by **the source's term equality** (`IQuadSource.TermComparer`
  for handles, the evaluator's own table for computed terms), not by value:
  `"1"^^xsd:integer` and `"01"^^xsd:integer` are two groups, as §18.5.1's
  `ListEval(…) = ListEval(…)` over RDF terms says.
- **Implicit grouping.** A `Group` with no keys over an empty input yields one
  group with no solutions, so `COUNT(*)` is `0`; a `Group` with keys over an
  empty input yields none.
- **Accumulators.** Each aggregate is an accumulator fed the values of its
  argument, one solution at a time, per group:

  | Aggregate | Accumulator | Errors in the argument | Empty group |
  |---|---|---|---|
  | `COUNT(e)`, `COUNT(*)` | a count; `*` counts solutions | removed (§18.5.1.2) | `0` |
  | `SUM` | running `op:numeric-add`, promoting per §17.3 | the result is an error | `0` (`xsd:integer`) |
  | `AVG` | `SUM` and `COUNT`, divided at the end with `op:numeric-divide` | the result is an error | `0` (`xsd:integer`) |
  | `MIN`, `MAX` | the least or greatest value under `ORDER BY`'s order (§15.1) | the result is an error | error |
  | `SAMPLE` | the first value that is not an error | removed | error |
  | `GROUP_CONCAT` | the `STR` of each value, joined by the separator (default U+0020) | the result is an error | `""` |

  A non-numeric value in `SUM` or `AVG` is an error in the same sense, and a
  non-literal in `GROUP_CONCAT` is too; an IRI in `MIN` or `MAX` is not — it is
  a term, and §15.1 orders it before every literal. An error means the
  argument's evaluation failed (an unbound variable, a type error), which
  `COUNT` removes by §18.5.1.2. For the others §18.5.1 is silent — `Flatten(M)`
  is defined over lists that may hold errors, and `Min`, `Max` and `Sample`
  are defined over terms — so Oxigraph is the tie-breaker (ADR 0038, D1): an error
  makes `MIN` and `MAX` an error, and `SAMPLE` returns a value that is not
  one, erroring only when every value is.
  **An aggregate whose result is an error leaves its binding unbound**; it
  never fails the query (§11.5).
- **`DISTINCT` inside an aggregate** is a set per accumulator, keyed by term
  equality of the argument's value, consulted before the value reaches the
  accumulator. `COUNT(DISTINCT *)` keys on the whole solution.
- **`GROUP_CONCAT` has no `ORDER BY`.** SPARQL 1.1 does not define one and
  neither does the 1.2 draft; the order of concatenation is the order in which
  solutions reach the group, which §18.5.1.7 leaves unspecified. The result
  is always a simple literal. §18.5.1.7 defines the result through `CONCAT`,
  which would keep a shared language tag, but the suite's `agg-groupconcat-4`
  requires `GROUP_CONCAT` over `"1"@en` and `"2"@en` to equal `"1 2"`, and
  `agg-groupconcat-06` the same of a single `"1"@en`. Oxigraph 0.5.11 keeps
  the shared tag and fails both; Oxigraph is the tie-breaker only where the
  specification and its suite are silent (ADR 0038, D1), and here the suite
  speaks — an `upstream-defect` in 0038's D2 terms.
- **Custom aggregates** — `sparql-algebra.md` open question 3 — are passed in
  the evaluator's options, like extension functions (ADR 0056): an
  `IExtensionAggregate` keyed by its IRI creates one accumulator per group.
  An aggregate whose IRI has no entry is an evaluation error that names the
  IRI, raised when the query is compiled, not per group.
- **Hash aggregation, in memory.** One hash table per `Group`, one entry per
  group, one accumulator per aggregate per entry, and one set per `DISTINCT`
  aggregate per entry. **No spilling.** The memory a grouping takes is
  bounded by the caller's resource governance — the cancellation token now
  (ADR 0052), and the server's per-request limits at the operability
  milestone — and the specification says so rather than implying a bound the
  evaluator does not enforce.

## Alternatives considered

- **Rewrite to the draft's `AggregateJoin` shape before evaluation.** Keeps
  the evaluator's operators one-to-one with the 1.2 draft's algebra. Rejected
  for the reason `sparql-algebra.md` §4.6 gave the parser: the names it needs
  are invented either way, and doing the collection in the evaluator's
  compiler is the same work without a second tree.
- **Sort-based grouping.** Groups by sorting on the keys, so memory is the
  sort's and streaming output is ordered. Rejected: SPARQL's key equality is
  term equality over handles whose order is the source's business, and a sort
  needs an order that agrees with that equality; a hash needs only the
  comparer the source already supplies.
- **Value equality for keys** (`1` and `1.0` in one group). Some engines do
  it. Rejected: §18.5.1 compares `ListEval` results, which are RDF terms, and
  the suite's `group` cases expect terms.
- **Errors removed from every aggregate**, as `COUNT` removes them. Keeps
  one ill-typed value from hiding the well-typed ones. Rejected: §18.5.1.3
  defines `SUM` as a chain of `op:numeric-add`, which an error breaks, and
  where the text is silent Oxigraph does not remove them from `MIN` and `MAX`;
  a third behaviour would be a third answer for the differential run to
  explain.
- **Spilling to storage** above a threshold. Rejected for this milestone: the
  evaluator has no storage (ADR 0005), and the bound belongs to the host.

## Consequences

- **A `GROUP BY` over a large input can exhaust memory**, and only
  cancellation stops it. The server at milestone 7 is where a per-request
  memory limit belongs; nothing here prevents one.
- **`GROUP_CONCAT`'s output order is the input's**, so it is deterministic for
  one source and one plan, and changes when the optimiser reorders a join.
  The optimiser's equivalence property (ADR 0048) therefore compares
  `GROUP_CONCAT` results as multisets of their parts.
- **A custom aggregate needs a registration** or the query does not run, and
  the error says which IRI.

## Checks

- **Checked against the accepted ADRs** (0001–0052) and the specification.
  Touches **0048** (the extraction is the evaluator's; the algebra is
  unchanged), **0022** (keys compare by the source's comparer, which is term
  equality), **0051** (numeric promotion and division are `Varve.Xsd`'s),
  **0052** (cancellation is the only bound in this milestone), **0003**
  (options, not a registry), **0056** (custom aggregates arrive as
  options), and **0038** (where Oxigraph decides and where it does not). No
  conflict with any.
- **Layer ownership.** `Varve.Sparql.Evaluation`, **layer 3**.
- **Analyzer rule.** None.
- **Open questions owned.** None. `sparql-algebra.md` open question 3 is
  closed by this ADR and ADR 0056.
