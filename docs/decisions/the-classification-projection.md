---
set: the-classification-projection
namespace: varve
adr: 0233
decisions:
  - key: OneClassificationProjectionPerBinding
    statement: "ClassificationProjection is an IProjection per classifier binding whose rows are a node, its interned subject set and a validity interval, and which keeps the nodes whose set changed at each position"
  - key: ClassificationGenerationsPublishedWhole
    statement: "A shapes change, a shapes graph that did not load or a change of binding starts a generation, a classification from scratch published only once applied"
  - key: ClassificationAsOfAnyPosition
    statement: "The projection answers a classification at any position: its rows above its floor, and from scratch over the as-of state below it"
  - key: ClassificationInTheLocalCacheVerified
    statement: "The state is one blob in the local derived store identifying its binding and verified in full by length and SHA-256 on read, a blob that does not verify being a miss and a rebuild"
  - key: ClassificationLagNeverUnready
    statement: "Each binding's lag is on readiness and never makes the dataset unready; only a row-restricted read above the projection waits once and is then 503 classification-behind"
---

The rulings of [ADR 0233](../adr/0233-the-classification-projection.md), filed unaccepted by milestone 9, part 1 ([#202](https://github.com/Hafeok/Varve/issues/202), under #14)
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
