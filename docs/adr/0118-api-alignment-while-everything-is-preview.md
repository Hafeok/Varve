# 0118 — API alignment while everything is preview: every path a resource, every status code what HTTP says

## Status

**Proposed — filed unaccepted by milestone Operability of #12, 2026-10-09**
(ADR 0066). Decided by the maintainer on the Operability plan; the proposed
`410 Gone` for the archive horizon was **reversed** on review (RFC 9110
§15.5.9 reserves `410` for conditions likely to be permanent; archiving is
undecided), so [0096](0096-time-travel-over-http.md) stands and this ADR
leaves the horizon alone. Acceptance is the maintainer's act on the pull
request.

**Amends by dated block** [0093](0093-datasets-are-the-routing-unit.md)
(the route table), [0097](0097-the-change-feed-and-the-diff.md) (the feed
is the commits resource) and [0106](0106-the-admin-api.md) (state and
settings are resources, `PUT` is idempotent). `docs/spec/change-feed.md` §4
follows.

## Context

7a and 7b grew the API in slices, and three of its paths are verbs:
`POST …/open`, `POST …/close` and `POST …/settings`. The feed is a path of
its own, and a single commit — which provenance needs, since every quad has
the commit that introduced it — has no address. `PUT /datasets/{name}` of a
dataset that exists is `409` whatever the body says, so a deployment script
cannot re-run. Every package is preview and renames are free now; after 1.0
each of these is a major version.

The W3C-fixed parts (`/sparql`, `/graphs`, the service description) stay as
they are.

## Decision

1. **State is a resource.** `GET /datasets/{name}/state` answers
   `{"state":"open"|"closed"|"failed","reason":…}` for a server admin.
   `PUT /datasets/{name}/state` with `{"state":"open"}` or `"closed"` opens
   or closes, **idempotently**: the state asked for is the state answered,
   `204` whether or not anything changed; a `failed` dataset asked to open is
   `503 unavailable` with the reason, as the old `open` was. `POST …/open`
   and `…/close` are removed.
2. **Settings are a resource.** `GET /datasets/{name}/settings` answers the
   settings at the head with the position as `ETag`; `PUT` replaces them
   whole and `PATCH` (`application/merge-patch+json`, RFC 7396) merges, both
   under `If-Match` as ADR 0094's expected position, both answering `204`
   with `Varve-Position`; the `Settings` commit is the effect, not the verb.
   `POST …/settings` is removed.
3. **The feed is the commits resource.**
   - `GET /datasets/{name}/commits?from=&to=` is the range in the delta
     format, with `from`, `fromTime`, `to`, `toTime`, `graph` and `pattern`
     as the feed had them, paged by `Limits:CommitsPageSize` with
     `Link rel="next"` (ADR 0114).
   - `GET /datasets/{name}/commits/{position}` is one commit as one record
     of the delta format, `404 position-not-reached` above the head;
     `If-None-Match` and the immutable cache headers apply (ADR 0119).
   - **Live tailing is `Accept: text/event-stream` on an open range**
     (no `to`), with `Last-Event-ID` as before; the plain format on an open
     range tails live too, as 0097 allowed. There is no separate path.
   - `GET …/feed` is removed; `/diff` stays.
4. **`PUT /datasets/{name}` is idempotent.** The same body again (the same
   storage) is `204`; a different body is `409 dataset-exists`; a first
   creation stays `201`.
5. **The old paths are removed, not aliased.** The change is listed under
   breaking changes in the release descriptor's summary. The client
   (`SparqlHttpClient`), the CLI, the service description
   (`varve:commits`, `varve:state`; `varve:changeFeed` removed) and the
   operator guide follow.

## Alternatives considered

- **Keep the verbs as aliases for a release.** Two surfaces to test and
  document, and nothing on nuget.org depends on the old ones yet.
- **`POST /datasets/{name}/commits` to write a commit.** Tempting symmetry;
  a commit is written by the protocols that compute its delta (ADRs 0057,
  0094), never posted as a delta.
- **`410` below the horizon.** Reversed, above.

## Consequences

- Problem types: none added; `dataset-exists` gains the idempotent case.
- `Varve.Protocol.Client` renames `FeedAsync` to `CommitsAsync`, gains
  `CommitAsync(position)`, `StateAsync`, `SetStateAsync`, `SettingsAsync`,
  `PutSettingsAsync`, `PatchSettingsAsync`; `varve feed` keeps its name and
  calls `commits`.

## Checks

- **Checked against the accepted ADRs** (0001–0109) and specification 1.6.
  Touches **0093**, **0097**, **0106** (amended), **0094** (`If-Match`),
  **0096** (left standing). No conflict beyond the amendments.
- **Layer ownership.** `Varve.Protocol` (5); the client (5); the CLI (6).
- **Analyzer rule.** None.
- **Open questions owned.** None.
