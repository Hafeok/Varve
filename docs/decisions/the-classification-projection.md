---
set: the-classification-projection
namespace: varve
adr: 0233
decisions:
  - key: OneClassificationProjectionPerBinding
    statement: "ClassificationProjection is an IProjection per classifier binding whose rows are a node, its interned subject set and a validity interval, and which keeps the nodes whose set changed at each position"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: ClassificationGenerationsPublishedWhole
    statement: "A shapes change, a shapes graph that did not load or a change of binding starts a generation, a classification from scratch published only once applied"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: ClassificationAsOfAnyPosition
    statement: "The projection answers a classification at any position: its rows above its floor, and from scratch over the as-of state below it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: ClassificationInTheLocalCacheVerified
    statement: "The state is one blob in the local derived store identifying its binding and verified in full by length and SHA-256 on read, a blob that does not verify being a miss and a rebuild"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: ClassificationFailsClosed
    statement: "A generation whose shapes graph does not classify fails closed: every node belongs to a subject no scope names, so a row-restricted caller sees no quad of the binding's graphs, and the problems are on readiness"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: ClassificationLookupsMemoisedPerHandle
    statement: "A classification decides a graph's binding once per graph handle, a node's set by a lookup of an immutable map for its position, and a triple term's set once per handle, none allocating once made"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: ClassificationLagNeverUnready
    statement: "Each binding's lag is on readiness and never makes the dataset unready; only a row-restricted read above the projection waits once and is then 503 classification-behind"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
---

The rulings of [ADR 0233](../adr/0233-the-classification-projection.md), filed unaccepted by milestone 9, part 1 ([#202](https://github.com/Hafeok/Varve/issues/202), under #14)
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
