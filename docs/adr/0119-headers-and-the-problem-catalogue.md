# 0119 — Headers by read kind, and a catalogue of every problem the server can emit

## Status

**Accepted — filed unaccepted by milestone Operability of #12, 2026-10-09**
(ADR 0066). Acceptance is the maintainer's act on the pull request.

**Amends by dated block** [0092](0092-protocol-scope-problem-details-and-tie-breakers.md)
point 5 (the catalogue), [0094](0094-a-write-over-http-is-one-commit.md)
point 3 (the cause and trace context) and [0096](0096-time-travel-over-http.md)
point 4 (the cache headers).

**Amended 2026-10-13** (ADR 0068), by milestone Commit path of #91 under ADR
[0126](0126-graph-versions-over-http.md): point 9's members of `409` and
`412`, and `412`'s title; see the end.

**Amended 2026-10-27** (ADR 0068), by #146 under [ADR 0213](0213-cached-derived-data-is-verified-before-use.md): `load-spill-damaged` joins the catalogue; see the end. Filed unaccepted.

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

## Amendment, 2026-10-13 — `graphs` on `409` and `412`

Filed by milestone Commit path of #91, unaccepted until the maintainer
accepts it (ADR 0066). Point 9 gains: `graphs` and `graphsTruncated` for
`409 conflict` (the intervening commits that touched the request's scope,
each with the graphs of the scope it touched; at most 100, read at most the
as-of bound back), and `graphs` for `412 precondition-failed` (each graph
whose version differed, with its version). `412`'s title becomes "The
precondition does not name the current version.", because a graph's
precondition is not the head's. The catalogue, the two pages and their test
change with it.

**The ledger.** The ruling of this block is `ConflictListsTheGraphsInScope`,
unaccepted until the maintainer accepts it.

## Amendment, 2026-10-15 — three problem types for events, and the validator's path

Filed by milestone Client and integration, part 2, of #93, unaccepted until
the maintainer accepts it (ADR 0066).

- **`event-declaration-invalid`** (`422`, members `declaration`, `reason`) and
  **`event-schema-incompatible`** (`422`, members `declaration`, `fields`):
  a declaration change refused by ADR 0145's validator.
- **`event-not-rendered`** (`422`, members `event`, `eventType`, `reason`):
  never a response, always the data of an in-band CloudEvent (ADR 0143). It is
  catalogued so that its title, status and members are fixed like every other.
- **The validator's path.** A dataset validator's rejection whose report
  starts with the IRI of a type the catalogue marks as a validator's, followed
  by an `rdf:JSON` literal of its members, is written as that type instead of
  `rejected`. Every other rejection is `rejected`, as point 9 says.

**The ledger.** The ruling of this block is `EventProblemTypes`, unaccepted
until the maintainer accepts it.

## Amendment, 2026-10-15 — `writer-superseded`

Filed by milestone Object storage of #92, unaccepted until the maintainer
accepts it (ADR 0066). The catalogue gains `writer-superseded`, `503`, no
members, titled "A newer writer holds the dataset; this instance serves reads
only." A Graph Store write or a SPARQL Update answers with it when the
dataset's storage no longer holds the writer epoch (ADR 0136); nothing was
committed. The page is `docs/problems/writer-superseded.md`, and the catalogue
test lists it as exercised by the server's object-store leg. Point 9's
`as-of-distance-exceeded` counts the log an as-of read replays, which is none
below the newest extent set (ADR 0138).

**The ledger.** The ruling is ADR 0136's `WriterSupersededProblem`.

## Amendment, 2026-10-20 — three problem types for shapes bindings

Filed by SHACL, part 2a (#138, under milestone 8, #13), unaccepted until the
maintainer accepts it (ADR 0066).

- **`shapes-violated`** (`422`, members `binding`, `shapesGraph`, `findings`,
  `report`, `shapesChanged`): a commit refused by a `reject` binding (ADR
  [0169](0169-commit-time-gating-under-a-shapes-binding.md)).
- **`binding-not-incremental`** (`422`, members `binding`, `shapes`): a
  `reject` binding whose shapes include a full shape without
  `varve:allowFullEvaluation`, at apply time or failing closed at commit time.
- **`shapes-invalid`** (`422`, members `binding`, `problems`): a binding whose
  shapes graph is refused.

All three are validator types: they reach a response by the validator's path
of the 2026-10-15 block above.

**The ledger.** The ruling is ADR 0169's `ShapesBindingProblemTypes`.

## Amendment, 2026-10-23 — `validation-behind`, and a guard's `graph-not-writable`

Filed by SHACL, part 2b (milestone 8, #13), unaccepted until the maintainer
accepts it (ADR 0066).

- **`validation-behind`** (`503`, members `binding`, `position`,
  `projectedPosition`): a read of a report graph at a position its binding's
  validation projection has not reached, after one bounded wait (ADR
  [0174](0174-the-validation-projection.md)). The response carries
  `Retry-After: 1`. Past the first byte it is the `Varve-Error` trailer, as
  every read limit is.
- **`graph-not-writable` is also a validator type**, so that
  `ReportGraphGuard`'s refusal of a write to a report graph reaches the
  response as that type, with `graph`, by the validator's path of the
  2026-10-15 block above.

**The ledger.** The rulings are ADR 0174's `ValidationBehind` and
`ReportGraphNotWritable`.

## Amendment, 2026-10-27 — `load-spill-damaged`

Filed by #146, unaccepted until the maintainer accepts it (ADR 0066); ruled
in [ADR 0213](0213-cached-derived-data-is-verified-before-use.md) point 5.

- **`load-spill-damaged`, `503`.** A bulk load's spill failed its check.
  The load ended before its commit, and nothing of it is visible. The input
  can be resubmitted as it is.
- No HTTP route runs a bulk load today. `varve load` names the type, and
  any route that later runs a load answers with it.
- The page, the catalogue entry and the README row follow the rules above.

## Amendment, 2026-10-27 — `entailment-unsupported` and `entailment-refused`

Filed by Inference, part 1 (#170, under #95), unaccepted until the maintainer
accepts it (ADR 0066).

- **`entailment-unsupported`** (`400`, members `value` and `supported`). It
  answers:
  - a `Varve-Entailment` header this server does not serve: an unknown token
    or IRI, more than one value, or a regime whose part has not landed;
  - a regime other than simple on an update request.

  The rules are those of ADR [0192](0192-entailment-regimes-over-http.md).
- **`entailment-refused`** (`400`, member `reason`): the regime's
  `QueryRequestRefused` (SPARQL 1.1 Entailment Regimes §1.4), such as a
  query illegal under OWL 2 Direct semantics or an inconsistent ontology
  under it (`reason` `inconsistent`). It is catalogued now and emitted by no
  tier-1 regime: RDF, RDFS and D never refuse a query. #95's later parts
  emit it.
- `Varve-Entailment` is a request and a response header of the query
  endpoints, beside `Varve-As-Of`. The `Vary` every dataset response
  carries names it, problems included, so the list stays one list.

**The ledger.** The rulings are ADR 0192's `EntailmentHeader` and
`UpdatesAreSimple`.
