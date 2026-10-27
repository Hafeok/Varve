---
set: warm-standbys-and-the-writer-set
namespace: varve
adr: 0180
decisions:
  - key: WriterSetIsPlacedOnAndStandbyOn
    statement: "A dataset's writer set is its varve:placedOn, at most once and the preferred writer, and its varve:standbyOn, which needs varve:automaticHandover true and is disjoint from placedOn; only a member claims"
  - key: PlacementIsAnEvent
    statement: "An instance acts on a change between consecutive declarations, never on a state: a placedOn change makes the holder abdicate and only the new placedOn claim; removing the holder from standbyOn makes it abdicate to the preferred writer"
  - key: StartRuleForAWriterSet
    statement: "At start a member claims a dataset with a writer set only when the record is empty or released and it is the preferred writer, when it holds the record itself, or when the holder is not a member; otherwise it starts as a standby"
  - key: FollowOnDemand
    statement: "A follower advances when a read arrives after Varve:Fleet:FollowInterval since its last advance, or on a minimum-position header above it; a standby and the system dataset advance on the interval, a standby also reading the writer record conditionally"
  - key: WarmIsDefaultProjectionCaughtUp
    statement: "A standby is warm when it tails on the interval, its last advance succeeded, its default projection has caught up with what it applied, it is not re-opening, and it has advanced since its last open; validation projections are not part of warm"
  - key: ReadinessReportsTheStandby
    statement: "Readiness reports each dataset's role, applied position, projection lag, tailing, warm and each binding's lag; a standby not warm stays ready, and no lag behind the writer is invented"
---

The rulings of [ADR 0180](../adr/0180-warm-standbys-and-the-writer-set.md),
one line each. Filed unaccepted by Replication part 1, #165 (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.
