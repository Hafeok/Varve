# 0119 — Headers by read kind, and a catalogue of every problem the server can emit

## Status

**Proposed — filed unaccepted by milestone Operability of #12, 2026-10-09**
(ADR 0066). Acceptance is the maintainer's act on the pull request.

**Amends by dated block** [0092](0092-protocol-scope-problem-details-and-tie-breakers.md)
point 5 (the catalogue), [0094](0094-a-write-over-http-is-one-commit.md)
point 3 (the cause and trace context) and [0096](0096-time-travel-over-http.md)
point 4 (the cache headers).

## Context

ADR 0096 gave every dataset response `ETag` and `Varve-Position` and a
`Vary` of `Accept, Varve-As-Of`; ADR 0107 added `Authorization`. What a
cache or a client still cannot know from the headers: that an as-of read at
a closed position never changes, when the position was committed, where the
service description is, how to continue a cut range. ADR 0092 made every
error a problem with a type IRI, and listed the types in the README; the
members a type carries are whatever the handler wrote, and nothing checks
that two handlers agree.

## Decision

### Headers

1. **`Vary: Accept, Varve-As-Of, Authorization`** on every dataset response,
   problems included.
2. **`Cache-Control` by read kind.** An as-of read at a closed position is
   immutable by the model (the bytes at a position never change for a given
   request and representation): `private, max-age=31536000, immutable`.
   `private`, because the response is per caller (ADR 0107). A head read is
   `no-cache` with its `ETag`, so a client revalidates with
   `If-None-Match` and pays one comparison (ADR 0096). A live tail and the
   admin endpoints are `no-store`.
3. **`Last-Modified`** is the resolved position's commit timestamp
   (`Dataset.TimestampAt`), and **`If-Modified-Since`** is honoured as RFC
   9110 §13.1.3 says: `304` when the position's timestamp is at or before
   it, evaluated before any pin is taken, after `If-None-Match` when both
   are present.
4. **`Link`**: `rel="service-desc"` to the dataset's description on every
   dataset response; `rel="next"` on a bounded commits range cut by the
   page size (ADRs 0114, 0118).
5. **W3C Trace Context** (`traceparent`, `tracestate`) is honoured and
   propagated (ADR 0112) and **never written into the log**: `cause` stays
   the server-minted request id (ADR 0094), because a trace id is
   client-chosen, so forgeable and collidable, and spans several requests;
   a reader assuming either property of `cause` would be misled. The join
   lives in telemetry.
6. **`WWW-Authenticate: Bearer`** with `error="invalid_token"` on `401` and
   `error="insufficient_scope"` on `403`, as RFC 6750 §3 says, verified
   against what `JwtBearer` emits (its challenge carries the scheme and,
   when a token was presented and failed, `invalid_token`; the forbid
   carries nothing, so the host adds `insufficient_scope`). Descriptions
   stay short and leak nothing (`IncludeErrorDetails` stays off).
7. **`Allow`** on every `405`.

### The problem catalogue

8. **Every problem type is catalogued**: `ProblemCatalogue` in
   `Varve.Protocol` holds, per `ProblemType`, its `title`, its `status` and
   the names of its extension members, fixed per type. The writer takes a
   catalogue entry, so a handler cannot emit a title, status or member the
   catalogue does not declare; `instance` is the request id on every
   problem.
9. **The members, by type**: `position`, `expectedPosition` and
   `headPosition` for `409` and `412`; `line`, `column` and `offset` for a
   parse error; `graph` for the graph `403`; `limit` and `actual` for every
   `422` of ADR 0114 and `limit` for `429` and `503 server-busy`; `horizon`
   for `below-archive-horizon`; `datasets` for `not-ready`; `report` for
   `rejected`; `head` is renamed `headPosition`.
10. **Every non-2xx is `application/problem+json`**: `404` for an unknown
    dataset and for a route nobody serves, `405` and `415` included, the
    host's fallback for an unmatched route too; `401` deliberately thin (type
    `unauthorized`, no detail).
11. **One page per type in `docs/problems/`**, named by the type's last
    segment, each stating the title, the status, the members and when it
    is emitted. The type IRI under `https://w3id.org/varve/problems/`
    resolves to its page by a redirect the maintainer files in
    `perma-id/w3id.org`; the operator guide names the page until then.
12. **Two tests hold it**: one enumerates every problem the server can emit
    (every `ProblemType` the catalogue knows, exercised through the test
    host) and asserts each is in the catalogue with exactly its declared
    members and that its page exists with the same table; and a property
    that two as-of reads at the same closed position return byte-identical
    bodies and validators, which is what `immutable` promises.

## Alternatives considered

- **`public, immutable` for as-of reads.** A shared cache would serve one
  caller's scoped view to another (ADR 0107).
- **A trace id as `cause`, or beside it in the log.** Rejected in point 5.
- **The catalogue as documentation only.** That is what 0092 had, and
  nothing checked it; a rule only in a document is not a rule.

## Consequences

- `HttpProblems` writes from the catalogue; `ProblemType` gains the
  `unauthorized`, `not-ready`, `server-busy`, `memory-limit-exceeded`,
  `as-of-distance-exceeded` and `too-many-live-tails` types.
- `Dataset` gains `TimestampAt(Position)` for `Last-Modified`.
- The client reads `headPosition` where it read `head`.

## Checks

- **Checked against the accepted ADRs** (0001–0109) and specification 1.6.
  Touches **0092**, **0094**, **0096** (amended), **0107** (`private`),
  **0112** (the join). No conflict beyond the amendments.
- **Layer ownership.** `Varve.Protocol` (5); the `WWW-Authenticate` detail
  and the route fallback `Varve.Server` (6).
- **Analyzer rule.** None.
- **Open questions owned.** None.
