# The change feed and the diff over HTTP

Functional specification for the change-feed and diff endpoints of
`Varve.Protocol` (layer 5), and for the `application/vnd.varve.delta` format
they write and `ChangeFeedReader` reads.

Status: **Proposed** with [ADR 0097](../adr/0097-the-change-feed-and-the-diff.md)
(milestone 7a, filed unaccepted). It changes only together with the ADR that
motivates the change. Format version **1**.

## 1. Normative references

- `log-and-projection-model.md` §8 (subscriptions), R3 (diff), I5 (time),
  T1 (commit kinds). The feed is a projection of the log: it reads through the
  subscription contract and nothing else.
- `n-triples.md`, canonical N-Quads as RDF 1.2 defines it (ADR 0061).
- RFC 3339 (timestamps); the WHATWG HTML Living Standard, *Server-sent events*
  (the `text/event-stream` framing).
- ADR 0098, blank nodes at the protocol boundary.

## 2. The format: `application/vnd.varve.delta; version=1`

UTF-8, lines ended by LF (`U+000A`) alone. A CR before an LF is an error. A
document is a sequence of **records**, each a block of lines ended by an
**empty line**. Between records there may be **comment lines**, which begin with
`#` and are ignored; a live feed writes `#` alone as a heartbeat.

### 2.1 A commit record

```
commit <position> <kind> <timestamp>
agent <term>
cause <term>
scope <term>
attachment <term>
+ <quad>
- <quad>

```

- **Header**: `commit`, one space, the position in decimal (no sign, no leading
  zeros, 1 or more), one space, the kind — `Data`, `Settings` or `Erasure` —
  one space, and the commit's timestamp as RFC 3339 in UTC with exactly seven
  fractional digits and `Z`: `2026-10-07T12:00:00.0000000Z`.
- **Metadata lines**, each optional and in this order: `agent`, `cause`,
  `scope` (the declared named-graph scope), then zero or more `attachment`.
  Each is the keyword, one space, and one term in canonical N-Triples form.
- **Change lines**: `+` or `-`, one space, and one quad in canonical N-Quads
  form without its trailing ` .`. A quad in the default graph has three terms.
  Assertions come before retractions. Within each, quads are in the order the
  store delivers them; the order carries no meaning.
- A `Settings` or `Erasure` commit has no change lines (I4). Its content is
  read from the dataset (its status endpoint), not from the feed, in this
  version.
- **Blank nodes** are written with labels derived from their store identity
  (ADR 0098). A label names the same node in every record, every diff and every
  query result from the same dataset. A label sent back in a request is a fresh
  node, never this one.

### 2.2 A diff record

```
diff <from> <to>
+ <quad>
- <quad>

```

`Diff(from, to)` of R3: the assertions are `G_to \ G_from` and the retractions
`G_from \ G_to`.

### 2.3 An error record

```
error <problem type IRI>

```

The last record of a stream that was cut (ADR 0095). It names an RFC 9457
problem type under `https://w3id.org/varve/problems/`. After it the stream
ends. A stream that ends without one and without its normal end was cut by the
transport.

### 2.4 What a reader must do

- Reject a record that is not one of the three forms, a line out of order, a
  term that is not canonical N-Triples, and a quad whose graph is a literal.
- Accept the input in any chunking: the result is the same for the whole
  document and for the document split at any byte (`testing.md` §2).
- Report an error with the byte offset, line and column of the first byte of
  the offending line.

## 3. Framing for a live tail: `text/event-stream`

Each record is one event:

```
id: <position>
event: commit
data: commit 42 Data 2026-10-07T12:00:00.0000000Z
data: + <http://ex/s> <http://ex/p> "o"

```

- `id` is the record's position, so a browser's automatic reconnection sends
  it back as `Last-Event-ID`, which the endpoint takes as `from` (§4).
- `event` is `commit`, `error`, or `shutdown` (ADR 0101). An `error` or
  `shutdown` event's `id` is the last position delivered, so a reconnection
  resumes after it, and its data is §2.3's error record.
- Each line of the record is one `data:` line. The record's terminating empty
  line is the event's.
- A comment line `:` is the heartbeat.

## 4. `GET /feed`

| Parameter | Meaning |
|---|---|
| `from` | a position; the feed starts **after** it (exclusive). Default 0. |
| `fromTime` | an RFC 3339 instant; resolves to the **earliest** closed commit whose timestamp is at or after it, and the feed starts at that commit. |
| `to` | a position; the feed ends **at** it (inclusive). |
| `toTime` | an RFC 3339 instant; resolves to the **latest** closed commit whose timestamp is at or before it, and the feed ends there. |
| `graph` | an absolute IRI, or `default`: only changes in that graph. |
| `pattern` | three terms in N-Triples syntax separated by spaces, each a term or `?name` for any term: only changes whose subject, predicate and object match. |

- `from` and `fromTime` are exclusive of each other, as are `to` and `toTime`;
  giving both of a pair is `400`.
- `Last-Event-ID`, when present, replaces `from` and `fromTime`.
- **With `to` or `toTime`** the range is bounded. The response is finite and
  ends after the record at `to`, or at once if the range is empty. A `to`
  after the head is `404` (`position-not-reached`). A `toTime` after the head's
  time resolves to the head.
- **With neither**, the feed tails live: it delivers every closed commit up to
  the head, then each new one as it closes, until the client leaves or the
  server shuts down.
- **Resolution is asymmetric**, as in Delta Sharing's change data feed. A start
  instant with no commit at or after it starts the feed at the head, where it
  waits. An end instant before the first commit gives an empty range.
- **Filters** restrict change lines. `Settings` and `Erasure` records are always
  present (spec §8). A `Data` commit whose filtered changes are empty is
  skipped, and the next record carries its true position. A term in a filter
  that the dataset does not yet know matches nothing until a commit allocates
  it, and from then on matches it.
- **The caller's scope** (ADR 0107) is a filter the caller did not write: a
  `Data` record's changes are cut to the graphs the caller reads, a record
  that becomes empty is skipped as above, and `Settings` and `Erasure`
  records go to a dataset admin alone. The diff is cut the same way. A
  client resumes by position as before, because the positions delivered are
  the commits' own.
- **Delivery is at-least-once, resumable by position**: a client that persists
  the last position it applied and resumes from it misses nothing and sees
  nothing twice.
- `Accept` chooses the framing. `application/vnd.varve.delta` (also the
  default) writes §2's format. `text/event-stream` writes §3's. Anything else
  is `406`.
- The response carries `Varve-Position` with the **start**: the exclusive
  starting position after resolution.

## 5. `GET /diff`

`from` or `fromTime`, and `to` or `toTime`, resolved as in §4: a start instant
gives the position before the earliest commit at or after it. `to` and
`toTime` default to the head. The body is one diff record. `from` after `to`
is allowed and gives the inverse diff. The response carries `Varve-Position`
and `ETag` with the resolved `to`.

## 6. Properties

The implementation is tested against these, by generated histories
(`docs/testing.md`):

1. **Replay.** Applying every record of the feed from 0 to the head, in order,
   to an empty dataset yields a dataset isomorphic to the as-of read at the head.
2. **Resumption.** The feed from any position `p` delivers exactly the commits
   after `p` that the feed from 0 delivers after `p`.
3. **Concatenation.** The bounded feed `(a, b]` followed by `(b, head]` equals
   `(a, head]`.
4. **Time.** For histories with repeated timestamps, `fromTime` and `toTime`
   resolve as §4 says, and `Varve-As-Of: time:` resolves as I5 says.
5. **Diff.** `Diff(a, b)` equals the net of the feed's changes over `(a, b]`.
