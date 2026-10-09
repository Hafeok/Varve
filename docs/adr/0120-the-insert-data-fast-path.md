# 0120 — The `INSERT DATA` fast path: ground quads to the commit request

## Status

**Proposed — filed unaccepted by milestone Operability of #12, 2026-10-09**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Answers
#35; builds on ADR 0057 as amended 2026-10-08 (a data-only request expects no
position).

## Context

#35 measured `INSERT DATA` of 100,000 quads into an empty store at 475 ms
against pyoxigraph's 392 ms, parsing included, and located about 155 ms in
the executor: staging every term through the staging view, the asserted and
retracted hash sets, sorting the delta and building the request. For a
request that evaluates no pattern there is nothing to evaluate, so most of
that is bookkeeping for a `WHERE` that does not exist.

ADR 0057's amendment already made such a request independent of the pinned
state: its delta is composed from the text alone and the sequencer
normalises it against the head it meets (I2).

## Decision

1. **A request whose operations are all `INSERT DATA` or `DELETE DATA`, with
   no expected position, takes a fast path**: its ground quads are written
   to the `CommitRequest` as request terms (`Assert`/`Retract` with
   `RdfTerm`s), in operation order, with no staging view, no asserted or
   retracted set and no sort. A blank-node label is one fresh blank node per
   label per request (SPARQL Update §3.1.1; ADR 0098), as before. Triple
   terms go in as request terms too.
2. **ADR 0057 holds**: one request, one commit, the effective delta, because
   the sequencer composes the chain and drops what the head already holds
   or does not hold (I2), which is the normalisation the executor used to
   perform against the pin. **ADR 0058 holds**: no handle is staged, so
   nothing reaches the dictionary but through the commit.
3. **The validators run as always**: the request carries the dataset's
   validators, and a `Rejected` outcome is answered as before.
4. **A request with an expected position, a scoped writer, or any other
   operation** takes the existing path. The write scope check needs the
   graph of each quad, which the fast path has before it writes, so a scoped
   writer (ADR 0107) takes the fast path too once the check is made on the
   request terms.
5. **Done when the median is at or below pyoxigraph's 392 ms on the same
   machine in the same run**, parsing included; if it is not reached, the
   report says where the remaining time is and #35 stays open with that
   finding.

## Alternatives considered

- **Optimise the staging view instead.** Every request would gain a little;
  the request that needs no staging would still pay for it.
- **Parse `INSERT DATA` straight into a commit request in the parser.**
  Fast, and the parser would know the store, which ADR 0005's spirit and the
  layering forbid.

## Consequences

- `RequestExecution` gains the path; `SparqlUpdate.ExecuteAsync` picks it
  before pinning.
- The 94 update cases, the one-commit assertion and the reference-model
  property guard it; the benchmark README gains the row, with pyoxigraph in
  the same run.

## Checks

- **Checked against the accepted ADRs** (0001–0109) and specification 1.6
  (I2). Touches **0057** (as amended), **0058**, **0098**, **0107**. No
  conflict.
- **Layer ownership.** `Varve.Sparql.Store`, layer 5.
- **Analyzer rule.** None.
- **Open questions owned.** None.
