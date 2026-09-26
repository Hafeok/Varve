---
set: records-commits-and-bulk-load
namespace: varve
adr: 0013
decisions:
  - key: RecordsAndCommits
    statement: "A record is the physical unit of append, and a commit is one or more records of which the last carries a closing flag"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ReadableHeadIsLastClosedCommit
    statement: "The readable head is the position of the last closed commit, and an unclosed commit's records are invisible to every read, subscription and projection"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: UnclosedTailDiscarded
    statement: "On recovery an unclosed tail is discarded, never replayed, repaired or reported as data"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: ClosingFlagInTheLog
    statement: "The closing flag is in the log, so a copy of log/ made at any moment is a valid log up to its last closed commit"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: OrderedDurability
    statement: "Every record of a commit is durable before its closing record, and the closing record's durability is the commit point"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: BulkLoadIsOneCommit
    statement: "A bulk load is one logical commit of as many records as it needs, written in bounded memory"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: RecordsIndependentlyDiscardable
    statement: "A record never depends on anything outside the log having been updated when it was written"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
  - key: SubscribersReceiveCommits
    statement: "A subscriber receives commits, never records, and a replica ships records but advances its readable head only on a close"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-21T00:00:00Z
---

The rulings of [ADR 0013](../adr/0013-records-commits-and-bulk-load.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Open questions Q2 and Q3 are open questions, not rulings, and are not enumerated.
