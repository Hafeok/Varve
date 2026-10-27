# 0193 — RDF, RDFS and D entailment by query rewriting

## Status

**Accepted — filed unaccepted by Inference, part 1 (#170, under #95),
2026-10-27** (ADR 0066). Decided by the maintainer on the plan in #95
(2026-10-27), question 3, and the answer to D5 that moved the RDF regime into
this part. Acceptance is the maintainer's act on the pull request.

## Context

SPARQL 1.1 Entailment Regimes defines a regime's answers to a basic graph
pattern (BGP) as conditions over what the active graph entails. The terms used
throughout this ADR:
- **The scoping graph** (SG) is the graph the regime evaluates against.
- **A possible solution** is a binding μ, together with a mapping σ of the
  BGP's blank nodes, such that the instantiated BGP is entailed.
- **C1** is the condition that the instantiated BGP, Skolemised, is entailed
  by the Skolemised scoping graph. In practice: a blank node in an answer is
  one of the scoping graph's.
- **C2** is the condition that each variable binds to a term that occurs in
  the scoping graph or in the regime's vocabulary, less `rdf:_n` (§2, §4,
  §5).
- **Well-formedness:** a solution must instantiate the BGP to well-formed RDF
  (§3.3), so a variable in subject position never binds a literal.
- **Multiplicity:** the multiplicity of μ is the number of distinct σ.
- **Property paths** are evaluated without entailment (§10).
- **Updates** are left to the implementation (§11).

What the specifications leave to us:
- **Selection.** No protocol parameter selects a regime. ADR 0192 adds one.
- **Where the ontology lives.** Entailment is from the active graph only, and
  SPARQL cannot merge into a named graph (§9). The regimes are silent on an
  ontology held outside the active graph.

Varve keeps every term as written (`sparql-evaluation.md` §13). That changes
nothing for RDF and RDFS, whose datatypes, `xsd:string` and `rdf:langString`,
have no other spelling of one value. It matters for D (§5.1), which recognises
datatypes by value.

The RDFS entailment rules (RDF 1.1 Semantics §9.2.1, and the complete
procedure of its Appendix A) have a property this ADR rests on. Call a
predicate a **schema predicate** if it is `rdfs:subClassOf`,
`rdfs:subPropertyOf`, `rdfs:domain` or `rdfs:range`, or a sub-property of one
by `rdfs:subPropertyOf*`. Then **every rule has at most one premise whose
predicate is not a schema predicate**:
- rdfs2, 3, 7 and 9 have one schema premise and one other;
- rdfs5 and 11 have two schema premises;
- the rest have one premise.

So every entailed triple that is not about the schema is the image of **one**
asserted triple under a finite chain of steps, each guided by the schema.

**The change against the plan.** The plan in #95 (question 3a) read the
schema by a few predicate-bound probes. The first build of this part
scanned instead: to catch meta-modelling, it read every triple whose shape
could act on the schema, which is every triple of the scoping graph, and
rewriting one query over 100,000 people took 2.9 s. The maintainer ruled
that out on review. What ships is the plan's design, as point 2 states it:
the schema by probes to a fixpoint, C2 by memoised probes, classes and
container membership properties by seeking the index, and the schema cached
per graph version. The scanning build is kept as the test oracle of the
probes (point 9).

## Decision

### 1. The ontology is merged into every graph a regime reads

- **The merged graph.** Under a regime other than simple, the scoping graph of
  each BGP is its active graph **merged with the TBox graphs** of the binding
  that covers it (ADR 0194). The default graph of a `FROM`-merged dataset is
  the merge of the `FROM` graphs and the TBox graphs.
- **The answers.** They are exactly the regime's answers for the same query
  with the TBox added by `FROM` to each active graph. For a named graph under
  `GRAPH`, merging into it is Varve's extension of §9, which the operator
  guide states.
- **`TBoxMerge`.** The merge is a view of the request's quad source. A `Match`
  of a graph also matches the TBox graphs, with each TBox quad reported in the
  matched graph.
  - A `Match` over every named graph reports each TBox quad once per named
    graph of the dataset. The named graphs are enumerated once per request.
  - A quad that is in a graph and in a TBox graph is seen twice by the view.
    Multiplicity is restored by point 6's `Distinct`.
  - The view reads through the caller's scoped source, so it reaches only
    what the caller reads, and the TBox graphs are readable by every reader of
    the dataset (ADR 0190).
- **A named graph the caller's view holds nothing of** is not merged: it
  is no graph of the dataset as the caller reads it, so `GRAPH <g>` over a
  graph the caller does not read, or that does not exist, answers nothing
  under any regime. The default graph always exists, and is merged with the
  TBox of the binding that names it, which every reader of the dataset
  reads (ADR 0190).
- **The pinned position.** The view is over the pinned view, so the TBox is
  read as of the request's position. An as-of read rewrites against the
  ontology as it was then.

### 2. The schema is saturated per scoping graph

**The schema Σ of a scoping graph** is computed when the query is rewritten,
by bound probes through the merge, never by a scan of the data:
1. **The schema predicates.** Probe each schema predicate, starting from the
   four, by a predicate-bound `Match`. A new `rdfs:subPropertyOf` triple can
   make a new schema predicate: close, and probe again, until no new schema
   predicate appears. A sub-property of `rdf:type` is typing data, which the
   rewriting answers (point 3), not schema.
2. **The regime's axiomatic triples.** For RDFS that is RDF 1.1 Semantics'
   rdfsV less `rdf:_n`, and its `rdf:_n` axioms only for the `rdf:_n` that
   Σ's triples or the query name. Every other `rdf:_n` meets the same axioms
   through the container-membership tables (point 5).
3. **Meta-modelling, by the same probes.** Under RDFS:
   - the triples typed, by `rdf:type` or a sub-property of it, into a meta
     class: `rdfs:ContainerMembershipProperty`, `rdfs:Datatype`, and any
     class Σ makes a subclass of one (rdfs12 and rdfs13 turn them into
     schema triples);
   - the triples under a property whose domain or range is a meta class, or
     under a sub-property of one;
   - the triples whose subject or object is a term of the RDF and RDFS
     vocabulary, read once, by subject and by object.
4. **Close** the result under the RDFS rules with the procedure of Appendix
   A, and repeat from 1 until no probe reads anything new.

**Every term Σ names is a parameter of the tables**: a known predicate and a
known class. A triple whose predicate or class is one meets what Σ says of
it; a type-like triple whose class is an `rdf:_n` meets that term's axioms.

**The one fallback that reads the graph whole.** A Σ that says something of
the vocabulary itself — a term of the RDF or RDFS vocabulary, other than an
`rdf:_n`, as the subject of a schema or typing triple, such as `rdf:type
rdfs:subPropertyOf ex:p` or `rdfs:member rdfs:domain ex:C` — makes any
triple a possible schema triple, and no bounded probe finds them. Its
scoping graph is read whole, and the result is correct at the cost of
materialising it. It is the one O(data) read of the schema, and it is
cached like any other (below).

**The cache.** A saturated Σ is kept across requests, in a bounded cache
that drops the least recently used first. Its key is the regime's rules, the
`rdf:_n` the query names, and each graph the scoping graph merges, its own
and its binding's TBox graphs, by name and version (ADR 0125). A merged
active graph keys on the set of its graphs' versions.
- **The versions** come from the source (`IGraphVersionSource`). The store's
  view names them as of its position, and refuses a graph a commit after an
  as-of view's position has touched. A source that names none saturates per
  request.
- **The caller's scope.** The scoped source reports a graph the caller cannot
  read as a graph with no quad, which is what the caller reads of it, so two
  callers never share a Σ built from what one of them cannot see.
- **One cache per dataset.** The server keeps one with each dataset's
  bindings, so a change to the bindings starts a new one. The benchmark
  reports its hits and misses.

**Which graphs need their own Σ.** A query rewrites each BGP once for each
distinct Σ it can meet:
- **The default graph, or one named graph, or a `FROM`-merged graph:** one Σ.
- **`GRAPH ?g`:** the named graphs with schema of their own are found by
  the same probes over every named graph, unmerged: every probe the
  saturation of their binding's TBox made, to its fixpoint. A graph none of
  those probes matches adds nothing to any, so its own saturation reaches
  its TBox's Σ. Each gets its own Σ and a branch `VALUES ?g { … }` joined with
  `GRAPH ?g { P′ }`. Every other named graph shares the Σ of its binding's
  TBox, in one such branch per binding that lists the graphs it covers, and
  the graphs no binding covers share the axioms' Σ in one more. The named
  graphs are enumerated once per request for it, as the merge enumerates
  them.
- A schema triple inside a data graph therefore acts on that graph alone.

### 3. Each pattern is rewritten into the asserted patterns that entail it

**The compilation.**
- Σ is compiled into tables by running the **same forward-chaining closure**
  over Σ plus one *representative* asserted triple per *shape*:
  - one shape per schema-known predicate;
  - one shape per schema-known class, as the object of an `rdf:type`-like
    predicate;
  - one generic predicate and one generic class, for all the rest.
- A representative's derived triples, read off by role, are the tables. A
  role is the subject, the object or the predicate of the asserted triple, or
  a constant.
- By the property in the context, the derived triples of an asserted triple
  are exactly its shape's, with the roles substituted.

**The rewritten pattern.** A triple pattern becomes the union of:
- **Σ's triples that match it**, as constants;
- **for each shape whose image can match it,** the asserted pattern of that
  shape with its predicate (and, for a class shape, its object) constrained by
  a `VALUES` table, and its result positions mapped by role;
- **facts that exist only if some asserted triple of a shape exists**, such
  as a class being an `rdfs:Class` because something has it as a type, or a
  predicate an `rdf:Property` because some triple has it. Where such a fact
  reads one role of the shape's triple only, a predicate or a class, and the
  scoping graph is exactly the graphs evaluated, the role's distinct terms
  are sought at rewrite time and emitted as a `VALUES` table: the
  predicate-first index is sought to the next distinct key, `(g, p+1)` or
  `(g, p, o+1)`, one seek per distinct term, merged across the store's runs,
  inside an extent set's window, and over an as-of view's log tail. Otherwise,
  and for a group of graphs under `GRAPH ?g` that shares one rewriting, the
  fact is guarded by an existence subquery over the shape (`Slice` of a
  projection), so it is not emitted for a graph where the shape is absent.

**Variables in every position** take their values from the tables, so a
variable predicate, class or property ranges over what the scoping graph
entails.

**No term is invented.** The rewriting adds no blank node, and every constant
it emits is in the scoping graph or in the regime's vocabulary (C1 and C2).

**Pruning.** A triple pattern's union keeps only branches that can add an
answer, decided when the query is rewritten:
- **A condition on a constant is decided.** A family's condition on the
  asserted triple (an `rdf:_n` predicate, subject, object or class, or a
  literal object) is false or true once the term it tests is a constant of
  the query or of the table. False drops the branch, which would otherwise
  read every triple of its pattern to find nothing. True drops the filter.
- **A table of one row is folded** into its branch, its constants put into
  the asserted triple and the outputs. Two branches that then differ in
  nothing are one, and the second is dropped: under point 6's `Distinct` it
  adds no row. The literal family's table is not folded, since it is a join
  on the datatype the branch binds.
- **A branch no axiom reaches is never built.** The tables hold only what Σ
  derives, so a domain or range branch for a class no `rdfs:domain` or
  `rdfs:range` reaches has no row and no branch.

On the benchmark's data, `?x a ex:Student` falls from six branches, three of
which read the same N rows and three of which read N rows to keep none, to
one. A property holds the pruned rewriting to the unpruned one (point 9).

### 4. The answer conditions

- **Well-formedness.** A branch that would put a literal in the subject
  position of a variable is filtered out.
- **A blank node of the query** is not a variable of the answer. It may stand
  for rdfD1's surrogate of a literal, as GrdfD1 has it (Appendix A), so the
  literal filter is not applied to it. That is the only place a generalised
  triple affects an answer.
- **The range of σ, the specification's gap.** Read literally, §2 and §4 let
  σ map a query blank node to any term (RDF-T). C2 restricts only variables.
  So `SELECT * { _:b a rdfs:Resource }` on the empty graph has one solution
  of unbounded multiplicity, one per IRI. The regimes never discuss this, and
  SPARQL Query asks them to prevent trivial infinite answers (§1.3).
  **Varve restricts σ as C2 restricts μ:** a blank node of the query binds a
  term of the scoping graph or of the regime's vocabulary, less `rdf:_n`.
- **Terms the closure holds that the graph need not name.** These are an
  `rdf:_n` the query names (§3.4), and, under RDF and RDFS, `xsd:string`,
  which rdfs1 and GrdfD1 type. A variable or query blank node binds one of
  them only where the active graph names it. The rewriting asks the scoping
  graph once per candidate term, by three bound probes per graph (as subject,
  predicate and object) through the merge, memoised for the request, and
  keeps or drops the rows. Under `GRAPH ?g` a group of graphs shares one
  rewriting under its TBox, so a graph that names a candidate the TBox does
  not is rewritten on its own, where the probe is exact.
- **C2's vocabulary.**
  - RDFS uses rdfsV-Minus.
  - RDF uses rdfV-Minus, with rdfD2 and RDF's axioms only. The RDFS steps are
    off.
  - D uses dV-Minus, which is RDFS's plus the datatype IRIs of point 7's map.
- **Ground patterns.** A ground triple pattern needs no binding, so C2 does
  not restrict it, and `ASK { rdf:_7 a rdf:Property }` is true on any graph
  (§3.4).

### 5. What it is complete for, and what it costs

**The rewriting is complete for the RDFS regime of §4, the RDF regime of §2
and the D regime of §5.1**, each under point 4's conditions. Nothing in them
is refused. Item by item:

| Item | Answered | Cost |
|---|---|---|
| ρdf (`subClassOf`, `subPropertyOf`, `domain`, `range`, `type`), variables in every position | by the tables | one union per triple pattern, of at most one branch per shape kind |
| Axiomatic triples | as constants of Σ, by C2 | none at query time |
| Container membership | the `rdf:_n` of the scoping graph, its distinct predicates sought (point 3), when a pattern can bind a variable to one | one seek per distinct predicate |
| Classes, as `?c a rdfs:Class` or a variable class | the distinct objects of `rdf:type`, sought | one seek per distinct class |
| Reflexive `rdfs:subClassOf` and `rdfs:subPropertyOf` | for every class and property the scoping graph entails, including objects of `rdf:type` (axiom: its range is `rdfs:Class`) and every predicate (rdfD2) | an index walk |
| Meta-queries over the TBox (`?c rdfs:subClassOf ?d`, `ex:C ?rel ex:D`) | from Σ | none at query time |
| `?s ?p ?o`, `?x a rdfs:Resource` | complete, answer-sized | a scan: **the only shapes whose cost grows with the data** |
| Inconsistency | not checked: §4 allows answering over an RDFS-inconsistent graph, and C1 and C2 keep answers finite | none |

**What grows with the data.** Rewriting a query is bounded by the schema and
the query: probes, seeks per distinct term, and per-request memoised
existence probes. Evaluating it is bounded by its answers. Two query shapes
read the data whole, because their answers are the whole data: `?s ?p ?o`
and `?x a rdfs:Resource`, which every term of the graph answers. The schema's
one O(data) read is point 2's fallback for a schema that redefines the
vocabulary. The benchmarks state the same.

**The work limit.** A rewriting over a large Σ is bounded by the query's work
limit. A rewritten query whose tables exceed it is refused as
`read-limit-exceeded`, the read limit every query has (ADR 0114).

### 6. Multiplicity

A rewritten BGP is wrapped in `Distinct`, over its variables and its blank
nodes renamed to fresh variables, and then projected to its variables. So a μ
appears once per distinct σ, as §1.3 defines.

### 7. D: Varve.Xsd's datatype map, and literals by value

- **The datatype map** is Varve.Xsd's (`docs/spec/xsd.md`, ADR 0051's
  precision policy), and the operator guide documents it, as §5.1 requires.
- **Canonical literals.** Each datatype's canonical literal is Varve.Xsd's
  canonical form, its lexical form canonical and its datatype canonical.
- **Canonical datatypes.** The canonical datatype is `xsd:integer` for every
  integer, whether of an integer type or an integral `xsd:decimal`. It is
  `xsd:dateTime` for `xsd:dateTimeStamp`, and `xsd:duration` for its two
  subtypes. Otherwise it is the literal's own. An ill-typed literal, or one
  outside the map, is its own canonical form.
- **Literal constants match by value.** A literal constant in a BGP is
  compared with the asserted term's canonical form instead of being bound
  into the `Match`.
- **Literal answers are returned in canonical form,** since C2 requires them
  to be in Lit(SG), and joins between patterns compare canonical forms.
- **A literal is an instance of every recognised datatype whose value space
  holds its value** (RDF 1.1 Semantics §7): `"1"^^xsd:int` is an
  `xsd:integer`, an `xsd:decimal`, an `xsd:unsignedByte`, and so on. Under D
  the closure's GrdfD1 adds each such type, and the literal family's
  parameter is the type, tested on the literal's value.
- **Two extension functions.** The rewriting calls `varve:canonical` and
  `varve:hasValueType`. The evaluator is at the same layer and cannot be
  referenced, so the host registers `LiteralValues.Canonical` and
  `LiteralValues.HasValueType` under those IRIs. That is one adapter each,
  as every extension function is registered (`sparql-evaluation.md` §7.10).
  Only D needs them.
- **Only under `d`.** Simple, RDF and RDFS keep literals as written, so
  `sparql-evaluation.md` §13 stands.

### 8. Scope

- **Rewritten:** BGPs only. Property paths are not (§10).
- **Not rewritten:** update requests. Their `WHERE` is simple entailment, and
  a regime on an update request is `entailment-unsupported` (ADR 0192),
  documented as §11 asks.
- **Not checked:** consistency.
- **OWL 2 QL (PerfectRef)** is #95's part 3, under ADR 0209.

### 9. The equality property

**Rewriting equals materialisation:**
- **The inputs.** Generated datasets, with schemas inside the TBox and inside
  data graphs, and including meta-modelling (a sub-property of
  `rdfs:subClassOf`), container membership and literals; generated BGPs, with
  variables, constants and blank nodes in every position; and the three
  regimes.
- **The rewritten side:** the rewritten query evaluated over the merge.
- **The oracle:** a test-only forward-chaining closure (Appendix A) of each
  scoping graph, with point 4's conditions applied and the BGP evaluated
  under simple entailment.
- **What must be equal:** the two, as multisets.

The oracle is the compilation's forward chainer run on the whole graph rather
than on representatives, so the property tests the context's property and the
compilation together.

**Pruning changes no answer.** A property holds the rewriting with point 3's
pruning to the rewriting with every branch kept, as multisets, on generated
cases aimed at `rdf:_n` and literal constants, and the default-graph,
named-graph and `GRAPH ?g` properties compare the two on every case.

**The probes equal the scan.** A second property compares Σ built by point
2's probes with Σ built by reading the scoping graph whole (the first build,
kept in the tests), on generated TBoxes and data graphs with sub-properties of
the schema predicates, schema triples inside data graphs, chains through
`rdfs:subPropertyOf`, and `rdf:_n` in every position. The cases that take the
fallback are held below half, so the probes are what it tests.

## Alternatives considered

- **A view that answers every `Match` with entailed quads** (backward
  chaining at the quad source). It works, but it hides the regime from the
  algebra: `Distinct` per BGP, the C2 filters and the per-graph branches
  could not be stated as algebra, and the evaluator's optimiser would see
  cardinalities it cannot estimate.
- **Rewriting by property paths** (`rdf:type/rdfs:subClassOf*`). It is
  incomplete under meta-modelling, since a sub-property of `rdfs:subClassOf`
  is not on the path. It also leaves domain and range, rdfD2 and the axioms
  to hand-written special cases.
- **ρdf only.** This was the brief's expectation. The single-premise property
  makes the whole regime no harder, and a partial regime would need a problem
  for every query it cannot answer.
- **Materialising RDFS for tier 1.** That is tier 2's job (#171), and its
  `rdfs` profile is this property's second oracle there.
- **Refusing a variable that ranges over container membership properties.**
  It is answerable by a seek per distinct predicate, so it is answered.
- **Reading Σ by scanning for the shapes that act on the schema.** The first
  build did, and it costs a read of the scoping graph per query (the context's
  change against the plan). It is the probes' test oracle.

## Consequences

- **A query under a regime pays:**
  - Σ's saturation by probes, on the first query over a set of graph
    versions only; later ones read it from the cache;
  - the seeks for distinct predicates and classes its patterns need, and
    its C2 probes, memoised for the request;
  - the unions.

  The benchmarks report query latency under `rdfs` against `simple`, and the
  rewriting alone, cold and warm, at 10,000, 100,000 and 1,000,000 people
  over a fixed schema, with the cache's hits and misses.
- **`GRAPH ?g` over many named graphs** costs every probe of its TBoxes'
  saturation over every named graph, to find those with their own schema.
- **`sparql11/entailment` and `rdf-mt` are wired** (§12.1 of
  `sparql-evaluation.md`, amended): the RDF, RDFS and D cases on the
  rewriting subject. The RIF and OWL 2 DL-only cases are blocked as out of
  scope by #15's exclusion of OWL 2 DL, listed by name, and counted by
  `eng/status.cs`.

## Checks

- **Checked against the accepted ADRs** (0001–0178, 0190 and 0191). Touches:
  - **0050** and **0051**: D's values;
  - **0052**: the evaluator still never pins;
  - **0054**: normalisation runs after the rewriting;
  - **0095**: the pinned view;
  - **0125**: graph versions key the schema cache;
  - **0107** and **0190**: the merge reads through the scoped source;
  - **0114**: the read limit.

  No conflict.
- **Layer ownership.** `Varve.Sparql.Entailment` (3), all of it.
- **Analyzer rule.** None new. The tables are built per saturated schema,
  the pattern rewriting per BGP. `VARVE0003` checks the merge's `Match`, which runs per
  quad.
- **Open questions owned.** None.
