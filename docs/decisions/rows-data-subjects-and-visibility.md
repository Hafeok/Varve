---
set: rows-data-subjects-and-visibility
namespace: varve
adr: 0230
decisions:
  - key: ClassifierBindingClassifiesAGraphSet
    statement: "A classifier binding classifies the quads of its graph set taken together as one data graph, and a data subject is a node its classifier names as one"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: SubjectsAreTransitiveThroughLinks
    statement: "The subjects of a node at a position are the least set holding the node when it is a data subject and the subjects of every node it reaches by a quad under a link predicate of the binding"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: AboutTheSubjectAndAClassifiedObject
    statement: "A quad in a classified graph is about the subjects of its subject node and, unless its predicate is a public link of the binding, the subjects of its object; a triple term carries the subjects of its components"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: TypeIsAPublicLink
    statement: "rdf:type is a public link in every binding, and a quad under a public link is about its subject's subjects only"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: VisibleWhenEverySubjectIsInTheSet
    statement: "A caller sees a quad of a classified graph when the graph is readable and every subject the quad is about is in the caller's subject set for the binding; a quad about no subject, or outside every binding, is governed by its graph alone"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: SubjectSetPerBinding
    statement: "A caller's subject set for a binding is the union of the subject sets of its read and write grants covering a graph of the binding, and all when one of them carries no subject terms"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: NoHiddenNodeIriInAnyResponse
    statement: "No response carries the IRI or label of a classified node whose subjects are not all in the caller's subject set, unless a quad under a public link names it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: ClassificationIsAFunctionOfStateAndBinding
    statement: "Classification at a position is a function of the state there and of the binding in force at the management position its commit was authorised under, never of history"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: RowLevelIsMilestone9
    statement: "Row-level access is decided by a classifier binding's classification and a grant's subject set, as this ADR states; graph-level scoping is unchanged beneath it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
---

The rulings of [ADR 0230](../adr/0230-rows-data-subjects-and-visibility.md), filed unaccepted by milestone 9, part 1 ([#202](https://github.com/Hafeok/Varve/issues/202), under #14)
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
