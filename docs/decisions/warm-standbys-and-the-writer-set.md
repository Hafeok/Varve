---
set: warm-standbys-and-the-writer-set
namespace: varve
adr: 0180
decisions:
  - key: WriterSetIsPlacedOnAndStandbyOn
    statement: "A dataset's writer set is its varve:placedOn, at most once and the preferred writer, and its varve:standbyOn, which needs varve:automaticHandover true and is disjoint from placedOn; only a member claims"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: PlacementIsAnEvent
    statement: "An instance acts on a change between consecutive declarations, never on a state: a placedOn change makes the holder abdicate and only the new placedOn claim; removing the holder from standbyOn makes it abdicate to the preferred writer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: StartRuleForAWriterSet
    statement: "At start a member claims a dataset with a writer set only when the record is empty or released and it is the preferred writer, when it holds the record itself, or when the holder is not a member; otherwise it starts as a standby"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: ClaimAfterAReleaseByItsReason
    statement: "A release for placement is claimed by the new placedOn alone; one for shutdown, storage-error or local-disk-error by every warm member racing, the store's conditional write picking one; a release for storage-error or local-disk-error starts the releaser's hold-off (ADR 0163), so it races for its own release only after it; a returning placedOn starts as a standby; restated by the amendment of 2026-11-10"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-11-10T00:00:00Z
  - key: StandbysFollowOnTheInterval
    statement: "A standby advances on Varve:Fleet:FollowInterval, as the system dataset does, and reads the dataset's writer record on the same interval; a follower that is not a standby still advances on demand (ADR 0164)"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: WarmIsDefaultProjectionCaughtUp
    statement: "A standby is warm when it tails on the interval, its last advance succeeded, its default projection has caught up with what it applied, it is not re-opening, and it has advanced since its last open; validation projections are not part of warm"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: ReadinessReportsTheStandby
    statement: "Readiness reports each dataset's role, applied position, projection lag, tailing, warm and each binding's lag; a standby not warm stays ready, and no lag behind the writer is invented"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
---

The rulings of [ADR 0180](../adr/0180-warm-standbys-and-the-writer-set.md),
one line each. Filed unaccepted by Replication part 1, #165 (ADR 0066); every
citation is `CS0618` until the maintainer accepts them.

`ClaimAfterAReleaseByItsReason` is restated by the ADR's amendment of
2026-11-10 (#216), and accepted as restated on #252 on 2026-11-10, at the
maintainer's word, by his delegate (ADR 0239).
