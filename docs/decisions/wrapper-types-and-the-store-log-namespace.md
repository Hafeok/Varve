---
set: wrapper-types-and-the-store-log-namespace
namespace: varve
adr: 0065
decisions:
  - key: LogIsTheModel
    statement: "The log's values live in Varve.Store.Log, the store's model namespace, and the engine stays in Varve.Store"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: LogTypesThatMove
    statement: "Commit, CommitKind, CommitMetadata, CommitOutcome, CommitResult, CommitRequest, DatasetSettings, SettingsChange, SubscriptionFilter, ValidationVerdict and SegmentInfo move to Varve.Store.Log"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: WrappersAreReadonlyRecordStructs
    statement: "Each wrapper is a readonly record struct over one primitive, with an explicit constructor, a Value property and no implicit conversion either way"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: PositionIsAWrapper
    statement: "Every log position is a Position over long, ordered as I1 requires, with no public arithmetic"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: CommitTimestampIsAWrapper
    statement: "A commit timestamp is a CommitTimestamp over DateTimeOffset, ordered as I5 requires"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: StorageMemberTypes
    statement: "The storage contract's segment ids, offsets and lengths are SegmentId, ByteOffset and ByteCount"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: QuadCountInRdf
    statement: "A cardinality estimate's count is a QuadCount in Varve.Rdf"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: RdfLearnsNoPositions
    statement: "Varve.Rdf learns nothing of positions, time or storage"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: EngineTakesWrappersToo
    statement: "The engine's own members take and return the wrappers too, even where no rule requires it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: WrapperOnlyApiDiff
    statement: "The public API diff of the change is the wrapper types, the moved namespace and the retyped signatures, and nothing else"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
---

The rulings of [ADR 0065](../adr/0065-wrapper-types-and-the-store-log-namespace.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Received from earlier sets: `PositionIsAWrapper` and `CommitTimestampIsAWrapper` supersede
0011's results over BCL primitives; `StorageMemberTypes` supersedes 0040's member types;
`QuadCountInRdf` supersedes 0049's `Count` type. What the ADR leaves undecided is not a ruling.
