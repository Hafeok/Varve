# 0113 — Health and readiness are different questions: `/health/live`, `/health/ready`

## Status

**Proposed — filed unaccepted by milestone Operability of #12, 2026-10-09**
(ADR 0066). Acceptance is the maintainer's act on the pull request.
**Amends [0101](0101-the-server-configuration-aot-shutdown-readiness.md)**
point 5 by a dated block: the paths move, readiness gains a lag and a problem
body, and both are rate-limited.

## Context

ADR 0101 gave the server `/live` and `/ready` with a JSON body of each
dataset's state and said the full health model was this milestone's. An
orchestrator asks two questions and acts differently on each answer: a failed
liveness probe restarts the process; a failed readiness probe stops routing
to it. Readiness that consults a dataset is right; liveness that does is a
restart loop waiting for a slow open.

## Decision

1. **`GET /health/live`** answers `200` once the host is listening, consults
   no dataset, and never answers anything else while the process serves. Its
   body is `{"status":"live"}`.
2. **`GET /health/ready`** answers `200` when every configured and
   discovered dataset that should be open is open, no default projection is
   in the failed state (spec §7), and each default projection is within
   `Varve:Health:ReadyLag` positions of its head (default `0`: at head).
   Otherwise it is **`503` with an RFC 9457 problem** of type `not-ready`
   whose `datasets` member lists each dataset that fails and why (`failed`
   with the reason, `behind` with its lag, `opening`, `draining`). A dataset
   closed by the admin API is listed as `closed` and does not count against
   readiness.
3. **Readiness becomes false during graceful shutdown**, from
   `ApplicationStopping` and before the listener closes, so a load balancer
   that probes stops routing while in-flight requests drain (ADR 0101 point
   4). Liveness stays true to the end: the process is up.
4. **Both are unauthenticated and rate-limited** by the shared framework's
   rate limiter, a fixed window per client address (`Varve:Health:RateLimit`,
   default 60 a minute), `429` beyond it. **Neither reveals dataset
   contents**: names and states, nothing from the log.
5. **The per-dataset status endpoint** (ADR 0106) stays the detailed,
   authenticated view; readiness is the summary an orchestrator may read.

## Alternatives considered

- **`Microsoft.Extensions.Diagnostics.HealthChecks`.** A package (in the
  shared framework for ASP.NET Core, but its `MapHealthChecks` writes a text
  body), a registration model for one check, and a body format that is not
  a problem. Two handlers over what the host already knows is less.
- **Readiness true when any dataset is open.** A server serving half its
  datasets would receive traffic for the other half and answer `503` per
  request; readiness is for all of them.
- **No rate limit on unauthenticated endpoints.** A probe is cheap, but an
  unauthenticated endpoint on the open port is the one a scanner hits
  first.

## Consequences

- `eng/server-smoke.cs`, the operator guide and the tests move to the new
  paths; the old ones are removed, not aliased, and the descriptor lists the
  change under breaking changes.
- The container (ADR 0111) and the Aspire resource (ADR 0117) name
  `/health/ready` as the health check.
- The readiness property: for generated histories with a projection-lag seam,
  readiness is false exactly while any projection is behind by more than the
  configured lag, and false during drain before the listener closes.

## Checks

- **Checked against the accepted ADRs** (0001–0109) and specification 1.6
  (§7, the failed state). Touches **0101** (amended), **0106** (status
  stays). No conflict.
- **Layer ownership.** `Varve.Server`, layer 6: the host knows its datasets.
  The problem type is `Varve.Protocol`'s catalogue (ADR 0119).
- **Analyzer rule.** None.
- **Open questions owned.** None.
