# SPARQL algebra

Functional specification for the algebra tree in `Varve.Sparql` (layer 2): the
tree the parser produces, the translation from the grammar to it, the
serialiser back to text, and the rewrite surface an optimiser uses. The text
side — grammar, lexical rules, versions, positions — is
[`sparql-grammar.md`](sparql-grammar.md). Evaluation is not here: it is
`sparql-evaluation.md`, written at milestone 5b for `Varve.Sparql.Evaluation`
(layer 3, ADR 0048).

Status: Accepted. Changes only together with the ADR that motivates the change.

## 1. Normative references

- **SPARQL 1.1 Query Language**, 21 March 2013: §18.2 translation to the
  algebra (18.2.1 scope, 18.2.2 graph patterns, 18.2.4 groups and aggregates,
  18.2.5 modifiers), §18.3 basic graph patterns, §18.4 property paths.
- **SPARQL 1.2 Query Language**, Working Draft 21 September 2026: §18.2 the
  explicit algebraic syntax, §18.3 the same translation renumbered
  (18.3.2.1–18.3.2.9, 18.3.4, 18.3.5), §4.3 the expansion of reified triples
  and annotations, §16.2 `CONSTRUCT`, §19.6 blank nodes. **Section numbers
  below are the 1.2 draft's** unless prefixed "1.1".
- **SPARQL 1.1 Update**, 21 March 2013, and **SPARQL 1.2 Update**, Working
  Draft 12 June 2026: §3 the operations.
- **ADR 0048**: two packages, algebra in and algebra out, records for the
  nodes.

## 2. The tree

### 2.1 One tree, not two

The parser produces the algebra directly. There is no syntax tree that is then
translated, because every step of §18.3 is local enough to run while parsing —
the group translation of §18.3.2.7 is a fold over the elements of a `{ }` as
they are read — and a second tree would be a second allocation of everything
and a second set of node types to keep in step. Oxigraph's `spargebra` takes
the same shape. What is lost is the ability to ask "what did the author
write" when it differs from "what does the query mean"; §2.4 says what is
kept for the serialiser's sake.

### 2.2 Records, immutable, with value equality

Every node is a `sealed record` class (ADR 0048's stated exception to the
repository's avoidance of records). A node's children are held in
`AlgebraList<T>`, a small immutable sequence with element-wise equality — the
helper ADR 0048 mentions — because `ImmutableArray<T>` compares by reference
to its backing array and would make two identical trees unequal. So two trees
are equal when they have the same shape and the same leaves. `Variable` is a
`readonly record struct` over its name. Terms in the tree are owned
`RdfTerm`s (ADR 0024: an allocated tree may hold allocated terms).

**Source positions do not take part in equality.** Two trees that mean the
same thing are equal wherever their text came from, which is what the
round-trip property needs (§6).

### 2.3 Every node carries a `SourceSpan`

Start and end byte offsets, and the 1-based line and byte column of the start,
in the units of `sparql-grammar.md` §6. A node the translation invents — the
`Join` a group produces, the `Project` a `SELECT` produces, a fresh blank node
— carries the span of the syntax that caused it: the group's braces, the
`SELECT` keyword to the end of the modifiers, the `<< … >>` it expanded.

### 2.4 The node families

The names are the ones the public API uses (`PublicAPI.Unshipped.txt` is the
record of the exact shape). The algebraic operator each stands for is the 1.2
draft's §18.2 name, in the right column.

**Terms in patterns.** `PatternTerm`: `VariablePattern(Variable)`,
`TermPattern(RdfTerm)` for an IRI, a literal or a ground triple term,
`BlankNodePattern(string Label)`, and `TripleTermPattern(subject, predicate,
object)` for a triple term with a variable somewhere inside it (1.2). A
`TriplePattern` is three `PatternTerm`s; a `QuadPattern` adds an optional
graph, which in an update template may be a variable.

**Query patterns.** `QueryPattern` — the draft's "algebraic query expression"; not `QueryPattern`, which is `Varve.Rdf`'s match-pattern type and would be ambiguous in every evaluator file:

| Node | Operator | Notes |
|---|---|---|
| `Bgp(ImmutableArray<TriplePattern>)` | `BGP(…)` | May be empty: `{}` is `BGP()`, the identity for `Join` |
| `PathPattern(PatternTerm, PropertyPath, PatternTerm)` | `Path(x, ppe, y)` | |
| `Join(Left, Right)` | `Join` | |
| `LeftJoin(Left, Right, Expression? Condition)` | `LeftJoin` | `null` is `true` |
| `Filter(Expression, Inner)` | `Filter` | |
| `Union(Left, Right)` | `Union` | |
| `Graph(PatternTerm Name, Inner)` | `Graph` | Name is a variable or an IRI |
| `Extend(Inner, Variable, Expression)` | `Extend` | |
| `Minus(Left, Right)` | `Minus` | |
| `Values(ImmutableArray<Variable>, ImmutableArray<ImmutableArray<RdfTerm?>>)` | a multiset of solution mappings | `null` is `UNDEF` |
| `Service(PatternTerm Name, Inner, bool Silent)` | — | §5 |
| `Group(Inner, ImmutableArray<GroupKey>)` | `Group` | §4.6 |
| `OrderBy(Inner, ImmutableArray<OrderCondition>)` | `OrderBy` | `OrderCondition(Expression, bool Descending)` |
| `Project(Inner, ImmutableArray<Variable>)` | `Project` | |
| `Distinct(Inner)`, `Reduced(Inner)` | `Distinct`, `Reduced` | |
| `Slice(Inner, long Offset, long? Limit)` | `Slice` | |

`ToList` and `ToMultiset` are not nodes: a sub-`SELECT` is its own
`Project`/`Distinct`/… chain inline where the draft writes `ToMultiset(…)`, and
the modifier nodes are understood to operate on a sequence. Nothing an
optimiser or evaluator can do differs for having the conversions written down.

**Property paths.** `PropertyPath`: `PredicatePath(RdfTerm)` is `Link`;
`InversePath`, `SequencePath`, `AlternativePath`, `ZeroOrMorePath`,
`OneOrMorePath`, `ZeroOrOnePath` are `Inv`, `Seq`, `Alt` and the three
closures; `NegatedPropertySet(ImmutableArray<RdfTerm> Forward,
ImmutableArray<RdfTerm> Inverse)` is `NPS`, `Inv(NPS)` or the `Alt` of both,
in one node so that `!(:p|^:q)` stays one thing.

**Expressions.** `Expression`: `VariableExpression`, `ConstantExpression(RdfTerm)`,
`UnaryExpression(UnaryOperator, Expression)` for `!`, `+`, `-`;
`BinaryExpression(BinaryOperator, Left, Right)` for `||`, `&&`, the six
comparisons and the four arithmetic operators; `FunctionCall(BuiltInFunction,
ImmutableArray<Expression>)` for every keyword function of `[141]
BuiltInCall` including `BOUND`, `IF`, `COALESCE`, `IN` and `NOT IN` (the
tested expression first, then the list); `CustomFunctionCall(RdfTerm Iri,
ImmutableArray<Expression>)` for `[76] FunctionCall`, which is also how an
XSD constructor such as `xsd:integer(?x)` appears; `ExistsExpression(QueryPattern,
bool Negated)`; and `AggregateExpression` (§4.6). A `<<( … )>>` in an
expression with a variable inside is `FunctionCall(Triple, …)`; a ground one
is a `ConstantExpression` holding a triple term.

**Queries.** `Query(Prologue, DatasetSpec?, SourceSpan)` with four forms:
`SelectQuery(QueryPattern)`, whose pattern already ends in the modifier chain;
`ConstructQuery(ImmutableArray<TriplePattern> Template, QueryPattern)`;
`AskQuery(QueryPattern)`; `DescribeQuery(ImmutableArray<PatternTerm> Resources,
QueryPattern)`. `Prologue(RdfTerm? Base, ImmutableArray<PrefixDeclaration>,
SparqlVersion? Version)` keeps what was declared, for the serialiser; the
tree's terms are already expanded and resolved. `DatasetSpec(ImmutableArray<RdfTerm>
DefaultGraphs, ImmutableArray<RdfTerm> NamedGraphs)` is `FROM` / `FROM NAMED`,
and `USING` / `USING NAMED` in an update.

**Updates.** `Update(Prologue, ImmutableArray<UpdateOperation>)`, §5.

### 2.5 What the tree does not keep

The shape of the text: `WHERE` present or elided, `SELECT *` versus a list,
`OPTIONAL { P FILTER(F) }` versus `OPTIONAL { { P FILTER(F) } }` (which differ
in meaning and therefore in tree, §4.5), one `PREFIX` declaration used or
unused, a comment. The tree is the meaning; the serialiser writes one canonical
text for it (§6).

## 3. Blank nodes, fresh things, and the 1.2 expansions

### 3.1 Blank nodes stay in the tree

The 1.2 draft's §18.3.2.2 replaces every blank node in a group graph pattern
with a fresh variable and collects them in a set `BV`, so that the algebra is
free of blank nodes. This tree keeps them as `BlankNodePattern` nodes, for two
reasons.

The `CONSTRUCT` template and the `INSERT` template need blank nodes as
written: a template blank node is instantiated afresh per solution (§16.2.1,
Update §3.1.3), which a variable cannot express. And the round-trip property
(§6) needs the serialiser to write back what the parser read; a fresh variable
has no spelling that parses back to a fresh variable.

The meaning is the same. Basic graph pattern matching (§18.4) treats a blank
node in a pattern as a variable that is not part of the solution, and the
in-scope table of §18.3.1 does not list blank nodes, so `SELECT *` never
projects one — which is what `BV` exists to say. `Varve.Sparql.Evaluation`
treats a `BlankNodePattern` as a non-projectable variable scoped to the query,
and `sparql-evaluation.md` will say so in those words.

### 3.2 Which blank nodes are the same

Blank node identity is the label, scoped to the parse (grammar §3.6). A label
that recurs is the same node, across triples blocks, `GRAPH` bodies of an
update template, and between an `INSERT` template and its `WHERE` clause.
`ANON` and the expansions of §3.4 get generated labels.

### 3.3 Fresh variables

The translation of §18.3.2.5 introduces a fresh variable when it rewrites
`x Seq(p1, p2) y` into two patterns. This tree does not perform that rewrite
(§4.4), and no other step of the translation needs a variable the author did
not write. **The parser invents no variables.** An optimiser that wants the
sequence rewrite performs it with variables it can name for itself; the
serialiser never has to write one.

### 3.4 The 1.2 expansions — §4.3

Reified triples and annotations are shorthand, expanded by the parser exactly
as §4.3.1 and §4.3.2 prescribe, so that the tree never sees the shorthand:

- `<< s p o >>` as a subject or object stands for a fresh blank node `_:b`
  and adds the triple `_:b rdf:reifies <<( s p o )>>` to the enclosing
  triples block.
- `<< s p o ~ r >>` uses `r` (a variable, an IRI or a blank node) in place of
  the fresh node.
- `s p o ~ r1 ~ r2` adds one `rdf:reifies` triple per reifier; a bare `~`
  is a fresh blank node.
- `s p o {| q v |}` asserts `s p o`, and is `<< s p o >> q v` for the block;
  `s p o ~ r {| q v |}` is `<< s p o ~ r >> q v`. Each block not preceded by
  its own `~ r` gets its own fresh node.
- Nested `<< … >>` expand recursively, inside out.

The added triples belong to the same basic graph pattern as the triple they
annotate, in the order: the asserted triple, then per reifier its
`rdf:reifies` triple followed by that reifier's annotation triples. The
expansion is the same in a `CONSTRUCT` template and in update templates,
where `<< … >>` is allowed by `[81]`, `[86]` and `[113]`.

A triple term `<<( s p o )>>` is not shorthand and is not expanded: it is a
`TermPattern` when ground and a `TripleTermPattern` otherwise, in the
position it was written.

## 4. Translation — §18.3

The steps, in the draft's order, and where this tree departs.

### 4.1 Expand syntax forms — §18.3.2.1

Prefixed names and `a` become IRIs; `( … )` collections become `rdf:first` /
`rdf:rest` triples with fresh blank nodes, ending in `rdf:nil`; `[ … ]`
becomes a fresh blank node with its property list; numeric and boolean
literals become typed literals with the lexical form as written (`1.0` is
`"1.0"^^xsd:decimal`, `1e0` is `"1e0"^^xsd:double`, not canonicalised —
`Varve.Sparql` has no `Varve.Xsd`, ADR 0048); `"x"@en--ltr` becomes a
directional language-tagged string. The 1.2 expansions of §3.4 happen here.

### 4.2 Blank nodes — §18.3.2.2

Kept, §3.1.

### 4.3 Collect filters — §18.3.2.3

Every `FILTER` in a group is removed from the group's element sequence and
its expression added to `FS`, applied in §4.5 to the whole group. Several
filters form one conjunction, `&&`-nested left to right in the order written,
so that `FILTER(a) FILTER(b)` is `Filter(a && b, …)`.

### 4.4 Property paths — §18.3.2.4, §18.3.2.5

Path expressions translate by the table of §18.3.2.4 into the `PropertyPath`
nodes of §2.4. A negated property set with both forward and inverse members
is one `NegatedPropertySet` node rather than the draft's `Alt(NPS, Inv(NPS))`,
which is its meaning.

Path patterns translate by the first two rows of §18.3.2.5 only:

| Written | Tree |
|---|---|
| `x :p y` (a single IRI, or `a`) | the triple pattern `x :p y`, in the enclosing `Bgp` |
| `x ^:p y` | the triple pattern `y :p x`, in the enclosing `Bgp` |
| `x ppe y`, anything else | `PathPattern(x, ppe, y)` |

The third row, `x Seq(ppe1, ppe2) y` into two patterns with a fresh variable,
is **not applied**: it needs a variable the author did not write (§3.3), the
draft's own issue 226 notes the rule is underspecified as to recursion, and
`ppeval` of §18.5 gives `Path(x, Seq(p, q), y)` exactly the meaning the
rewrite would — a join through an unnamed variable. An optimiser may do it
(§7); the parser does not.

A `PathPattern` is a group element of its own, joined with its neighbours:
`?s :p ?a . ?s :q/:r ?b . ?s :t ?c` is
`Join(Join(BGP(?s :p ?a), Path(?s, Seq(:q, :r), ?b)), BGP(?s :t ?c))`. Triple
patterns on either side of a path do not merge into one `Bgp` across it,
because §18.3.2.6 collects *adjacent* triple patterns and a path is not one.

### 4.5 Groups — §18.3.2.6 to §18.3.2.9

A group `{ E1 E2 … }` translates as §18.3.2.7 writes it, starting from the
empty `Bgp()` and folding each element in order: `OPTIONAL { P }` gives
`LeftJoin`, `MINUS { P }` gives `Minus`, `BIND` gives `Extend`, and every
other element — a `Bgp` of adjacent triple patterns, a `PathPattern`, a
nested group, a `UNION`, `GRAPH`, `SERVICE`, `VALUES`, a sub-`SELECT` — gives
`Join(G, Translate(E))`. `UNION` chains are left-nested. Then `FS` is applied
as one `Filter` over the whole group.

**Simplification runs after the whole translation** (§18.3.2.9, the draft's
preferred reading): `Join(Bgp(), A)` and `Join(A, Bgp())` become `A`. This
ordering is what decides the doubly-nested filter the draft calls out:

| Written | Tree |
|---|---|
| `{ A OPTIONAL { B FILTER(F) } }` | `LeftJoin(A, B, F)` |
| `{ A OPTIONAL { { B FILTER(F) } } }` | `LeftJoin(A, Filter(F, B), null)` |

In the second, the optional body is a group whose one element is a group;
`Translate` of the body is `Join(Bgp(), Filter(F, B))`, which is not of the
form `Filter(…)` when the `OPTIONAL` rule looks at it, and only later
simplifies. The parser achieves the same by reading the `OPTIONAL` rule
syntactically: the condition is the conjunction of the body's own `FILTER`s,
and the body without them is the right operand.

A sub-`SELECT` in a group is its translated modifier chain (§4.7) as the
element; `ToMultiset` is implicit (§2.4).

`VALUES` in a group is a `Values` node as the element. A query's trailing
`VALUES` clause is `Join(P, Values)` applied after aggregation and `HAVING`
and before `SELECT` expressions (§18.3.4.3).

### 4.6 Aggregation — §18.3.4

The draft extracts every aggregate into an `Aggregation(…)` term, binds it to
an invented name, and joins them with `AggregateJoin`. This tree keeps the
aggregate **where it was written**, as an `AggregateExpression` inside the
`Extend`, `Filter` or `OrderBy` above a `Group` node:

- `Group(Inner, Keys)` is inserted when the level has `GROUP BY`, or has an
  aggregate anywhere in `SELECT`, `HAVING` or `ORDER BY` (implicit grouping,
  `Keys` empty). `GroupKey(Expression, Variable?)`: `GROUP BY ?v` is
  `(?v, ?v)`; `GROUP BY (expr AS ?v)` is `(expr, ?v)`; `GROUP BY (expr)` or
  a bare function call is `(expr, null)`, a key that is not in scope by name.
- `AggregateExpression(AggregateFunction, Expression? Argument, bool Distinct,
  string? Separator, RdfTerm? CustomIri)`: `COUNT`, `SUM`, `MIN`, `MAX`,
  `AVG`, `SAMPLE`, `GROUP_CONCAT` with its `SEPARATOR`, and a custom
  aggregate — an IRI called with `DISTINCT` or with an aggregate argument
  position — with `CustomIri` set. `Argument` is `null` for `COUNT(*)`.
- `HAVING` conditions become `Filter`s over the `Group`, in order.
- `SELECT (expr AS ?v)` items become `Extend`s in order, over the `Group`
  (or over the pattern when there is no grouping).
- `ORDER BY` with an aggregate becomes an `OrderBy` whose condition holds the
  aggregate.

The evaluator performs the extraction the draft describes: it collects every
`AggregateExpression` reachable from the nodes above a `Group` without
crossing another `Group` or a sub-`SELECT`, evaluates each once per group,
and substitutes. The draft's rule that an unaggregated variable in such an
expression is read as `SAMPLE(v)` is the evaluator's too; the parser enforces
instead the projection restriction of §11.4 and the scope rules of
`sparql-grammar.md` §4, which leave no unaggregated variable to sample except
a group key.

Why not the draft's shape: `AggregateJoin` needs names for the aggregates,
the names have to be written by the serialiser, and every name it could write
is one an author could also write. Keeping the expression where it was makes
the tree the parser's image exactly, at the cost of the evaluator doing the
collection — which it has to do in some form anyway.

### 4.7 Solution modifiers — §18.3.5

Applied innermost first in the draft's order, over the pattern `P` of §4.5
(after the aggregation, `HAVING`, trailing `VALUES` and `SELECT` expressions
of §4.6):

1. `OrderBy(P, conditions)` if `ORDER BY`;
2. `Project(…, PV)` for `SELECT`, where `PV` is the list of variables named, in
   order and without repetition, or for `SELECT *` the in-scope variables of
   the pattern (§18.3.1) in order of first appearance;
3. `Distinct` or `Reduced`;
4. `Slice(…, offset, limit)` if `LIMIT` or `OFFSET`: `Slice(M, offset, null)`,
   `Slice(M, 0, limit)`, `Slice(M, offset, limit)`.

`CONSTRUCT`, `ASK` and `DESCRIBE` take `ORDER BY`, `LIMIT` and `OFFSET` the
same way and take no `Project`, `Distinct` or `Reduced`. `SELECT *` is
resolved at parse time: the tree always carries an explicit `Project`, so
that two queries that project the same variables are the same tree however
they said so.

A `CONSTRUCT WHERE { … }` short form uses its template as its pattern; a
`DESCRIBE *` names the in-scope variables of its pattern; a `DESCRIBE` with no
`WHERE` has the empty `Bgp()` as its pattern.

## 5. Update

`Update(Prologue, ImmutableArray<UpdateOperation>)`; the operations of
Update §3, one node each, in request order:

| Node | Syntax |
|---|---|
| `InsertData(ImmutableArray<QuadPattern>)` | `INSERT DATA { }`; blank nodes allowed, variables not |
| `DeleteData(ImmutableArray<QuadPattern>)` | `DELETE DATA { }`; neither |
| `DeleteWhere(ImmutableArray<QuadPattern>)` | `DELETE WHERE { }`; variables allowed, blank nodes not |
| `Modify(RdfTerm? With, ImmutableArray<QuadPattern> Delete, ImmutableArray<QuadPattern> Insert, DatasetSpec? Using, QueryPattern Where)` | `WITH … DELETE { } INSERT { } USING … WHERE { }` |
| `Load(RdfTerm Source, RdfTerm? Graph, bool Silent)` | `LOAD` |
| `Clear(GraphTarget, bool Silent)`, `Drop(GraphTarget, bool Silent)` | `GraphTarget`: `Default`, `Named`, `All`, or `Graph(iri)` |
| `Create(RdfTerm Graph, bool Silent)` | |
| `Add`, `Move`, `Copy` `(GraphOrDefault From, GraphOrDefault To, bool Silent)` | `GraphOrDefault`: `Default` or `Graph(iri)` |

**`WITH` is kept, not expanded.** Update §3.1.3 explains `WITH <g>` as sugar
for a `GRAPH <g>` around each template and a `USING <g>` — but only when no
`USING` is present, and the dataset it implies is a matter for evaluation
(the `WHERE` runs against a dataset whose default graph is `<g>`, which is
not `Graph(<g>, P)`). The node records what was written; the executor at
layer 5 applies §3.1.3. The same holds for `USING`: a `DatasetSpec`, as
`FROM` is for a query.

The `WHERE` pattern of a `Modify` is translated exactly as a query's (§4),
including the group translation and the blank node rules of grammar §3.6.
The templates' `GRAPH ?g { }` may name a variable, which must be bound by
the `WHERE`; the parser does not check that, the executor does, because the
draft's syntax suite accepts such a request and its meaning is a solution
with the variable unbound producing no quad (Update §3.1.3).

**The update algebra is a record of the request.** Nothing here computes a
delta, chooses a graph, or knows a store: ADR 0005 puts execution in a layer 5
integration package that evaluates the `WHERE` against a pinned position and
submits one commit, and that package is not part of milestone 5a.

`SERVICE` in a query is a `Service` node with its `SILENT` flag; nothing here
implements federation, and an evaluator that does not is expected to refuse
the node with an error that names it.

## 6. Serialisation

`SparqlWriter` writes a `Query` or an `Update` as text that parses back to
the **identical** tree: for every tree `t` the parser can produce, and for
every tree in the parser's image that a generator produces,
`Parse(Write(t)) == t`, with record equality. This is the serialiser's whole
contract, and it is tested as a property (§8) rather than believed.

The text is canonical and not the author's:

- Prologue as recorded: `VERSION` if any, `BASE` if any, every `PREFIX` in
  order, whether or not used. Terms are written in full `<…>` form; prefixes
  are not reapplied, because reapplying them is a choice the round trip does
  not need and a source of bugs it does not want.
- `SELECT` always lists its variables; `SELECT *` is never written. `WHERE` is
  always written.
- Every `Join` operand is in its own `{ }`. Two adjacent `Bgp`s would merge
  on re-parse (§18.3.2.6), so `Join(Bgp₁, Bgp₂)` is `{ { … } { … } }`, and a
  `Bgp` is written without extra braces only where it is the sole content of
  a group. Left-nested `Join` chains flatten into one group, since a group
  folds left; left-nested `Union` chains flatten the same way.
- `Filter(F, A)` is `{ A' FILTER(F) }` with `A'` written as a group element,
  which for a nested `Filter` means nested braces: `Filter(F, Filter(G, A))`
  is `{ { { A } FILTER(G) } FILTER(F) }`.
- `LeftJoin(A, B, F)` is `{ A' OPTIONAL { B' FILTER(F) } }` and
  `LeftJoin(A, B, null)` is `{ A' OPTIONAL { B' } }`, where `B'` is `B` as a
  group element — braces of its own when `B` is a `Filter`, so that §4.5's
  two forms come back as themselves.
- `Extend(A, v, e)` is `{ A' BIND(e AS ?v) }`; `Minus`, `Graph`, `Service`,
  `Values` likewise as elements after `A'`.
- A `Group` and the modifier chain above it are written as one `SELECT`
  level: keys as `GROUP BY`, each `Filter` directly above it as `HAVING`,
  each `Extend` as a `(expr AS ?v)` item, `OrderBy`, `Project`, `Distinct` /
  `Reduced`, `LIMIT` / `OFFSET`. A modifier chain that is not the query's own
  is a sub-`SELECT` in braces.
- Numbers, strings and IRIs are written from the term's lexical form with the
  escapes of grammar §3.2 applied where the lexical form needs them, and a
  literal whose datatype is `xsd:integer`, `xsd:decimal`, `xsd:double` or
  `xsd:boolean` is written in short form only when its lexical form matches
  the corresponding terminal exactly; otherwise in `"…"^^<…>` form.
- Blank nodes are written with their labels; `[]` and the 1.2 shorthands are
  never written, so no label is generated on re-parse.
- 1.2 constructs are written in 1.2 syntax, and a tree containing one is
  written with `VERSION "1.2"` if its prologue does not already carry a
  version, so that the text says what it needs.

The parser's image is the set of trees the rules of §4 can produce. A
generator for the property test stays inside it: fresh variable per
`Extend`, projection lists over in-scope variables, aggregates only above a
`Group`, `Project` only as part of a modifier chain, and so on. A tree
outside the image — `Extend` of a variable already bound below it, a `Bgp`
that repeats a blank node label from a separate `Bgp` — has no text, and the
writer's obligation is to refuse it with an error that says which rule, not
to write text that parses to something else.

## 7. The rewrite surface

`AlgebraRewriter` is an abstract class with one virtual method per node type
(`RewriteBgp`, `RewriteJoin`, …, `RewriteExtend`; one per `PropertyPath` and
`Expression` node likewise), each receiving the node and returning a node of
the same family. The base implementation rebuilds a node only when a child
changed, and returns the same instance otherwise, so that an unchanged
subtree is shared and a rewriter that touches nothing allocates nothing.

Dispatch is by a type switch in the base class. No reflection, no visitor
interface, no dynamic: both compile to ordinary code under Native AOT and
the browser build, and the set of node types is closed (every family is a
sealed hierarchy), so the switch is exhaustive and a new node type is a
compile error in every rewriter until its method exists.

The optimiser in `Varve.Sparql.Evaluation` is a rewriter (ADR 0048: algebra
in, algebra out). Nothing in `Varve.Sparql` rewrites anything; the class
exists here because it is the algebra's, and a linter or a query editor at
layer 5 is as entitled to it as the optimiser is.

## 8. Tests, and the gate

- **The syntax suites** of `sparql-grammar.md` §1, ratcheted. A positive case
  passes when it parses; a negative case passes when it does not. No suite in
  this milestone checks the tree, because none can: the SPARQL test suites
  have no algebra oracle. The next three are what check it.
- **The round-trip property.** Generated trees, thousands per run, through
  `Write` then `Parse`, compared with record equality; the generator covers
  every node type of §2.4, every operator, every 1.2 construct, `Update` as
  well as `Query`, and the shapes §6 calls out as hazardous. Any
  counterexample is a bug in the writer or in the parser, and the report
  names which.
- **The corpus round trip.** Every positive case of every suite is parsed,
  written and parsed again, and the two trees must be equal. This holds the
  writer to the trees real queries produce, which a generator can only
  approximate.
- **The examples of §18.3.3.** Each of the draft's mapped graph pattern
  examples, and the doubly-nested filter pair of §4.5, is a unit test that
  builds the expected tree by hand and compares it with the parse.
- **The UTF-8 / UTF-16 agreement** and **the allocation assertion** of
  `sparql-grammar.md` §8.

## 9. Open questions

1. **The sequence-path rewrite** (§4.4) is left to an optimiser. If 5b finds
   that the evaluator wants it done before it sees the tree, it is a rewriter
   in `Varve.Sparql.Evaluation`, not a parser change. Owner: 5b.
2. **`BV` and `SELECT *`.** The draft's `BV` excludes blank-node variables
   from `SELECT *`; this tree has no such variables (§3.1), so the question is
   moot unless the draft's final text makes a blank node in a pattern
   projectable, which nothing suggests. Owner: `Varve.Sparql`, due when the
   1.2 draft reaches Candidate Recommendation.
3. **Custom aggregates** (§4.6): the parser accepts `iri(DISTINCT ?x)` and
   `iri(?x)` in aggregate position as a custom aggregate, per the draft's
   note. Whether an evaluator can be handed one, and how it would know the
   function, is 5b's. Owner: 5b.
