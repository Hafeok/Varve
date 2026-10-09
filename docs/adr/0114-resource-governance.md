# 0114 — Resource governance: a stated answer for every limit

## Status

**Proposed — filed unaccepted by milestone Operability of #12, 2026-10-09**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Refines
[0095](0095-a-read-over-http-is-pinned-bounded-and-cut-visibly.md) (the 7a
limits) and ADR 0052's "the server enforces".

## Context

7a bounded a read by time, pin lifetime and result size, and a request body
by size (ADR 0095). What it did not bound: how many reads run at once, how
much memory one evaluation may materialise, how far an as-of read may replay
from its nearest checkpoint (6c measured 0.2–0.3 ms a commit, so 10,000
commits is about three seconds and an unbounded log is an unbounded read),
and how many live tails one client may hold open. The roadmap's own words: on
an event-sourced store this is not a nicety.

## Decision

Every limit has a default, a problem type under
`https://w3id.org/varve/problems/` (ADR 0119), a line in the operator guide,
and a property test that hitting it produces its problem type and never a
timeout or a disconnect.

1. **A concurrent-read limit per server with a bounded queue.**
   `Varve:Limits:MaxConcurrentReads` (default 64) reads run at once;
   `Varve:Limits:ReadQueueLength` (default 256) wait; the next is refused
   with **`503 server-busy`** and `Retry-After: 1`. Built on the shared
   framework's `ConcurrencyLimiter` as an endpoint rate-limiting policy on
   the read endpoints, with the problem written by our rejection handler. A
   write is bounded by the sequencer already (one at a time per dataset) and
   is not queued here.
2. **A per-request memory bound on the evaluator's materialising
   operators**, enforced by counting, not by the collector:
   `Varve:Limits:MaxQueryMemory` (default 256 MiB) becomes
   `EvaluationOptions.MemoryBudget`, a byte count the join table, the sort
   table, the group table and the distinct set charge as they grow (rows
   times slot width, plus the bytes of the terms an accumulator holds). A
   charge over the budget throws `MemoryBudgetExceededException`, a
   `QueryEvaluationException`, which the protocol answers as **`422
   memory-limit-exceeded`** with `limit` and `actual`. `422` and not `413`:
   the request body was fine; a valid request is refused by policy. The same
   budget bounds an update's patterns through `UpdateOptions`.
   - Before the first byte (ORDER BY, GROUP BY, a hash join's build side,
     which all materialise before any row is written) the answer is the
     problem alone. After it (DISTINCT, which accumulates while streaming)
     the cut is ADR 0095's: the `Varve-Error` trailer names the type.
   - The property: a request over the bound fails before the process's
     working set has grown by more than the bound plus a stated constant
     (64 MiB, the test's allowance for the collector and the response
     buffers).
3. **An as-of read bound as a maximum log distance from the nearest
   checkpoint.** `Varve:Limits:MaxAsOfDistance` (default 10,000 commits): a
   `Varve-As-Of` whose position lies more than that many commits above the
   nearest checkpoint at or below it, or above position 0 when there is
   none, is **`422 as-of-distance-exceeded`** with `limit` and `actual`,
   decided from `Dataset.Checkpoints` **before the view is opened**, so an
   as-of read never scans an unbounded log. The service description
   advertises the bound (`varve:maxAsOfDistance`), so a client can take a
   checkpoint, or pick a position a checkpoint covers, before it asks.
4. **A bound on open live tails per client.** `Varve:Limits:MaxLiveTailsPerClient`
   (default 16): the client is the token's subject when there is one and the
   remote address otherwise; the next tail is **`429 too-many-live-tails`**
   with `limit`. Bounded ranges are reads and fall under point 1.
5. **A page size for a bounded commits range.** `Varve:Limits:CommitsPageSize`
   (default 1,000): a bounded `GET …/commits` serves at most that many
   commits and, when the range holds more, carries `Link: <…>; rel="next"`
   with the next `from`, decided before the first byte so that the header can
   be sent (ADR 0118). The result size cap stays as the backstop and cuts as
   ADR 0095 says.
6. **The service description advertises the limits that shape a client's
   expectations**: `varve:resultSizeCap`, `varve:maxAsOfDistance`,
   `varve:commitsPageSize`. Timeouts and concurrency are the operator's.

## Alternatives considered

- **Let the collector bound memory** (a heap limit per process). It bounds
  the process, not the request: one query would take the whole server down
  with it, and the failure would be an `OutOfMemoryException` at a random
  allocation, not a problem naming the limit.
- **`413` for the memory bound.** `413 Content Too Large` is about the
  request's representation (RFC 9110 §15.5.14); the request here was small.
- **An as-of bound in time** (a maximum replay duration). Machine-dependent;
  a distance in commits is the quantity R2 names and the one a checkpoint
  policy controls.
- **Limit live tails per server instead of per client.** One client could
  then take every slot; the server-wide bound is the connection limit Kestrel
  already has.

## Consequences

- `Varve.Sparql.Evaluation` gains `MemoryBudget` and the exception on its
  public surface; the four operators count. Counting costs an addition per
  row materialised, measured by the allocation and benchmark rows.
- `ProtocolLimits` gains the five values; `ProtocolOptions` gains the client
  identity for tails through the existing `ICallerIdentity`.
- The operator guide's limits page shows what each failure looks like to a
  client.

## Checks

- **Checked against the accepted ADRs** (0001–0109) and specification 1.6
  (R2, the as-of cost). Touches **0052**, **0095** (refined), **0097** (a
  bounded range is paged), **0096** (the service description). No conflict.
- **Layer ownership.** The budget is `Varve.Sparql.Evaluation` (3); the
  limits and their problems `Varve.Protocol` (5); the values `Varve.Server`
  (6).
- **Analyzer rule.** None. The counting is not on a `[HotPath]` member: it
  is charged when a table grows, per row materialised.
- **Open questions owned.** None.
