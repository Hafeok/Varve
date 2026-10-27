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
- **The pinned position.** The view is over the pinned view, so the TBox is
  read as of the request's position. An as-of read rewrites against the
  ontology as it was then.

### 2. The schema is saturated per scoping graph

**The schema Σ of a scoping graph** is computed when the query is rewritten:
1. Start from its triples whose predicate is a schema predicate. They are
   found by one predicate-bound `Match` per schema predicate, through the
   merge, to a fixpoint: a new `rdfs:subPropertyOf` triple can make a new
   schema predicate.
2. Add the regime's axiomatic triples. For RDFS that is RDF 1.1 Semantics'
   rdfsV less `rdf:_n`, and its `rdf:_n` axioms only for the `rdf:_n` that
   occur in the scoping graph (point 5).
3. Close the result under the RDFS rules with the procedure of Appendix A.

**Which graphs need their own Σ.** A query rewrites each BGP once for each
distinct Σ it can meet:
- **The default graph, or one named graph, or a `FROM`-merged graph:** one Σ.
- **`GRAPH ?g`:** the named graphs that hold a triple with a schema predicate
  of their own are found by the same `Match` over every named graph,
  unmerged. Each gets its own Σ and a branch `VALUES ?g { … }` joined with
  `GRAPH ?g { P′ }`. Every other named graph shares the TBox's Σ, in one
  branch that filters those graphs out.
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
  as a class being an `rdfs:Class` because something has it as a type. These
  are guarded by an existence subquery over that shape (`Slice` of a
  projection), so they are not emitted for a graph where the shape is absent.

**Variables in every position** take their values from the tables, so a
variable predicate, class or property ranges over what the scoping graph
entails.

**No term is invented.** The rewriting adds no blank node, and every constant
it emits is in the scoping graph or in the regime's vocabulary (C1 and C2).

### 4. The answer conditions

- **Well-formedness.** A branch that would put a literal in the subject
  position of a variable is filtered out.
- **A blank node of the query** is not a variable of the answer. It may stand
  for rdfD1's surrogate of a literal, as GrdfD1 has it (Appendix A), so the
  literal filter is not applied to it. That is the only place a generalised
  triple affects an answer.
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
| Container membership | the `rdf:_n` of the scoping graph, found by **one scan of the scoping graph** when a pattern can bind a variable to one | a scan, measured in the benchmarks |
| Reflexive `rdfs:subClassOf` and `rdfs:subPropertyOf` | for every class and property the scoping graph entails, including objects of `rdf:type` (axiom: its range is `rdfs:Class`) and every predicate (rdfD2) | an index walk |
| Meta-queries over the TBox (`?c rdfs:subClassOf ?d`, `ex:C ?rel ex:D`) | from Σ | none at query time |
| `?s ?p ?o`, `?x a rdfs:Resource` | complete, answer-sized | a scan |
| Inconsistency | not checked: §4 allows answering over an RDFS-inconsistent graph, and C1 and C2 keep answers finite | none |

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
- **Literal constants match by value.** A literal constant in a BGP becomes a
  variable plus a value comparison over a predicate-bound `Match`. It is
  fast when both literals are canonical and inline (ADR 0050).
- **Literal answers are returned in canonical form,** since C2 requires them
  to be in Lit(SG).
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
  It is answerable at the cost of a scan, so it is answered and the cost is
  stated.

## Consequences

- **A query under a regime pays:**
  - Σ's saturation, which is cached per (TBox graphs, graph versions) and
    merged with a scoping graph's own schema triples;
  - one `Match` per schema predicate;
  - the unions.

  The benchmarks report query latency under `rdfs` against `simple`, and the
  container-membership scan.
- **`GRAPH ?g` over many named graphs** costs one `Match` per schema predicate
  over every named graph, to find those with their own schema.
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
  - **0107** and **0190**: the merge reads through the scoped source;
  - **0114**: the read limit.

  No conflict.
- **Layer ownership.** `Varve.Sparql.Entailment` (3), all of it.
- **Analyzer rule.** None new. The tables are built per query, the pattern
  rewriting per BGP. `VARVE0003` checks the merge's `Match`, which runs per
  quad.
- **Open questions owned.** None.
