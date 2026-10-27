---
set: rdfs-rdf-and-d-by-rewriting
namespace: varve
adr: 0193
decisions:
  - key: TBoxMergedIntoEveryGraph
    statement: "Under a regime other than simple each basic graph pattern's scoping graph is its active graph merged with the TBox graphs of the binding that covers it, read through a view of the request's pinned, scoped source"
  - key: SchemaSaturatedPerScopingGraph
    statement: "The schema of a scoping graph, its triples under schema predicates found to a fixpoint and the regime's axiomatic triples, is closed by RDF 1.1 Semantics Appendix A when the query is rewritten, once per distinct schema a pattern can meet"
  - key: SchemaInADataGraphActsThere
    statement: "A named graph holding schema triples of its own is rewritten under its own schema, by a branch of its own under GRAPH ?g, and every other named graph shares the TBox's"
  - key: OneAssertedPremisePerEntailment
    statement: "Every RDFS rule has at most one premise outside the schema, so each pattern is rewritten into the union of the schema's matching triples and, per shape, the asserted pattern whose images can match it, through tables compiled by the forward chainer over one representative per shape"
  - key: ExistenceGuardedFacts
    statement: "A fact entailed only if some asserted triple of a shape exists is guarded by an existence subquery over that shape, so it is never emitted for a graph without it"
  - key: SchemaByProbes
    statement: "The schema is read by bound probes to a fixpoint, never by a scan of the data: the schema predicates and their sub-properties, typings into a meta class, triples under a property whose domain or range is one, and triples whose subject or object is a term of the RDF and RDFS vocabulary"
  - key: KnownTermsAreParameters
    statement: "Every term the schema names is a known predicate and a known class of the compiled tables, so a triple whose predicate or class is one meets what the schema says of it, and a type-like triple whose class is an rdf:_n meets its axioms"
  - key: DistinctTermsBySeek
    statement: "The distinct predicates of a graph and the distinct objects of a predicate in it are enumerated by seeking past each, one seek per distinct term, where the source can; a source that cannot is read whole"
  - key: ExistenceByMemoisedProbe
    statement: "C2's question whether the scoping graph names a term is answered by an existence probe at rewrite time, memoised per request, for the candidate terms the tables hold"
  - key: SchemaCachedPerGraphVersion
    statement: "A saturated schema is cached, bounded, keyed by the versions of the graphs its scoping graph merges"
  - key: SigmaRestrictedLikeMu
    statement: "A blank node of the query binds, as a variable does under C2, only a term of the scoping graph or of the regime's vocabulary less rdf:_n, where the specification leaves σ unbounded"
  - key: AnswerConditions
    statement: "Rewritten answers meet the regime's conditions: no invented blank node (C1), variables bound only to terms of the scoping graph or the regime's vocabulary less rdf:_n (C2), no literal in a variable's subject position, a query blank node free to stand for a literal's surrogate"
  - key: MultiplicityByDistinctSigma
    statement: "A rewritten basic graph pattern is Distinct over its variables and its renamed blank nodes, then projected to its variables, so a solution appears once per distinct blank-node mapping"
  - key: CompleteForTheThreeRegimes
    statement: "The rewriting is complete for the RDFS regime (section 4), the RDF regime (section 2) and the D regime (section 5.1), refuses nothing in them, finds container membership properties by one scan of the scoping graph when a pattern needs them, and checks no consistency"
  - key: DByValue
    statement: "Under d the datatype map is Varve.Xsd's, a literal constant matches by value, a literal answer is canonical, a literal is an instance of every recognised datatype whose value space holds its value, and the host registers varve:canonical and varve:hasValueType; under simple, rdf and rdfs literals stay as written"
  - key: BasicGraphPatternsOnly
    statement: "Only basic graph patterns are rewritten: property paths are evaluated without entailment and updates under simple entailment"
  - key: RewritingEqualsMaterialisation
    statement: "Over generated datasets, schemas, basic graph patterns and the three regimes, the rewritten query equals the simple evaluation over a forward-chained closure of each scoping graph with the answer conditions applied, as multisets"
---

The rulings of [ADR 0193](../adr/0193-rdfs-rdf-and-d-by-rewriting.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by Inference, part 1 (#170) (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
