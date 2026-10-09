---
set: the-change-feed-and-the-diff
namespace: varve
adr: 0097
decisions:
  - key: DeltaLineFormat
    statement: "The feed and the diff are written as application/vnd.varve.delta; version=1, a line format of commit, diff and error records with canonical N-Quads changes prefixed + or -"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: FeedEndpoint
    statement: "GET /datasets/{name}/feed takes from or fromTime, to or toTime, graph and pattern; from is exclusive, to inclusive, and no end tails live"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: FeedResolutionIsAsymmetric
    statement: "A start time resolves to the earliest commit at or after it and an end time to the latest at or before it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: LiveTailIsServerSentEvents
    statement: "Live tailing is server-sent events with the position as the event id and Last-Event-ID as the resume point; long-polling is the recorded alternative"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: FeedReadsTheSubscriptionOnly
    statement: "The feed reads through Dataset.Subscribe and Commit.TryExternalise and nothing else, filtering by handle itself only while a filter term is not yet known"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: DiffEndpoint
    statement: "GET /datasets/{name}/diff writes Diff(from, to) of R3, resolved as the feed resolves, as one diff record"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: FeedReaderShips
    statement: "Varve.Protocol ships ChangeFeedReader, a pull reader of the format over UTF-8 that runs the chunk-boundary oracle"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-08T00:00:00Z
  - key: FeedIsTheCommitsResource
    statement: "The feed is GET /datasets/{name}/commits for a range and /commits/{position} for one commit, tailing live by Accept text/event-stream on an open range, and /feed is removed; amended 2026-10-09 under ADR 0118"
---

The rulings of [ADR 0097](../adr/0097-the-change-feed-and-the-diff.md), filed unaccepted by milestone 7a of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
