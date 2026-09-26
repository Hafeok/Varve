---
set: projection-contract-and-subscriptions
namespace: varve
adr: 0016
decisions:
  - key: ProjectionGetsClosedCommitsInOrder
    statement: "A projection receives closed commits in position order, never records and never an unclosed tail"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: AtLeastOnceIdempotentByPosition
    statement: "Delivery to a projection is at-least-once, and applying a commit at or below the projection's position is a no-op"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: PositionPersistedWithState
    statement: "A projection persists its position atomically with its state"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ProjectionRebuildEquivalence
    statement: "A projection may be dropped and rebuilt from position 0 or a checkpoint, and a rebuilt projection is observationally equal to a maintained one (I8)"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: DefaultProjectionSynchronous
    statement: "The default quad projection is updated before Committed(P) returns, so a pin taken afterwards observes P, and every other projection lags"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: CommitStandsIfProjectionFails
    statement: "If the default projection fails after a commit's records are durable, the commit stands and the projection catches up by replay"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: SequencerWaitsForDefaultProjection
    statement: "The sequencer refuses the next commit with Unavailable until the default projection is at the readable head"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: StuckProjectionFailsTheDataset
    statement: "A default projection that cannot reach the head puts the dataset in an explicit failed state, so nothing waits indefinitely"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: SubscriptionConsumerOwnsPosition
    statement: "Subscribe(from, filter) delivers closed commits after from, in order and at-least-once, and the consumer owns its position"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: FilteredSkipsKeepTruePositions
    statement: "A subscription filter skips a commit whose filtered delta is empty, and the next delivered commit carries its true position"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ErasurePurgeInProjections
    statement: "A projection holding plaintext derived from private terms records the KeyId of each entry, keeps it only under derived/, and purges it on that key's Erasure commit"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
---

The rulings of [ADR 0016](../adr/0016-projection-contract-and-subscriptions.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Moved to a later set: `Erasure` commits bypassing the subscription filter, as amended on
2026-09-23 to include `Settings` commits, is in ADR 0046's set. The same-day amendment of
2026-09-21 is transcribed here with the ADR's date.
