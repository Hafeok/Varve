# 0171 — Which shapes are incrementally maintainable: footprints, anchors and three classes

## Status

**Accepted — filed unaccepted by SHACL, part 2a (#138, under milestone 8,
#13), 2026-10-20** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the part 2 plan (#13, 2026-10-20). It
answers the question open since milestone 2, and the second ADR #13's
Decisions section expects.

## Context

The brief: "computing the affected focus-node set from a delta is
straightforward for Core property shapes and hard for property paths,
`sh:not`, recursion, and SPARQL-based constraints. Define which shapes are
incrementally maintainable and what falls back to scoped or full
re-validation." Commit-time gating (ADR 0169) and the validation projection
(part 2b) both validate only the focus nodes a commit's delta affects; a full
validation per commit would make write latency O(dataset).

shacl-rs (`Hafeok/rust-shacl` at `c0c4873`), read for this ADR, has no
incremental validation: no delta input, no affected-node computation, no
retraction handling, no analysis of SPARQL dependencies. What follows is
derived from SHACL 1.2 Core (Working Draft of 14 October 2026, as pinned by
ADR 0149) and SHACL 1.2 SPARQL-Related Features.

## Decision

### 1. The rule: footprints and anchors

For a shape s and a node n, the **footprint** F(s, n) is the set of triples of
the data graph that the validation of n against s (§6.6, with each
component's validator in §7) may read, including the validations of other
pairs it asks about — `sh:node`, `sh:property`, the logical components and the
rest of §7.7–§7.8, transitively, through recursion too.

A commit's delta δ = (A, R) (ADR 0010: R ⊆ G, A ∩ G = ∅) **affects** the pair
(s, n) when F(s, n) in the post-state meets A, when F(s, n) in the pre-state
meets R, or when n entered or left the targets of s (§3.1.3).

A pair's results are a function of its footprint, so **every pair that is not
affected keeps its results**, and re-validating the affected pairs over the
post-state gives the post-state's report. Re-validation is a full evaluation
of the pair, recursion included (ADR 0150's greatest fixpoint, recomputed for
the pairs it reaches); only the *finding* of affected pairs is incremental.

Affected pairs are found from each changed triple (x p y) by **anchors**:
reverse traversal from the triple to the nodes whose footprint holds it.
**Assertions are traversed in the post-state and retractions in the
pre-state**, because a retracted triple's path exists only before the commit
and an asserted one only after.

**Over-approximation is sound.** A node found that is not truly affected is
re-validated to the results it already had. That is what lets SPARQL be
analysed by its join graph (point 4) and every rule below err wide.

### 2. Affected sets, per shape, as a least fixpoint

For each shape s, Aff(s) is a set of nodes of the data graph, the least
solution of:

- **Aff(s) ⊇ Local(s, δ)**, the anchors of s's own reads: its path's triples,
  and what its components read at its value nodes (the table below);
- **Aff(s) ⊇ Rev_s(Aff(s′))** for every reference s → s′ (ADR 0150's dependency
  graph, every polarity), where Rev_s(V) is the nodes from which s's value
  nodes include a node of V (§6.9), in either state. For a node shape, whose
  value node is its focus node, Rev_s is the identity. Through `sh:memberShape`
  it is the list's reverse closure first (§7.5), and through
  `sh:reifierShape` the reifier's `rdf:reifies` triples (§7.8.5).

The equations are monotone in the sets and the sets are finite, so the
solution is the least fixpoint, computed by `Varve.Derivation`'s propagation
(ADR 0172) along the dependency graph's strata; a strongly connected
component iterates until no set grows. **Polarity does not matter here**: a
conformance can flip either way, and a flip either way can change a result.

The affected pairs are then, for each shape s that can have targets (any shape
with a target declaration, and every IRI shape, since `n sh:shape s` in the
data targets it, §3.1.3.7):

- the nodes of Aff(s) ∪ Tgt(s, δ), where Tgt(s, δ) are the nodes the delta
  may have moved into or out of s's targets;
- each validated in the pre-state if it is a target there, and in the
  post-state if it is a target there.

### 3. The classes

- **Incremental**: anchors are found by a bounded local rule — a fixed number
  of index lookups per changed triple, independent of the data's size.
- **Scoped**: anchors are found by a closure whose size depends on the data
  (a reverse closure, a recursive component, a SPARQL join path).
- **Full**: the shape's anchors cannot be bounded; every target of the shape
  is affected by every delta that touches the data graph.

A shape's class is the worst of its targets', its path's, its components', and
those of the shapes it reaches in the dependency graph.

| Construct (SHACL 1.2 Core) | Class | Anchors of a changed triple (x p y) |
|---|---|---|
| `sh:targetNode` (§3.1.3.1) | incremental | none: the targets do not depend on the data |
| `sh:targetSubjectsOf`, `sh:targetObjectsOf` (§3.1.3.4–5) | incremental | Tgt: x (or y) when p is the predicate |
| `sh:targetClass`, implicit class targets (§3.1.3.2–3), on `rdf:type` | incremental | Tgt: x when p is `rdf:type` |
| … on `rdfs:subClassOf` in the data graph (§1.1) | scoped | Tgt: every node typed by a class in the subclass closure below x, in either state. Under `subClassOfInShapesGraph` (§6.4) a subclass triple of the shapes graph is a shapes change (ADR 0169, R5). |
| explicit shape targets, `sh:shape` (§3.1.3.7) | incremental | Tgt: x when p is `sh:shape` |
| `sh:targetWhere` (§3.1.3.6) | the class of the shape it names | Tgt: Aff(where-shape), and x and y, which a triple may add to or remove from the graph's nodes |
| predicate, inverse, sequence, alternative and zero-or-one paths (§4.1–4.4, 4.7) | incremental | Local: for each step labelled p, the nodes that reach the step's tail by the path's prefix — reverse-evaluated from x (forward step) or y (inverse step) |
| `sh:zeroOrMorePath`, `sh:oneOrMorePath` (§4.5–4.6) | scoped | as above, the prefix reverse-evaluated through the closure |
| `sh:datatype`, `sh:nodeKind` (§7.1.2–3); value ranges (§7.3); lengths, `sh:pattern`, `sh:singleLine`, `sh:languageIn` (§7.4.1–5); `sh:hasValue`, `sh:in` (§7.9.2–3) | incremental | the path's anchors only: each reads the value term and nothing of the data |
| `sh:minCount`, `sh:maxCount` (§7.2), `sh:uniqueLang` (§7.4.6) | incremental | the path's anchors |
| `sh:class` (§7.1.1), on `rdf:type` | incremental | Local: x as a value node, then Rev_s |
| … on `rdfs:subClassOf` in the data graph | scoped | every node typed by a class in the closure below x, as a value node, then Rev_s |
| `sh:equals`, `sh:disjoint`, `sh:subsetOf`, `sh:lessThan`, `sh:lessThanOrEquals` (§7.6) | the class of the second path | the anchors of both paths |
| `sh:closed`, `sh:ignoredProperties` (§7.9.1) | incremental | Local: x as a value node, for any p. `sh:ByTypes` reads x's types too: incremental on `rdf:type`, scoped on `rdfs:subClassOf` |
| `sh:node`, `sh:property`, `sh:not`, `sh:and`, `sh:or`, `sh:xone`, `sh:someValue`, `sh:qualifiedValueShape` and `sh:qualifiedValueShapesDisjoint` (§7.7, §7.8.1–4) | the class of the shapes referenced | Rev_s(Aff(s′)) |
| `sh:reifierShape`, `sh:reificationRequired` (§7.8.5) | incremental | Local: when p is `rdf:reifies` and y is a triple term (a b c), the node a; Rev through the reifiers for Aff(reifier shape) |
| `sh:memberShape`, `sh:minListLength`, `sh:maxListLength`, `sh:uniqueMembers` (§7.5) | scoped | Local: when p is `rdf:first` or `rdf:rest`, the list heads that reach x by reverse `rdf:rest*`, as value nodes |
| `sh:rootClass` (§7.9.4) | scoped | Local: when p is `rdfs:subClassOf`, the classes below x by reverse `rdfs:subClassOf*`, as value nodes |
| `sh:uniqueValuesFor` (§7.9.5) | incremental | Local: when p is one of its properties, x, and every node whose key — its values for the properties — is x's key in either state, found from x's first non-empty property's values by reverse lookup; and for each node of Tgt(s, δ), the nodes sharing its key, since the count of a key is over the shape's targets; then Rev_s |
| a recursive strongly connected component (§6.6.3, ADR 0150) | scoped | the component's sets iterate to their least fixpoint (point 2) |
| a constant value node, `sh:values` or `sh:defaultValue` (§3.3, §5.1–5.2), with a component that reads the data at it or a reference | full | a change at the constant affects every focus node |
| a SPARQL-based constraint or component (SHACL-SPARQL §§3–4), analysable (point 4) | scoped | its join paths from the changed triple to `$this` |
| … not analysable, and deterministic | **full** | every target of its shape |
| … using `NOW()`, `RAND()`, `UUID()` or `STRUUID()`, or `BNODE()` whose value can reach a result | refused (ADR 0169) | — |
| `sh:severity`, `sh:message`, `sh:deactivated` (§3.1.4–6) | static | none: the shapes graph's, not the data's |

### 4. SPARQL-based constraints, by their join graph

`Varve.Shacl.Sparql` analyses each SPARQL-based constraint's algebra, as it
already parses it for pre-binding (ADR 0148):

- **The join graph** has the query's variables and constants as vertices, and
  an edge for each triple pattern and property path, wherever it stands —
  under `OPTIONAL`, `MINUS`, `FILTER EXISTS`, `FILTER NOT EXISTS`, a `GRAPH`
  block, or a sub-`SELECT`.
- **A query is analysable** when every edge is connected to `$this` (`$value`
  for a validator of a SPARQL-based component with ASK) through the join graph.
- **The anchors of a changed triple** are, for each pattern it matches, the
  nodes reached from its matched ends back to `$this` along a join path,
  evaluated with filters dropped, which over-approximates (point 1).
- **Not analysable, so full**: a pattern unconnected to `$this`; a sub-`SELECT`
  that does not project a variable joined to `$this`; an aggregate whose group
  is not joined to it; a path the reverse rule does not cover.

### 5. The classification is a value of the shapes graph

`ShapesGraph.Classify()` returns, for every shape, its class and the construct
and section that decided it — the worst one, and a full shape's every reason —
computed from the shapes graph alone, so it cannot change with the data. A
binding is classified when it is applied, and the classification is served
with it (ADR 0169).

## Alternatives considered

- **Full re-validation of every commit.** Exact and simple. Rejected: write
  latency proportional to the dataset is what the brief's tension names.
- **Re-validate the nodes the delta mentions, and nothing else.** Wrong:
  `sh:node` and paths make a node's validity depend on triples about other
  nodes, so the changed triple's subject is not the affected focus node.
- **Track each validation's actual reads**, and invert them. Exact, but it
  records a footprint per pair for every pair of the dataset — memory
  proportional to the report's work, held for ever by the projection. The
  static rules above need nothing per pair.
- **Polarity-aware propagation** — propagate only flips that can change the
  referencing result. Tighter for `sh:not`. Rejected for now: a flip's
  direction is known only after re-validating the referenced pair, which costs
  what it would save; the rule above is sound and simple, and the property
  holds it.
- **Class `sh:uniqueValuesFor` as scoped**, since its constraint state counts
  every target's key. The reverse lookup from a key's first value finds the
  nodes sharing it with a fixed number of lookups per value, so it stays
  incremental.

## Consequences

- The incremental report is exactly a full validation's, at every position,
  for every construct classed incremental or scoped. A property in
  `Varve.Shacl.Tests` holds it over generated histories with retractions and
  shapes changes; the pull request reports the iteration count.
- `reject` bindings need the classification at apply time (ADR 0169), and
  `varve:allowFullEvaluation` states the cost of a full shape.
- REC 4 (ADR 0141) uses no full construct: its SPARQL constraints are
  analysable and scoped (the pull request reports them).

## Checks

- **Checked against the accepted ADRs** (0001–0157, and #134's 0163–0168).
  Touches **0010** (the delta's exactness, which the anchors rely on),
  **0017** (validation reads the overlay and the delta, and here the pinned
  state as the overlay's base), **0150** (the dependency graph and its strata),
  **0151** (constants are looked up), **0152** (paths, now evaluated through
  `Varve.Derivation`, ADR 0172). No conflict.
- **Layer ownership.** `Varve.Shacl` (3) holds SHACL's footprints and the
  classification; `Varve.Shacl.Sparql` (4) the SPARQL analysis;
  `Varve.Derivation` (2) the generic propagation and path anchors.
- **Analyzer rule.** None new. What runs per changed quad and per affected
  node is `[HotPath]`, held by `VARVE0003`.
- **Open questions owned.** None. The brief's "which shapes are incrementally
  maintainable" is closed.
