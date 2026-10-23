---
set: the-format-5-upgrade-and-the-set-cap
namespace: varve
adr: 0178
decisions:
  - key: AFormat4SetIsNarrowedNeverFailed
    statement: "A format-4 extent set loads and serves reads of the present as before, and a read inside its window narrows it; it is never failed and never rebuilt at open"
  - key: MaintenanceRewritesFormat4Sets
    statement: "When the merge policy has nothing due, the writer's maintenance rewrites the oldest format-4 set alone as format 5, a merge of one input under ADR 0158's rules; with merges off nothing is rewritten"
  - key: AnOlderReaderMissesFormat5
    statement: "An instance that reads derived format 4 finds a format-5 extent a cache miss: its chain ends before that set and it reads the log from there, never failing and never returning a wrong result"
  - key: FollowersFirst
    statement: "A rolling upgrade on one object store upgrades the followers before the writer"
  - key: TheCapBoundsMergesNotReads
    statement: "MaxSetBytes bounds a merge's work and an extent's upload, not an as-of read's cost; its default stays 256 MiB"
---

The rulings of [ADR 0178](../adr/0178-the-format-5-upgrade-and-the-set-cap.md),
filed unaccepted by #133 (ADR 0066).
