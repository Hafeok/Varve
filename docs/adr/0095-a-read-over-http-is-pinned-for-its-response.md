# 0095 — A read over HTTP is pinned for its response, bounded, and cut visibly

## Status

**Accepted — filed unaccepted by milestone 7a of #11, 2026-10-07** (ADR 0066).
Acceptance is the maintainer's act on the pull request. Refines ADR 0052's
fourth point, which left the bound to "the server, at milestone 7".

## Context

ADR 0052 fixes that a pinned read lives for one query execution, that the
caller disposes it when the result stream ends, and that the server enforces a
maximum lifetime with cancellation. Over HTTP the result stream ends when the
response body has been written. That is after the endpoint's handler returns
whenever the body streams. A result cut off half-way must not look like a
complete one: a client that reads a well-formed JSON prefix of a result set
cannot know that rows are missing.

## Decision

1. **The pin is taken when the endpoint starts and released when the response
   has finished**, through `HttpResponse.RegisterForDisposeAsync`. ASP.NET Core
   disposes what is registered after the last byte has been written or the
   request has been aborted. The same holds for an as-of view (ADR 0096): it
   too is held for the response and released with it.
2. **A read is bounded twice.**
   - `Limits:QueryTimeout` bounds evaluation.
   - `Limits:PinnedReadLifetime` bounds how long the pin may be held, writing
     included.

   Both come from the injected `TimeProvider`. They are linked with
   `RequestAborted` into the token the evaluator and the writers observe. A
   client that goes away cancels the read.
3. **`Limits:ResultSizeCap`** bounds the bytes a read may write. Reaching it is
   the same as reaching a time limit.
4. **A read that is cut is cut visibly.**
   - **Before the response has started**: `503` with the problem
     `read-limit-exceeded`, and the pin is released.
   - **After the response has started**, when the response supports trailers
     (HTTP/2, or HTTP/1.1 chunked): the trailer `Varve-Error` carries the
     problem type's IRI, and the body ends there. Clients that read trailers
     know; clients that do not read them still see a document that may be
     malformed. The next point closes that gap.
   - **The feed and the diff** end with an in-band `error` record that names
     the problem type (ADR 0097), because a line format can carry one.
   - **Otherwise the connection is aborted** (`HttpContext.Abort()`), so the
     transfer is visibly incomplete: no terminating chunk on HTTP/1.1, and a
     reset stream on HTTP/2. A short result that looks whole is never sent.
5. **A live feed is not a read for these limits.** It holds no pin between
   commits: it is a subscription (ADR 0042). Each delivered commit is
   externalised and written, and `PinnedReadLifetime` does not apply.
   Cancellation and shutdown end it (ADR 0101).

## Alternatives considered

- **Buffer the whole response, then send it.** A cut becomes a clean `503`
  every time, and a million-row result needs a million rows of memory. ADR 0052
  rejected materialising for the same reason.
- **An in-band error for every format.** XML could carry a comment, and CSV and
  TSV nothing at all; a JSON result has no place for one that a standard client
  would read. Trailers plus an abort are the honest general answer.
- **Let the pin outlive its limit and only cancel evaluation.** A slow reader
  would then hold an index version for as long as it liked. That is the leak
  ADR 0015 names.

## Consequences

- No pin outlives its response, its limit, or its client, by construction. The
  test is that a response the client abandons releases its pin; it does.
- A client that wants certainty about completeness reads the trailer, or relies
  on the transfer completing.

## Checks

- **Checked against the accepted ADRs** (0001–0090) and specification 1.5.
  Touches:
  - **0015** and R1 (a pin is per operation);
  - **0052** (refined: the operation's end is the response's end, and the
    bound is the server's);
  - **0042** (a feed is a subscription, not a pin).

  No conflict.
- **Layer ownership.** `Varve.Protocol` (5); the limits' values are
  `Varve.Server` configuration (6).
- **Analyzer rule.** None.
- **Open questions owned.** None.
