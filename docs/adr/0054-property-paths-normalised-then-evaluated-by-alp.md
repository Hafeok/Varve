# 0054 — Property paths: normalised to joins and unions, closures by `ALP`

## Status

**Accepted.** 2026-09-25. Decided by the maintainer on the milestone 5b plan,
and written here before the evaluator. Closes `sparql-algebra.md` open
question 1 (the sequence-path rewrite) and settles item 2 of what milestone 5a
left to 5b.

## Context

The parser translates a path pattern by the first two rows of SPARQL 1.1
Query §18.2.2.4 only: a single IRI becomes a triple pattern and `^iri`
becomes a swapped one. Everything else stays a `PathPattern` node, because
the third row — `x seq(P, Q) y` into two patterns joined through a fresh
variable — needs a variable the author did not write, and the parser invents
none (`sparql-algebra.md` §3.3, §4.4).

§18.4 defines what every path means. Sequence, alternative and inverse are
defined *in terms of* the algebra — `seq` is `Project(Join(…), Var(X, Y))`
through a fresh variable, `alt` is `Union`, `inv` swaps the endpoints — and
so they carry **multiset** semantics: `?x (:p|:p) ?y` has two solutions per
matching triple, and `:p/:q` one per intermediate node. The three closures
are defined by the function `ALP`, whose visited set makes their results
**sets** of nodes. A negated property set is a scan with a filter on the
predicate.

The choice is where each definition is carried out: as rewrites into the
operators the evaluator already has, or as path operators of its own.

## Decision

**A normalisation pass, in `Varve.Sparql.Evaluation`, always applied, before
the optimiser.** It is a rewriter (`AlgebraRewriter`, ADR 0048) and not part
of the optimiser, because the optimiser may be skipped (ADR 0048) and the
evaluator's operators then still must not see a `seq`, `alt` or `inv` at the
top of a path.

- **What it rewrites**, recursively and only at the top of a `PathPattern` —
  never inside a closure, whose argument is a path expression and not a
  pattern:

  | Written | Normalised |
  |---|---|
  | `Path(X, link(p), Y)` | the triple pattern `X p Y`, as a one-triple `Bgp` |
  | `Path(X, inv(P), Y)` | `Path(Y, P, X)` |
  | `Path(X, seq(P, Q), Y)` | `Join(Path(X, P, V), Path(V, Q, Y))`, `V` fresh |
  | `Path(X, alt(P, Q), Y)` | `Union(Path(X, P, Y), Path(X, Q, Y))` |

  and anything else — the three closures and a negated property set — stays
  a `PathPattern`. `inv` pushed onto a closure becomes the closure with its
  endpoints swapped, `Path(Y, P*, X)`, which is §18.4's own
  `eval(Path(vx, ZeroOrMorePath(path), y)) = eval(Path(y,
  ZeroOrMorePath(inv(path)), vx))` read backwards.
- **Recursion is on the result**, so `(:p/:q)|^:r` normalises all the way
  down. Issue 226 of the 1.2 draft — whether the §18.3.2.5 rewrite recurses —
  has no bearing, because the rewrite here is justified by §18.4's semantics,
  not by the translation table, and §18.4 recurses by construction.
- **Fresh variables are named outside `VARNAME`** (grammar production `[166]`
  in 1.1, `[187]` in the 1.2 draft), which cannot begin with `.`: the pass
  names them `.p0`, `.p1`, …, per query. No author's variable can collide
  with one, the serialiser would refuse to write one (it is not in the
  parser's image, `sparql-algebra.md` §6), and `SELECT *` never projects one
  because `Project` is resolved at parse time. `Var(X, Y)`'s projection is
  therefore implicit: the variable is never projected by anything.
- **Closures are evaluated by §18.4's `ALP`**, one reachability search per
  start node with a visited set per start, so `ZeroOrMorePath` and
  `OneOrMorePath` yield sets of nodes as §18.4 says; `ZeroOrOnePath` yields
  the start and its one-step successors, deduplicated.
  - **One end bound** (a term, or a variable the incoming solution binds):
    `ALP` from that end, over the path or its inverse.
  - **Both ends bound**: the same search, stopping when the other end is
    reached.
  - **Both ends unbound**: every node of the active graph is a start —
    `nodes(G)`, the subjects and objects of its triples, as §18.4 defines
    it — and the same search runs from each. `?x P* ?x` keeps the pairs whose
    ends agree.
  - **Zero length from a term not in the graph**: `ALP(x, path)` contains
    `x` whatever the graph holds, so `<a> :p* ?y` binds `?y` to `<a>` even
    when `<a>` appears in no triple. That is §18.4's text; Oxigraph 0.5.11
    returns no solution here, and the suite's property-path cases decide
    whose reading holds (the evaluation specification records the outcome).
  - The step inside a closure is any path expression: a link is one scan, a
    sequence is its steps composed, an alternative is both, an inverse swaps
    direction, a negated property set is a filtered scan, and a nested
    closure is a nested `ALP`.
- **A negated property set is a filtered scan**: `!(:p|:q)` scans the active
  graph with the subject and object as bound as the solution makes them and
  keeps triples whose predicate is not in the set; `!(^:p)` scans in the
  other direction; a set with both halves is the union of the two.
- **The active graph** is whatever the enclosing `GRAPH` makes it, including
  a graph variable not yet bound, in which case a closure runs once per named
  graph, because §18.4 evaluates every path within one active graph and a
  reachability search must not cross from one graph into another.

## Alternatives considered

- **Path operators for all eight forms**, evaluated by §18.4's definitions
  directly, with no rewrite. One operator family, one place to look.
  Rejected: sequence and alternative *are* join and union in §18.4, and
  giving them their own operators would give them their own join and union
  implementations to keep in step with the real ones — and would hide them
  from the optimiser's join reordering and filter placement, which is where
  a long sequence path gains most.
- **The rewrite in the parser.** Rejected by `sparql-algebra.md` §3.3: the
  parser invents no variables, and a rewritten tree would not round-trip.
- **The rewrite as one of the optimiser's rules.** Rejected: skipping the
  optimiser would then change which operators the evaluator must implement,
  and the equivalence property (optimised against unoptimised) would compare
  two evaluators rather than two plans.
- **Set semantics for every path**, as Oxigraph evaluates them (0.5.11
  returns one solution for `?x (:p|:p) ?y`). Rejected: §18.4 defines `alt` as
  `Union` and `seq` as `Join`, both multiset operators, and Oxigraph is the
  tie-breaker only where the text is silent (ADR 0038, D1).
- **Fresh variables with a reserved prefix inside `VARNAME`**, such as
  `?__p0`. Rejected: any name inside the grammar is a name an author can
  write.

## Consequences

- **The evaluator's operators never see `seq`, `alt` or `inv` at the top of
  a path**, with or without the optimiser, and the optimiser sees the joins
  and unions a path became — so `?s :a/:b/:c ?o` with `?o` constant is
  reordered to start from the constant end like any other join chain.
- **Paths return multisets where §18.4 says so**, which differs from Oxigraph
  on alternatives with overlapping branches and on sequences with several
  intermediate nodes. The differential run reports each such case rather than
  counting it as a Varve defect.
- **`?x P* ?y` with both ends unbound costs a full scan of the active graph
  for its node set**, then one search per node. That is §18.4's definition;
  the cost is the query's.

## Checks

- **Checked against the accepted ADRs** (0001–0053) and the specification.
  Touches **0048** (a rewriter in the evaluation package; the optimiser stays
  skippable), `sparql-algebra.md` §3.3 and §4.4 (the parser is unchanged, and
  its open question 1 is closed here), and **0053** (the same rule for names
  the author cannot write). No conflict with any.
- **Layer ownership.** `Varve.Sparql.Evaluation`, **layer 3**.
- **Analyzer rule.** None.
- **Open questions owned.** None.
