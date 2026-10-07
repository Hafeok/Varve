# 0096 — Time travel over HTTP: `Varve-As-Of`, `ETag`, `304`

## Status

**Proposed — filed unaccepted by milestone 7a of #11, 2026-10-07** (ADR 0066).
Acceptance is the maintainer's act on the pull request.

## Context

The store answers an as-of read at any closed position at or above the archive
horizon (R2), resolves a timestamp to the greatest position whose timestamp is
at or before it (I5), and refuses below the horizon with an explicit error
(T3). The protocol has to carry the same three things.

Delta Sharing (the Delta Sharing Protocol, `PROTOCOL.md` in
`delta-io/delta-sharing`) has run time travel over HTTP in production since
2021:

- a query selects a snapshot by `version` or by `timestamp` (*Read Data from a
  Table*, request body);
- every response that describes a table carries the version it describes in a
  `Delta-Table-Version` header (*Query Table Version*, *Query Table Metadata*,
  *Read Data from a Table*);
- timestamps are ISO 8601 in UTC (*Timestamp Format*).

Its change data feed resolves a start timestamp to "a version created greater
or equal to this timestamp" and an end timestamp to one "created earlier than
or at the timestamp" (*Read Change Data Feed from a Table*). The feed adopts
that asymmetry in ADR 0097.

## Decision

1. **A read may carry `Varve-As-Of`**:
   - `position:<n>` — `n` a decimal position;
   - `time:<RFC 3339 date-time>` — any offset, normalised to UTC. At most seven
     fractional digits, the store's tick. More is `400`, because silently
     truncating a time is silently moving it.

   It applies to queries (`GET` and `POST`), Graph Store `GET` and `HEAD`, and
   the service description. On a write it is `400`.
2. **A time resolves by I5**: the greatest position whose commit timestamp is
   at or before it (`Dataset.PositionAt`).
3. **Resolutions that have no answer are `404`**, each with its own problem
   type:

   | Case | Problem |
   |---|---|
   | a time before the first commit, or any time in an empty dataset | `before-first-commit` |
   | a position after the head | `position-not-reached` |
   | a position below the archive horizon | `below-archive-horizon` |

   A position below the horizon cannot occur until T3 is built. The problem
   type is reserved now so that clients can handle it from the first release.
4. **Every response that touched a dataset carries `Varve-Position`** — the
   head, or the resolved as-of position — and **`ETag: "<position>"`**, a
   strong tag (the bytes at a position never change for a given request and
   representation). A `Vary: Accept, Varve-As-Of` keeps caches honest.
5. **`If-None-Match` on a read returns `304`** when its tag equals the position
   the read would describe: the head, or the resolved as-of position. It is
   answered **before any pin is taken**, so a client that polls an unchanged
   dataset costs one comparison. `*` matches any position.
6. **The service description advertises all of it** under `varve:`: the header
   names, both selector forms, and the conditional requests.

## Alternatives considered

- **A query-string parameter (`?asOf=`)** in place of a header. Visible in
  logs and bookmarks. It is also a parameter the SPARQL Protocol does not
  define, on a URL whose parameters it does define. A header leaves the
  protocol's own parameters alone and applies uniformly to the GSP.
- **`Accept-Datetime` and `Memento-Datetime` (RFC 7089).** Memento's
  negotiation is per resource with redirects to an original URI, and it names
  instants only, never positions. The resolution rule is the same, so a Memento
  front can be added later over this.
- **A weak `ETag`.** Weak tags cannot be used with `If-Match` (RFC 9110
  §13.1.1), and ADR 0094 needs exactly that.
- **`410 Gone` below the horizon.** Arguably more precise. `404` with a
  distinct problem type is what Delta Sharing does and what a client already
  handles. The problem body says which.

## Consequences

- Polling is cheap: `If-None-Match` with the last `ETag` is answered from the
  head alone.
- An as-of read costs what R2 says, proportional to the log distance from the
  nearest checkpoint, and is bounded by the same limits as any read (ADR 0095).

## Checks

- **Checked against the accepted ADRs** (0001–0090) and specification 1.5.
  Touches:
  - **0015** (R2, as-of reads; the horizon);
  - I5 (resolution);
  - **0094** (the `ETag` form `If-Match` uses).

  No conflict.
- **Layer ownership.** `Varve.Protocol`, layer 5.
- **Analyzer rule.** None.
- **Open questions owned.** None. T3's horizon is the storage roadmap's.
