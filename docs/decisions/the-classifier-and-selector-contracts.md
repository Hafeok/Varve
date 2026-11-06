---
set: the-classifier-and-selector-contracts
namespace: varve
adr: 0231
decisions:
  - key: RowClassificationAtLayerOne
    statement: "IRowClassification in Varve.Rdf is a classification at one state in one source's handles: the binding of a graph, the interned subject set of a node, its members and whether a predicate is a public link, its lookups allocation-free"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: ClassifierClassifiesTheOverlay
    statement: "IClassifier in Varve.Store gives the classification of the overlay of a pinned source and a pending delta, as a pure function of that state and the classifier's configuration"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: SelectorIsTheClassification
    statement: "ISelector in Varve.Store selects the quads of a source whose about-set contains a subject, implemented once over the classification so that one definition of a subject's data serves access and erasure"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: SelectorComparesThroughTheSourceComparer
    statement: "The selector's cursor compares a set's members with the subject through the source's TermComparer, which ADR 0022 makes the only correct equality, and allocates nothing doing so"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: AccessWithoutErasureIsAdminOnly
    statement: "GET /datasets/{name}/access with a subject and a binding answers the selector over the head, Current scope only, to a dataset admin only"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
---

The rulings of [ADR 0231](../adr/0231-the-classifier-and-selector-contracts.md), filed unaccepted by milestone 9, part 1 ([#202](https://github.com/Hafeok/Varve/issues/202), under #14)
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
