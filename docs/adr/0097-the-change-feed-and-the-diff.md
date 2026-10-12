# 0097 — The change feed and the diff: a line format, SSE for tailing

## Status

**Accepted — filed unaccepted by milestone 7a of #11, 2026-10-07** (ADR 0066).
Decided by the maintainer on the 7a plan: "application/vnd.varve.delta;
version=1". The format is specified in
[`docs/spec/change-feed.md`](../spec/change-feed.md). Acceptance is the
maintainer's act on the pull request.

**Amended 2026-10-09** (ADR 0068), by milestone Operability of #12 under ADR
[0118](0118-api-alignment-while-everything-is-preview.md): the feed endpoint
is the commits resource; see the end.

## Context

A change feed is the second reason the store is event-sourced (the brief's
thesis). The spec defines it in-process (§8: `Subscribe(from, filter)`,
at-least-once, consumer-owned positions; ADR 0042: a reader of the log). The
diff is R3. Over HTTP both need a wire format and a framing for streams that do
not end.

Delta Sharing's change data feed (*Read Change Data Feed from a Table*) is the
production precedent:

- a start and an end, each a version or a timestamp;
- the start timestamp resolved to the first version at or after it, the end
  to the last at or before it;
- a version header on the response.

Its range is inclusive at both ends. Ours keeps the log's own convention, an
exclusive start (spec §8), so that a resumed feed's `from` is the last position
applied.

## Decision

1. **The format is `application/vnd.varve.delta; version=1`**, a line format
   (the specification, §2):
   - one record per commit — position, kind, timestamp, agent, cause, scope,
     attachments — and its changes as canonical N-Quads lines prefixed with
     `+` or `-`;
   - a diff record for R3;
   - an error record for a stream cut short (ADR 0095).

   It is a line format, not JSON, so that a change is the same bytes a
   canonical N-Quads writer produces, and a reader is a canonical N-Quads
   reader plus four keywords.
2. **The feed endpoint is `GET /datasets/{name}/feed`** with `from` or
   `fromTime`, `to` or `toTime`, and `graph` and `pattern` filters (§4).
   - Resolution is asymmetric as in Delta Sharing: start ≥, end ≤.
   - `from` is exclusive and `to` inclusive.
   - No `to` means a live tail.
   - The response's `Varve-Position` is the resolved start.
3. **Live tailing is server-sent events** (`text/event-stream`). Each record is
   one event whose `id` is its position, so the browser's own reconnection
   resumes the feed through `Last-Event-ID` with no client code. The plain
   format can also tail live, over a chunked response with `#` heartbeats, for
   clients that are not browsers.
4. **The feed reads through the subscription contract and nothing else.**
   - Every record comes from `Dataset.Subscribe`.
   - Its terms come from `Commit.TryExternalise` (ADR 0042, amended
     2026-10-07).
   - A filter goes to the subscription when its terms are known. When one is
     not yet known, the subscription is unfiltered, and the protocol matches by
     handle, learning the handle from the commit that allocates it.
   - `Settings` and `Erasure` commits are always delivered (ADR 0046).
5. **The diff endpoint is `GET /datasets/{name}/diff`**: `Dataset.DiffAsync`
   over the resolved range, written as one diff record.
6. **A reader ships in `Varve.Protocol`**: `ChangeFeedReader`, a pull reader
   over UTF-8 that yields `FeedRecord`s with Varve's own terms. It runs the
   chunk-boundary oracle (`testing.md` §2), so a client consumes the feed with
   Varve types and no parser of its own.

## Alternatives considered

- **Long-polling.** Each request returns the commits after `from` up to a
  batch limit, or waits until one arrives. It needs no streaming support in
  proxies and no client-side event parser, and costs a request per batch and a
  resume protocol of its own. Recorded as the alternative. The plain format's
  bounded range is already its building block: a client can long-poll with
  `from` and a `to` of the head.
- **NDJSON**, one JSON object per commit with the delta as an array of quads.
  Familiar, and it puts N-Quads text inside JSON strings, escaped twice, or
  invents a JSON shape for RDF terms that canonical N-Quads already defines.
- **WebSockets.** Bidirectional, which the feed does not need, and no standard
  resume.
- **The inclusive start of Delta Sharing.** A resumed client would have to add
  one; the log's convention would differ from the protocol's.

## Consequences

- A consumer replays the log by position with nothing but HTTP and a line
  reader. The specification's §6 properties are the tests.
- A filtered term not yet known costs an unfiltered subscription until it
  appears: reads, not memory (ADR 0042).
- `Settings` records carry no settings in version 1. A client reads them from
  `/status`. A later version may carry them as lines, under the version
  parameter.

## Checks

- **Checked against the accepted ADRs** (0001–0090) and specification 1.5.
  Touches:
  - **0016** and **0042**: delivery, filters, the reader;
  - **0046**: kinds that bypass the filter;
  - **0047**: R3's composition;
  - **0061**: canonical form;
  - **0098**: blank labels.

  No conflict.
- **Layer ownership.** `Varve.Protocol`, layer 5. The store is unchanged
  except `Commit.TryExternalise`.
- **Analyzer rule.** None.
- **Open questions owned.** None.

## Amendment, 2026-10-09 — the feed is the commits resource

Filed by milestone Operability of #12, unaccepted until the maintainer
accepts it (ADR 0066). The format (point 1), the resolution (point 2's
rules), the subscription contract (point 4), the diff (point 5) and the
reader (point 6) stand. What changes is the address and the framing choice
(ADR 0118):

- `GET /datasets/{name}/commits?from=&to=` is the bounded range, with the
  parameters of point 2, paged by `Limits:CommitsPageSize` with
  `Link rel="next"` (ADR 0114);
- `GET /datasets/{name}/commits/{position}` is one commit as one record;
- an open range (no `to`) tails live, as **server-sent events when `Accept`
  asks for `text/event-stream`** and as the plain format with heartbeats
  otherwise, as point 3 already allowed; the position is the event id and
  `Last-Event-ID` resumes;
- `/feed` is removed, not aliased.

`docs/spec/change-feed.md` §4 is retitled `GET /commits` and says the same.

**The ledger.** `FeedEndpoint` and `LiveTailIsServerSentEvents` stand as
accepted; the ruling of this block is `FeedIsTheCommitsResource`, unaccepted
until the maintainer accepts it.