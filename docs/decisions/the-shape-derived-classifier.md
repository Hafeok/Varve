---
set: the-shape-derived-classifier
namespace: varve
adr: 0232
decisions:
  - key: VarveClassificationAtLayerFive
    statement: "Varve.Classification at layer 5 holds the shape-derived classifier and the classification projection, referencing Varve.Iri, Varve.Rdf and Varve.Store only"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: AnnotationsReadAsRdf
    statement: "The classifier reads varve:dataSubject on node shapes and varve:subjectLink and varve:publicLink on property shapes with an IRI path, from the binding's shapes graph as RDF; varve:privacy is reserved for erasure mode"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: TargetsReadOnlyTheFocusRow
    statement: "A data subject shape targets by sh:targetClass, an implicit class target, sh:targetNode or sh:targetSubjectsOf only, without rdfs:subClassOf, and any other target on an annotated shape is refused"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: IncrementalByAffectedNodes
    statement: "A delta reclassifies the changed nodes and every node reaching them by link predicates in either state, each recomputed in the post-state, equal to classification from scratch"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: ShapesChangeIsAConfigurationChange
    statement: "A change to the binding's shapes graph reclassifies from scratch, and the shapes graph is never in the classified graph set"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: NoQueryDerivedClassifier
    statement: "No query-derived classifier ships, since a query can read any row and the write rule's argument would fail"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
---

The rulings of [ADR 0232](../adr/0232-the-shape-derived-classifier.md), filed unaccepted by milestone 9, part 1 ([#202](https://github.com/Hafeok/Varve/issues/202), under #14)
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
