---
set: per-graph-optimistic-concurrency
namespace: varve
adr: 0125
decisions:
  - key: ConflictWhenTheScopeWasTouched
    statement: "A request with expected position p and scope S conflicts when some commit in (p, head] touched a graph in S, and without a scope S is every graph"
  - key: SettingsAndErasureTouchEveryGraph
    statement: "A Data commit touches the graphs of its effective delta, and a Settings or Erasure commit touches every graph"
  - key: GraphVersionIndexIsAProjection
    statement: "The version of each graph is maintained by the default projection, persisted in derived/ with the projection's state and beside each checkpoint, and rebuilt from the log when absent or stale"
  - key: GraphVersionAtAPositionLikeR2
    statement: "A graph's version at an earlier position is the nearest checkpoint's map overlaid with the graphs the commits after it touched, bounded like an as-of read"
  - key: ConflictEvidenceIsBounded
    statement: "The commits that caused a conflict are listed outside the sequencer, newest first, limited in count and in log distance, saying when the list stopped short"
  - key: GraphsAreTheUnitNotResources
    statement: "Optimistic concurrency is per graph; a per-resource version index is rejected for now as the size of the dataset"
---

The rulings of [ADR 0125](../adr/0125-per-graph-optimistic-concurrency.md), filed unaccepted by milestone Commit path of #91
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
