# SPARQL evaluation

Functional specification for `Varve.Sparql.Evaluation` (layer 3, ADR 0048):
the optimiser and the evaluator, from a `Query` in the algebra of
[`sparql-algebra.md`](sparql-algebra.md) to solutions, a boolean, or triples,
over any `IQuadSource`. The result formats that carry those answers over a
wire are [`sparql-results.md`](sparql-results.md) (readers at 5b, writers at
5c).

Status: Accepted. Changes only together with the ADR that motivates the change.

## 1. Normative references

- **SPARQL 1.1 Query Language**, W3C Recommendation, 21 March 2013. **Section
  numbers in this document are 1.1's** unless prefixed "1.2": §13 datasets,
  §15 solution modifiers, §16 query forms, §17 expressions and the function
  library, §18.3 basic graph patterns, §18.4 property paths, §18.5 the algebra
  and §18.5.1 aggregates, §18.6 evaluation semantics.
- **SPARQL 1.2 Query Language**, Working Draft 21 September 2026 (the draft
  `sparql-grammar.md` pins): §17.4.2.9–§17.4.2.11 and §17.4.2.17 the
  directional-language functions, §17.4.6 the triple-term functions.
- **SPARQL 1.1 Federated Query**, 21 March 2013: §2.3 and §3.2, `SERVICE`.
- **XPath and XQuery Functions and Operators 3.1**: the `op:` and `fn:`
  functions §17.3 and §17.4 name, through `Varve.Xsd` ([`xsd.md`](xsd.md)).
- **RFC 4122** §4.4 (`UUID`), **RFC 1321** (`MD5`), **FIPS 180-4** (the SHA
  functions), **RFC 3986** §5 and **RFC 3987** (`IRI`), **BCP 47** §3.3.1 /
  **RFC 4647** §3.3.1 (`langMatches`).
- **ADRs** 0022 (the handle), 0049 (estimates), 0050 (the inline accessor and
  its benchmark), 0051 (value comparison, the dateTime order), 0052 (the
  pinned read), 0053 (aggregation), 0054 (paths), 0055 (`SERVICE`), 0056
  (options, clock, randomness).

## 2. The entry point, and who owns what

```csharp
var evaluator = new SparqlEvaluator(options);            // immutable, reusable
using QueryResults results = evaluator.Evaluate(query, source, cancellationToken);
```

- **`Evaluate` takes a source it does not own** (ADR 0052). It never pins,
  never disposes the source, and never learns that the source is a pinned
  view of a store. The caller keeps the source alive until the results are
  disposed, and disposes it afterwards; disposing a pinned view while results
  are still being read is a caller error, reported by the view's
  `ObjectDisposedException`. The documentation on `Evaluate` says so, and
  `Dataset.Pin()`'s documentation says the other half.
- **Results are streamed** where the operators allow: a `SELECT` without
  `ORDER BY`, `DISTINCT`, `GROUP BY` or a hash join over its whole input
  produces its first solution before reading its last quad. Disposing the
  results stops the evaluation and releases every cursor it opened.
- **Cancellation** is a first-class outcome (ADR 0052). The token is checked
  at every operator boundary — each solution an operator produces or
  consumes — and inside every scan, closure search, sort and grouping at
  least once per 1,024 steps. A cancelled evaluation throws
  `OperationCanceledException` from the `MoveNext` that noticed it.
- **Errors that fail the query** — a `SERVICE` failure without `SILENT`
  (§6.12), `NOW()` without a clock (§7.8), an unknown custom aggregate —
  throw `QueryEvaluationException`, whose message names the cause. **Errors
  inside expressions never fail the query** (§7.1).

The result is one of three:

| Query form | Result | Rows |
|---|---|---|
| `SELECT` | `SolutionResults` | `Variables`, then `MoveNext`; per column `TryGetHandle` (the source's handle, no allocation) and `TryGetTerm` (externalised) |
| `ASK` | `BooleanResult` | `Value` |
| `CONSTRUCT`, `DESCRIBE` | `TripleResults` | `MoveNext`, then `Subject`, `Predicate`, `Object` as `RdfTerm` |

`TryGetHandle` returns false for an unbound column and for a term the source
does not hold (a computed literal, a fresh blank node, a `VALUES` constant it
has never seen); `TryGetTerm` returns false only when the column is unbound.

## 3. Options — ADR 0056

`EvaluationOptions` is immutable (`init` properties) and has no ambient
defaults:

| Option | Default | Meaning |
|---|---|---|
| `Optimise` | `true` | Apply the optimiser (§8). The normalisation of §5.1 runs regardless. |
| `ValueAccess` | `InlineAccessor` | How an expression obtains a term's value: ADR 0050's three arms (§10) |
| `ImplicitTimezoneOffsetMinutes` | `0` (UTC) | F&O §10.4's implicit timezone, for comparing dateTimes (§7.4) |
| `Clock` | none | A `TimeProvider`; `NOW()` without one fails the query naming this option. The caller's line is `Clock = TimeProvider.System`. |
| `Randomness` | none | An `IRandomSource`; `RAND`, `UUID`, `STRUUID` without one fail the query naming this option. |
| `ServiceHandler` | refuses | ADR 0055 |
| `Functions` | none | Extension functions by IRI (§7.10) |
| `Aggregates` | none | Custom aggregates by IRI (ADR 0053) |
| `RegexTimeout` | 1 second | Bounds one `REGEX` or `REPLACE` match (§7.6) |

## 4. Solutions

### 4.1 Representation

A solution is **one array of 64-bit slots**, one slot per variable the query
mentions — the author's, the blank-node variables of §4.3, the fresh path
variables of ADR 0054, and the hidden aggregate and group-key slots of ADR
0053 — followed by one mask word per 64 slots. A slot holds `0` when unbound,
and otherwise either **a source handle** (its bit clear in the mask) or **a
local term** (its bit set), an index into the execution's own term table.

A local term exists because not every term in a solution is the source's:
`BIND` computes literals, `BNODE` mints blank nodes, `VALUES` and constants
name terms the source may never have seen, and `SERVICE` returns terms from
elsewhere. **A computed term is internalised first**: the evaluator asks the
source (`TryInternalise`) and uses its handle when it has one, and creates a
local term only when it does not. That invariant is what makes equality
cheap and correct:

- source against source: `IQuadSource.TermComparer` (ADR 0022), which is term
  equality and, over a store with erasure, compares private terms by
  plaintext;
- local against local: the table interns by `RdfTerm` equality, so equal
  terms have equal indexes;
- source against local: never equal, because a term the source holds is never
  made local.

`0` is free to mean unbound because `TermHandle.None` is never the handle of a
term (it is the wildcard and the default graph, `rdf-model.md`).

### 4.2 Compatibility and merge

Two solutions are **compatible** (§18.5, *Compatible Mappings*) when every
slot bound in both holds equal terms under §4.1. Their **merge** takes each
slot from whichever binds it. Every join, left join and minus in §6 is over
these two definitions.

### 4.3 Blank nodes in patterns

**A blank node in a query pattern is a variable that is never projected.**
`sparql-algebra.md` §3.1 keeps `BlankNodePattern` in the tree; the evaluator
gives each distinct label a slot of its own, named `_:label`, which no
author's variable can be named because `:` is outside `VARNAME` (`[166]`).
Basic graph pattern matching (§18.3.1) maps it like a variable; `SELECT *` was
resolved to named variables at parse time and so never includes it. In a
`CONSTRUCT` template a blank node is not a variable: it is minted afresh per
solution (§9.1).

### 4.4 Variable scope

Slots are shared by name across the query, which is correct wherever the
same name means the same variable and is made correct at the one boundary
where it does not: **a sub-`SELECT`'s variables that it does not project are
not the outer query's** (§18.2.1, §12). The `Project` operator of a
sub-`SELECT` therefore evaluates its input with the incoming solution
restricted to its projected variables (§6.1), and clears every slot it does
not project on the way out. `Group` clears every slot that is not a key or an
aggregate on its way out, for the same reason.

## 5. From the algebra to operators

### 5.1 Normalisation — ADR 0054

Always applied. Property path patterns whose top is `link`, `inv`, `seq` or
`alt` become triple patterns, swapped patterns, joins through fresh variables
(`.p0`, `.p1`, …) and unions; what remains as `PathPattern` is a closure or a
negated property set (§6.10).

### 5.2 The optimiser — §8

Applied when `Optimise` is set. Its output is algebra (ADR 0048).

### 5.3 Compilation

Once per execution: the variable table (§4.1) is built; every constant in a
pattern is internalised through the source **once** — a constant the source
cannot internalise makes its triple pattern match nothing, and a constant in
an expression becomes a local term with its value parsed once; the aggregates
above each `Group` are extracted (ADR 0053); and the tree is turned into
operators.

## 6. Operators — §18.5, §18.6

Every operator is evaluated **given an incoming solution** μ (the empty
solution at the top). For the operators marked *substitutable* — `Bgp`,
`PathPattern`, `Values`, `Graph` of a substitutable pattern, and `Join` and
`Union` of substitutable patterns — evaluating given μ is the same as
evaluating alone and keeping the results compatible with μ, merged with it;
`Join` uses this (§6.2). For every other operator, given μ means
**substitution** in §18.6's sense (`substitute(P, μ)`), which is what `EXISTS`
needs (§6.13); in ordinary evaluation no non-substitutable operator is ever
given a non-empty μ.

### 6.1 `Project`, `Distinct`, `Reduced`, `Slice`, `OrderBy` — §15, §18.5

- `Project(P, V)`: μ restricted to `V`; the results' other slots cleared.
- `Distinct`: a hash set of the projected solutions under §4.1's equality.
  `Reduced` is evaluated as `Distinct`, which §15.3.2 permits.
- `Slice(P, offset, limit)`: skips and stops; the input is not read past the
  limit.
- `OrderBy(P, conditions)`: materialised and sorted by a stable merge sort
  under the order of §7.5, each condition in turn; equal keys keep their
  input order.

### 6.2 `Join` — §18.5 *Join*

When the right operand is substitutable, each left solution is the incoming
solution of the right: an index nested loop in which a bound variable
becomes a bound position in the next scan. Otherwise the right operand is
evaluated once, alone, into a hash table keyed on the variables both sides
certainly bind; each left solution probes it and is merged with each
compatible entry. A key variable that a right solution leaves unbound puts
that solution in every bucket's candidate list, so the table never loses a
compatible pair.

### 6.3 `LeftJoin` — §18.5 *LeftJoin*

`LeftJoin(P1, P2, F)`: for each solution μ1 of `P1`, the solutions μ2 of `P2`
compatible with it for which `F` over merge(μ1, μ2) has effective boolean
value true; if there are none, μ1 alone. `F` absent is true; an error in `F`
is false (§17.2). `P2` is evaluated given μ1 when substitutable, otherwise
once into a hash table as for `Join`.

### 6.4 `Filter` — §18.5 *Filter*

Keeps a solution when the expression's effective boolean value is true; false
and error both drop it (§17.2).

### 6.5 `Union` — §18.5 *Union*

The left's solutions, then the right's: a multiset union.

### 6.6 `Minus` — §18.5 *Minus*

`Minus(P1, P2)` keeps μ1 unless some μ2 of `P2` is compatible with it **and
shares a bound variable with it** — `dom(μ1) ∩ dom(μ2) ≠ ∅`. `P2` is evaluated
once, alone.

### 6.7 `Extend` — §18.5 *Extend*

Binds the variable to the expression's value; **an error leaves it unbound**
and keeps the solution (§18.5: `Extend(μ, var, expr) = μ` if `expr(μ)` is an
error).

### 6.8 `Values`

A table of constants, `UNDEF` as unbound; given μ, each row compatible with μ
merged with it.

### 6.9 `Bgp` — §18.3

Each triple pattern is one `IQuadSource.Match` over the **active graph**
(§6.11), with each position bound when it is a constant or a variable the
current solution binds, and a wildcard otherwise; a variable repeated within
one pattern (`?x :p ?x`) is checked on the quad. Patterns are matched in the
order the optimiser left them (§8.2). The empty `Bgp()` yields the incoming
solution once, and is the identity of `Join`.

A **triple term pattern with a variable inside** (1.2) is matched by scanning
its position as a wildcard, externalising the term found, and unifying its
components.

**The scan adapter allocates nothing per quad.** It binds into the solution
array it will emit and allocates that array only when a quad matches; the
store's cursor allocates nothing per quad (milestone 4). §11 states the bound.

### 6.10 `PathPattern` — §18.4, ADR 0054

Only closures and negated property sets reach here. `ZeroOrMorePath` and
`OneOrMorePath` run `ALP` from each start node with a visited set per start;
`ZeroOrOnePath` is the start and its one-step successors, deduplicated. With
both ends unbound the start nodes are `nodes(G)`, the subjects and objects of
the active graph.

**The zero-length step** of `*` and `?` reaches its start whatever the graph
holds when either end is a term **written in the query**; when both ends are
variables it reaches only nodes of the active graph, **even when an incoming
solution has bound one of them**. A variable bound by `VALUES` or by a join is
still a variable of the path pattern, and `sparql11/property-path`'s
`values_and_path` requires exactly this: the `VALUES` binds a term the graph
does not hold, and the expected answer has no zero-length row for it.

**A negated property set** is a scan in the direction its members name,
keeping triples whose predicate is not among them, and **each direction is a
set**: §18.4 defines `eval(Path(x, NPS(S), y))` as `{ μ | ∃ triple … }`, so
two triples between the same two nodes are one solution, whichever ends are
bound. A set with both forward and inverse members is §18.2.2.3's
`alt(NPS(fwd), inv(NPS(inv)))`, and its halves join as `alt` does, a multiset
union. The optimiser property (§8.6) found the first implementation counting
each triple when both ends were free and each pair once when one was bound,
which made the answer depend on join order; Oxigraph 0.5.11 does the same
(two solutions for `?c !:p0 ?c` over two such triples, one once `?c` is
bound by `VALUES`), and is not followed here.

### 6.11 `Graph`, and the dataset — §13, §18.6

The **dataset** is the source's, unless the query has a `DatasetSpec`
(`FROM`, `FROM NAMED`), in which case §13.2 applies:

- the **default graph** is the RDF merge of the `FROM` graphs, each read
  from the source's named graph of that IRI — a triple in two of them is in
  the merge once, so a scan over two or more deduplicates; with no `FROM` and
  some `FROM NAMED`, the default graph is empty;
- the **named graphs** are the `FROM NAMED` graphs; with `FROM` and no `FROM
  NAMED`, there are none.

A `FROM` or `FROM NAMED` IRI the source cannot internalise is a graph with no
triples. The active graph starts as the default graph.

`Graph(<g>, P)` evaluates `P` with `<g>` active if `<g>` is a named graph of
the dataset, and yields nothing otherwise. `Graph(?g, P)` ranges `?g` over the
named graphs, and §18.6's definition is a join: `P` is evaluated with the
graph active **and `?g` as `P` would see it without the `GRAPH`** — unbound,
unless the incoming solution binds it — and each of its solutions is then
joined with `?g` = the graph's name. A `?g` that `P` itself binds, in an
`OPTIONAL` or a sub-`SELECT` (`sparql10/graph`'s `graph-optional`,
`sparql11/aggregates`' `agg-empty-group-count-graph`, `sparql12`'s
`graph-variable-scope`), must be compatible with the name, not pre-bound to
it: pre-binding changes which `OPTIONAL` branches match and what a grouped
count sees.

When `P` does not mention `?g` and every solution of `P` binds it through a
scan (every branch holds a non-empty `Bgp`), the scans themselves bind it —
`Match` over `AnyNamed`, restricted to the `FROM NAMED` set when there is one
— which is the same join done by the index. Otherwise `P` is evaluated once
per named graph, the graphs being the `FROM NAMED` list or, without one, the
distinct graph names of one `AnyNamed` scan, taken once per execution.

### 6.12 `Service` — ADR 0055, Federated Query §3.2

The handler is called with the endpoint, the node, and the incoming
solutions; its solutions are internalised (§4.1) and joined with the
incoming. A failure without `SILENT` fails the query naming the endpoint;
with `SILENT` the node evaluates to Ω0, one empty solution (§2.3). `SERVICE
?v` is invoked once per distinct IRI `?v` has.

### 6.13 `EXISTS` and `NOT EXISTS` — §17.4.1.4, §18.6

`exists(P)` over μ is true when `P` evaluated **given μ** (substitution, §6)
yields a solution. The known difficulties of `substitute` — a variable of μ
that `P` binds inside a `MINUS` or a sub-`SELECT` — are resolved as §4.4
resolves scope for sub-`SELECT`s, and `Minus` substitutes into its left
operand only. The suite's `exists` and `negation` cases hold under this
reading.

### 6.14 `Group` and aggregates — ADR 0053

As that ADR decides: hash grouping by term equality of the key values,
accumulators per aggregate, errors per its table, `DISTINCT` as a set per
accumulator, no spilling.

## 7. Expressions — §17

### 7.1 Errors — §17.2

An expression evaluates to a term, a value, or an **error**. Every function
and operator that is not a functional form is an error when an argument is;
`||` and `&&` follow §17.2's truth table; `BOUND`, `IF`, `COALESCE`, `IN`,
`NOT IN`, `EXISTS` are functional forms with their own rules (§17.4.1).
An error in a `FILTER` drops the solution; in `BIND` or a `SELECT`
expression it leaves the variable unbound; in `ORDER BY` it sorts as unbound;
in a group key it groups with other errors (ADR 0053).

### 7.2 Values, and when a term is externalised

A value is obtained from a term **only when an operator needs it**, and by the
cheapest route ADR 0050 allows (§10): the inline accessor first for a source
handle, then externalisation and `Varve.Xsd`'s parse of the lexical form. A
numeric or boolean result of an expression is carried as a value and becomes
a term (in canonical form, `xsd.md` §4) only when it is bound, compared as a
term, or returned. A lexical form outside its datatype's value space (or
outside `Varve.Xsd`'s precision policy, ADR 0051) has no value: operators that
need one produce an error, and `sameTerm` and term equality still work.

### 7.3 Effective boolean value — §17.2.2

`xsd:boolean` by value; numerics false when zero or `NaN`; simple literals
and `xsd:string` false when empty; anything else an error — **a boolean or
numeric literal whose lexical form is invalid for its datatype included**.
SPARQL 1.1 §17.2.2 made such a literal false; SPARQL 1.2 makes it an error,
and `sparql12/expression`'s `not-not` case tests the difference (`!!"z"^^xsd:boolean`
has no value, so the solution's `BIND` leaves its variable unbound). The 1.2
rule is applied to 1.0 and 1.1 queries too: no 1.0 or 1.1 case depends on
the old one, and one evaluator with two EBVs would answer the same query two
ways by its `VERSION`.

### 7.4 Operators — §17.3

Numerics compare and compute through `XsdNumeric` with XPath promotion
(integer → decimal → float → double; integer division is decimal); simple
literals and `xsd:string` compare by code point (`XsdString`); booleans with
`false < true`; `xsd:dateTime` **by the implicit-timezone total order** (ADR
0051, `XsdDateTime.Compare` with `ImplicitTimezoneOffsetMinutes`).

`xsd:date`, `xsd:time` and the `g` types — `gYear`, `gYearMonth`, `gMonth`,
`gMonthDay`, `gDay` — compare, when both operands have the same type, **by
XSD's partial order** (`CompareXsd`): a timezoned and an untimezoned value
closer than fourteen hours are incomparable, and incomparable is a type
error. This is the mapping ADR 0051 left to 5b "as the evaluation suite
decides", and the suite decides it: over `data-3.ttl`,
`sparql10/open-world`'s `date-1` (`FILTER(?v = "2006-08-23"^^xsd:date)`)
expects only `:d1`, not `:d2` (`"2006-08-23Z"`) or `:d3`
(`"2006-08-23+00:00"`), and `date-2` (the same with `!=`) expects neither of
them either. Under the implicit-timezone order with the UTC default,
`"2006-08-23"` equals both, so `date-1` would include them and `date-2`
exclude them only by accident of the default; under the partial order the
comparisons are indeterminate, an error, and the filter drops them in both. No case of
the suites compares `xsd:dateTime` values across the fourteen-hour window,
so the dateTime order is not contradicted (§13.1). The two ordered duration
types compare by value; `xsd:duration` by its partial order, an incomparable
pair being a type error. Each of these is §17.3.1's operator extensibility,
as Oxigraph provides.

`=` and `!=` are value equality where the table above applies, and
`RDFterm-equal` (§17.4.1.7) otherwise: equal when the terms are the same,
**a type error** when both are literals that are not the same term and no
operator applies (for example two literals of an unknown datatype, or a
literal whose lexical form is ill-typed for a known one), false otherwise.
Language-tagged strings are equal only as terms.

### 7.5 The order of `ORDER BY` — §15.1

The ascending order of two values is:

1. unbound (and error) lowest, then blank nodes, then IRIs, then literals,
   then triple terms (1.2 §15.1);
2. blank nodes by label, IRIs by code point;
3. two literals by `<` of §7.4 when it orders them; otherwise — incomparable
   types, equal values with different terms, or `NaN` — by lexical form in
   code-point order, then by datatype IRI, then by language tag.

Rule 3's fallback is §17.3.1's latitude to add mappings of `<` for ordering,
and it is Oxigraph's order (checked against pyoxigraph 0.5.11 with a mixed
column): `"1"^^my:t` before `1.5` before `"10:00:00"^^xsd:time` before `2`.
**`NaN` has no numeric place**, so it sorts by its lexical form among the
literals it is compared with, which puts it after every number whose lexical
form begins with a digit or sign — `xsd.md` §5.1's open item, decided here.

### 7.6 The function library — §17.4

Every function of §17.4 and the SPARQL 1.2 draft's additions that the algebra
carries (`BuiltInFunction`, `sparql-algebra.md` §2.4), implemented with the
section's definition and no other:

| Group | Functions | Section | Notes |
|---|---|---|---|
| Functional forms | `BOUND`, `IF`, `COALESCE`, `IN`, `NOT IN`, `EXISTS`, `\|\|`, `&&` | §17.4.1 | `IN` is `=` over the list with §17.2's error rule |
| Term tests | `sameTerm`, `isIRI`, `isBlank`, `isLiteral`, `isNumeric` | §17.4.1.8, §17.4.2.1–4 | `isNumeric` is true only of a valid lexical form |
| Accessors | `STR`, `LANG`, `DATATYPE`, `IRI`/`URI`, `BNODE`, `STRDT`, `STRLANG`, `UUID`, `STRUUID` | §17.4.2.5–13 | `IRI` resolves against the query's base (RFC 3986 §5) through `Varve.Iri`; `BNODE(s)` is one node per string per solution |
| Strings | `STRLEN`, `SUBSTR`, `UCASE`, `LCASE`, `STRSTARTS`, `STRENDS`, `CONTAINS`, `STRBEFORE`, `STRAFTER`, `ENCODE_FOR_URI`, `CONCAT`, `langMatches`, `REGEX`, `REPLACE` | §17.4.3 | Lengths and positions count code points; §17.4.3.1.2's argument compatibility; §17.4.3.1.3's result type; case mapping is Unicode's, culture-invariant |
| Numerics | `ABS`, `ROUND`, `CEIL`, `FLOOR`, `RAND` | §17.4.4 | `ROUND` rounds half up (F&O `fn:round`) |
| Dates | `NOW`, `YEAR`, `MONTH`, `DAY`, `HOURS`, `MINUTES`, `SECONDS`, `TIMEZONE`, `TZ` | §17.4.5 | over `xsd:dateTime`; `TIMEZONE` of a value without one is an error, `TZ` the empty string |
| Hashes | `MD5`, `SHA1`, `SHA256`, `SHA384`, `SHA512` | §17.4.6 | lowercase hex of the UTF-8 bytes; `MD5` is the package's own RFC 1321 implementation (§12.4), the SHA family the BCL's |
| 1.2 language | `LANGDIR`, `hasLANG`, `hasLANGDIR`, `STRLANGDIR` | 1.2 §17.4.2.9–11, §17.4.2.17 | |
| 1.2 triple terms | `isTRIPLE`, `TRIPLE`, `SUBJECT`, `PREDICATE`, `OBJECT` | 1.2 §17.4.2, §17.4.6 | `TRIPLE` refuses a literal subject or a non-IRI predicate |

`REGEX` and `REPLACE` translate XPath's regular-expression syntax and flags
(F&O §5.6.1: `s`, `m`, `i`, `x`, `q`) to .NET's, culture-invariant, without
the `Compiled` option (it is ignored under Native AOT and in the browser);
a constant pattern is compiled once per execution. A pattern .NET rejects, a
pattern that matches the empty string in `REPLACE` (F&O §5.6.3's
`FORX0003`), and a match that exceeds `RegexTimeout` are errors.

### 7.7 Casts — §17.5

`xsd:boolean`, `xsd:double`, `xsd:float`, `xsd:decimal`, `xsd:integer`,
`xsd:dateTime`, `xsd:string` as XPath constructor functions, by §17.5's table
of allowed source types: a cast from a string is a parse of its lexical form
(an invalid one an error), a cast between numerics converts by value
(`XsdInteger.TryFromDecimal` truncates, a double's `NaN` or infinity to an
integer or decimal is an error), a boolean to a numeric is `1` or `0`, a
numeric to a boolean is false for zero and `NaN`. An IRI casts only to
`xsd:string`; a blank node to nothing. The derived integer types cast as
`xsd:integer` with their range checked.

### 7.8 The clock and randomness — ADR 0056

`NOW()` is read from `Clock` once, when the execution starts, and carries the
provider's local offset as its timezone. `RAND()` is 53 bits of `Randomness`
over 2⁵³. `UUID()` and `STRUUID()` take 16 bytes and set RFC 4122 §4.4's
version (4) and variant bits.

### 7.9 `BNODE`, and blank nodes the evaluator mints

A minted blank node is a local term with a label of the form `b<n>` unique in
the execution; it never equals a blank node of the source (§4.1). `BNODE(s)`
returns the same node for the same `s` within one solution and different
nodes across solutions (§17.4.2.9).

### 7.10 Extension functions — ADR 0056

An IRI in function position that is an XSD cast of §7.7 is the cast; one that
is a key of `Functions` calls it with the evaluated arguments as `RdfTerm`s;
anything else is an error (§17.2.1). Extension functions are never folded or
moved by the optimiser.

## 8. The optimiser — ADR 0048

A pass from `Query` to `Query` that changes no answer. It runs over the
normalised tree (§5.1) with the source at hand, because its estimates are the
source's (ADR 0049). Its rewrites run in a fixed order: constants are folded
(§8.4) so that a folded condition can be placed; trivial joins go (§8.5) so
that adjacent groups merge; filters are placed (§8.1) before joins are
ordered (§8.3) so that a filter travels with its operand; and triple patterns
are ordered last (§8.2). Each counts how often it fired (§8.6).

### 8.1 Filter placement

A `Filter`'s condition is split into its `&&` conjuncts (sound, because a
solution passes `a && b` exactly when it passes both, §17.2's truth table),
and each conjunct is moved down as far as it soundly can:

- through `Union` into both branches, through `Extend` when the conjunct
  does not read the extended variable, and through another `Filter` —
  **always**: each passes every solution of its input through, one to one or
  not at all, without changing what the conjunct reads;
- into an operand of `Join`, the **left** operand of `LeftJoin` and of
  `Minus` (never the right), and `Graph`'s pattern — **only when that operand
  certainly binds** every variable the conjunct reads: then each output
  solution agrees, on those variables, with the operand's solution it came
  from;
- never through `Project`, `Group`, `Slice`, `Distinct`, `Reduced`,
  `OrderBy`, `Values` or `Service`.

"Certainly binds" is the static analysis: a `Bgp` or path binds its
variables; `Join` the union of both; `LeftJoin`, `Minus` and `Filter` their
left or inner; `Union` the intersection; `Extend` its inner plus its variable
only when its expression cannot error — so, conservatively, never; `Project`
the intersection with its list; `Group` its key variables. A conjunct reading
a variable that is only *possibly* bound stays where it was: that is what
keeps §18.2.2.7's scoping intact, since moving a filter to a place where the
variable is unbound changes an error into a different error or a true.

A conjunct that contains `EXISTS`, an aggregate, `BOUND`, `COALESCE`,
`IF`, a call to `RAND`, `NOW`, `UUID`, `STRUUID` or `BNODE`, or an extension
function, is not moved. (An XSD constructor is a cast, not an extension
function: the compiler takes it first.)

### 8.2 Triple order within a `Bgp`

Greedy by estimate (ADR 0049), each pattern's estimate being the source's
for its constants with every other position a wildcard: the first pattern is
the one with the smallest estimate; each next is the smallest among those
sharing a variable or blank node with the patterns placed, or, when none
does, the smallest of all. An estimate the source reports unknown sorts
after every known one; a pattern with a constant the source cannot
internalise estimates zero, since it matches nothing; ties keep the written
order. A `Bgp`'s solutions are a set of matches (§18.3), the same in any
order. Estimates are asked once per pattern per query.

### 8.3 Join order

A chain of `Join` nodes whose every operand is a `Bgp`, a `PathPattern`, a
`Values`, or a `Graph` or `Filter` over one of those, is flattened and rebuilt
left-deep in the order §8.2's rule gives its operands, sharing meaning a
certainly bound variable (§8.1), and the estimate of an operand being its
smallest pattern's (a `Values`, its row count; a closure or negated property
set, unknown). Join is associative and commutative over multisets (§18.5), so
the answer is the same. A chain with any other operand — a `LeftJoin`,
`Minus`, `Extend`, sub-`SELECT`, `Group` or `Service` — is left as written.

### 8.4 Constant folding

An expression with no variable, no `EXISTS`, no non-deterministic call and no
extension function is evaluated once, with the evaluator's own expression
code, and replaced by its value's term — **only when it evaluates without
error**, because the algebra has no node for an error.

### 8.5 Trivial joins

`Join(Bgp(), P)` and `Join(P, Bgp())` become `P`; two adjacent `Bgp`s in a
`Join` become one; a `Values` with one empty row joined with `P` becomes `P`.

### 8.6 The equivalence property

For generated queries and datasets, the normalised query and the optimised
one give **the same solution multiset** (for `ASK`, the same boolean). The
generator writes SPARQL text — triple patterns, closures and negated property
sets, `FILTER` with pure and impure conditions and constant-foldable parts,
`BIND`, `VALUES` (with `UNDEF`, and the identity `VALUES () { () }`),
`OPTIONAL`, `UNION`, `MINUS`, `GRAPH` by IRI and by variable, nested groups,
sub-`SELECT`s, `FILTER EXISTS`, and `SELECT`, `SELECT DISTINCT`, grouped
`SELECT` with `HAVING`, and `ASK` forms — over a vocabulary small enough that
joins meet: five IRIs, four integers, a string and two blank nodes, three
predicates, two named graphs. It writes no `ORDER BY` or `LIMIT`: the
optimiser does not cross them, so they add no coverage, and they would make
the comparison one of tie groups and sub-multisets.

Each rewrite of §8.1–§8.5 counts how often it fired, and the run fails if any
count is zero, or if fewer than two in five generated queries have a
non-empty answer — what keeps the property from passing vacuously (ADR 0043's
rule for generators). A counterexample is a defect in a rewrite or in an
operator whose answer depends on the order it is asked in; the report names
which.

## 9. Query forms — §16

### 9.1 `CONSTRUCT` — §16.2

For each solution, the template's triples are instantiated: a variable by its
value, a blank node by a node minted for this solution (§16.2.1), a constant
as itself. A triple with an unbound position, a literal subject, or a
predicate that is not an IRI is left out (§16.2). The output is a set: a
triple produced twice is returned once.

### 9.2 `ASK` — §16.3

True if the pattern has a solution; the first one ends the evaluation.

### 9.3 `DESCRIBE` — §16.4, informative

§16.4.3 leaves the description to the implementation. **This one is
minimal:** for each described resource — each IRI written, and each IRI or
blank node the pattern's solutions bind to a described variable — the
triples of the default graph with that resource as subject, and, closed over
blank nodes, the triples with each blank-node object as subject. The
**concise bounded description** (reification statements, the inverse
direction) and any symmetric variant are **out of scope** and stated so.

## 10. ADR 0050's arms — `ValueAccess`

| Arm | A numeric or boolean operand is obtained by |
|---|---|
| `InlineAccessor` | `TryGetInlineValue`, externalising only when it answers false |
| `Externalise` | `TryExternalise` and a parse of the lexical form, always |
| `Materialise` | Every term a scan binds is externalised **at the scan**, and expressions read the owned term |

**The materialised arm still joins on handles.** Compatibility and hashing in
§4.2 compare handles through the source's comparer in every arm, so the arm
measures the cost of externalising every bound term and not the cost of
comparing owned terms in joins — which a term-based contract would also pay.
Its cost is therefore **a lower bound** on the contract ADR 0022 rejected,
and ADR 0050's verdict rule is read with that in mind.

## 11. Allocation

Constraint 5 makes allocation per quad a defect. For a `SELECT` whose
pattern is a `Bgp` (no expression, no `DISTINCT`, no `ORDER BY`):

- **per solution**, one slot array of `8 × (w + ⌈w/64⌉)` bytes plus the
  array header, where `w` is the query's slot count, and — for a pattern after
  the first — the store's cursor for the next scan and one iterator per
  operator per input solution; the test states the measured constant;
- **per quad scanned and not matched: nothing.**

Both are asserted as milestone 4 asserted the store's scan: the difference
between two runs of different sizes, divided by the difference in solutions,
against a stated bound, and zero for quads that do not match. The measured
constant, over `InMemoryDataset` and over the store's pinned view alike, is
**48 bytes per solution** for a two-slot query — the row, `ulong[3]`, and
nothing else — and **0 bytes** per quad scanned and not matched.

A `SELECT`'s own `Project`, when nothing above it but a `Slice` compares
whole rows, is compiled away: the results read the projected columns only,
so restricting the row would cost a second row per solution and change
nothing anyone reads. A `Project` under `DISTINCT` or `REDUCED`, and every
sub-`SELECT`'s, restricts as §18.5 says.

## 12. Tests, and the gate

### 12.1 The W3C evaluation suites

The `QueryEvaluationTest` entries of the SPARQL 1.0 evaluation directories,
of `sparql11/aggregates`, `bind`, `bindings`, `cast`, `construct`,
`csv-tsv-res`, `exists`, `functions`, `grouping`, `json-res`, `negation`,
`project-expression`, `property-path`, `subquery` and `service`, and of the
SPARQL 1.2 directories whose data parses without RDF 1.2 Turtle, **run over
two subjects**: `InMemoryDataset`, and the store's default projection through
a pinned view. Both are in the ratchet, one line per case per subject.

- **Loading.** Each `qt:data` file is one load; each `qt:graphData` file is
  one load into the named graph of its IRI; a query's `FROM` and `FROM NAMED`
  files are loaded as named graphs of their IRIs. Separate loads keep blank
  nodes distinct across files, as RDF merge requires.
- **Comparison, by result form.** `SELECT` results as multisets of solutions
  with a bijection between blank nodes, in order when the query has
  `ORDER BY` (compared as a sequence of tie groups); `mf:LaxCardinality` as
  sets. Literals compare as terms, with two exceptions: a CSV result
  compares lexical forms, which is all CSV carries; and two numeric or two
  boolean literals **of the same datatype** compare by value, because the
  suites' cast cases write expected results in lexical forms other than the
  canonical one a processor returns. `CONSTRUCT` and `DESCRIBE` results by graph
  isomorphism. `ASK` by value.
- **The RDF/XML files** of `sparql10/sort` and `sparql11/subquery` are read
  from N-Triples translations generated by dotNetRDF, with a SHA-256 guard
  on each original (ADR 0027, dated note).
- **`SERVICE`** is served by a test handler that evaluates the pattern over
  the manifest's `qt:serviceData` with this evaluator (ADR 0055).
- **The fixed world.** A fixed clock and a seeded random source; the
  `functions` cases that test `NOW`, `RAND`, `UUID` and `STRUUID` test
  properties of the result, not its value.
- **Not wired**: `sparql11/entailment` (entailment regimes are out of scope)
  and the SPARQL 1.2 cases whose data is RDF 1.2 Turtle or TriG, pinned as
  blocked in the guard until the slice that brings RDF 1.2 Turtle (§13.3).

### 12.2 Properties

- **Optimiser equivalence**, §8.6.
- **Store against dataset.** For generated commit histories with retractions,
  and a position P: evaluation over the store's as-of view at P and over an
  `InMemoryDataset` loaded from that view's quads give the same solutions,
  compared as terms with a blank node bijection. This is the property that
  ties the evaluator to the storage thesis. The dataset is loaded from the
  view's own externalised terms, so a blank node carries the store's label
  into both answers and the bijection is the identity; the histories are one
  to eight commits of two to twelve assertions and retractions each, and the
  queries §8.6's generator's.
- Both run a stated number of iterations — **20,000** for the optimiser,
  **2,000** for the store — and the report gives every counterexample found.

### 12.3 Allocation — §11

### 12.4 `MD5`

The package's MD5 (RFC 1321) is tested against the seven test vectors of RFC
1321 §A.5, and against `System.Security.Cryptography.MD5` on generated inputs
where that is supported (not in the browser, which is why it exists).

### 12.5 Native AOT and the browser

The AOT smoke app and the browser smoke app load a small Turtle file and run
a `Bgp`, an aggregate and a property path query, printing the results.

## 13. Findings

Recorded at the close of 5b; each is a statement about this implementation
against the pinned suites.

### 13.1 The dateTime order

**No case disagrees with ADR 0051's implicit-timezone total order for
`xsd:dateTime`**, over both subjects: the order was applied, the suites run,
and nothing changed for dateTime. `sparql10/open-world`'s `date-1` and
`date-2` compare **`xsd:date`** values and require the XSD partial order
(§7.4); ADR 0051 left `xsd:date` and the other seven-property types for 5b to
map "as the evaluation suite decides", and they are mapped to the partial
order. This is recorded as a finding, not a change to ADR 0051: its dateTime
decision stands as written.

### 13.2 ADR 0050's benchmark

*Recorded when measured.*

### 13.3 What is blocked

The SPARQL 1.2 evaluation cases whose data is RDF 1.2 Turtle or TriG are
blocked on `turtle.md` §9, and are unblocked by the roadmap's slice that
brings RDF 1.2 Turtle together with RDF/XML and JSON-LD, before milestone 7
(6b). There are **41**: 37 of `eval-triple-terms`' 38 (its `expr-2` reads
`empty.nq` and runs) and 4 of `lang-basedir`'s 10. `EvaluationGuardTests`
pins the count and prints each case with the error its data raises.

## 14. Open questions

None owned by this document at its writing; §13 records findings.
