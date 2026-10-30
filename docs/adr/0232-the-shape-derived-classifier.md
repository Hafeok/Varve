# 0232 — The shape-derived classifier, in `Varve.Classification`

## Status

**Filed unaccepted by milestone 9, part 1 (#202, under #14), 2026-10-30**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
on the plan in #14 (2026-10-30), question 2. **Amends ADR
[0003](0003-package-layering.md)**'s package table: a new layer-5 package.

Records decision set
[`the-shape-derived-classifier`](../decisions/the-shape-derived-classifier.md).

## Context

ADR 0023 places implementations derived from annotated shapes or from
queries at layer 5. Row-level access needs one, declarative and applied by
`varve apply`. ADR 0230 also needs it to be **row-local**: every input of
subjects(n) must be a quad of n's own row, or the subjects of a node n links
to. Otherwise ADR 0236's write rule could not be made probe-free.

## Decision

1. **`Varve.Classification`, layer 5.** It holds:
   - the shape-derived `IClassifier`;
   - the classification projection (ADR 0233);
   - the `IRowClassification` they build.

   It references `Varve.Iri` (0), `Varve.Rdf` (1) and `Varve.Store` (4). It
   references neither `Varve.Shacl` nor any layer-5 package. The annotations
   are read as RDF (point 3), and `Varve.Shacl`'s shape model is internal
   (ADR 0155).
2. **The annotations**, non-validating terms in `https://w3id.org/varve/ns#`
   on shapes of the binding's shapes graph:

   | Term | On | Means |
   |---|---|---|
   | `varve:dataSubject true` | a node shape | its focus nodes are data subjects |
   | `varve:subjectLink true` | a property shape whose `sh:path` is an IRI | the path's predicate is a link predicate of the binding (ADR 0230 point 1) |
   | `varve:publicLink true` | a property shape whose `sh:path` is an IRI | the path's predicate is a public link of the binding (ADR 0230 point 3) |
   | `varve:privacy` | a property shape | reserved for erasure mode (#203), and refused until it lands |

   A link or public-link predicate is the **binding's**, wherever the shape
   that declares it sits. A predicate both linking and public is refused.
3. **Targets of a `varve:dataSubject` shape** are limited to those that read
   only the focus's own row. Any other target on an annotated shape is
   refused when the binding is read, with the shape named.
   - `sh:targetClass C`, and the implicit class target of a shape that is
     also an `rdfs:Class` or `owl:Class`: nodes n with `(n rdf:type C)` in
     Γ_B. `rdfs:subClassOf` is not followed, because it would read rows that
     are not n's.
   - `sh:targetNode n`.
   - `sh:targetSubjectsOf p`: nodes n with `(n p x)` in Γ_B.
4. **Classification from scratch** over a state:
   - the data subjects, by the targets;
   - then, for each, the reverse closure along the link predicates, adding
     the subject to every node that reaches it (ADR 0230 point 1's least
     set).
   - Its cost is the data subjects times the nodes that link to them,
     bounded by the quads of Γ_B under link predicates.
5. **Incremental classification** of a delta δ = (A, R):
   - **The changed nodes** are:
     - the subject of every changed quad whose predicate is `rdf:type`, a
       targeting predicate or a link predicate;
     - every node a changed quad names as a link object.
   - **The affected nodes** are the changed nodes, and everything that
     reaches them along link predicates in either state. Following ADR
     0171, assertions are traversed in the post-state and retractions in
     the pre-state.
   - **Each affected node's subjects** are recomputed by a forward traversal
     in the post-state.
   - It is equal to classification from scratch at the post-state, which
     ADR 0233's property checks at every position.
6. **The binding's own configuration** is the shapes graph as of the state:
   - **A change to the shapes graph** is a change of configuration, and
     reclassifies from scratch (ADR 0233's generation).
   - **The shapes graph must not be in Γ_B.** That is refused at apply, as
     for a shapes binding.
7. **Query-derived classification does not ship.** An arbitrary query can
   read any row, so its outcome could depend on rows a caller does not see,
   and ADR 0236's write argument fails. A query-derived classifier needs a
   locality analysis of its own before it is offered.

## Alternatives considered

- **Reuse `Varve.Shacl`'s target and path evaluation.** It is internal (ADR
  0155), and opening it would publish much more than the three targets and
  predicate paths this needs.
- **Follow `rdfs:subClassOf`** for class targets. It reads the class
  hierarchy's rows, which a row-restricted caller may not read. An operator
  who wants a hierarchy targets each class, or uses `sh:targetSubjectsOf`.
- **Link paths longer than one predicate.** Transitivity through nodes does
  that, with every step in a row of its own.
- **Put it in `Varve.Shacl.Store`.** That package gates commits and holds
  the validation projection, and would gain a second reason to change.

## Consequences

- **A new package**, in the README's package table and `AGENTS.md`'s layer
  table. It takes no new dependency.
- **`docs/vocabulary.md`** gains the four annotation terms.

## Checks

- **Checked against the accepted ADRs** (0001–0190). Touches:
  - **0003**: amended, as above;
  - **0023**: a layer-5 implementation;
  - **0155**: not opened;
  - **0171**: anchors in either state;
  - **0172**: no use. The closures are plain traversals.

  No conflict.
- **Layer ownership.** `Varve.Classification` (5).
- **Analyzer rule.** None new.
- **Open questions owned.** Query-derived classification, not owned by a
  milestone.
