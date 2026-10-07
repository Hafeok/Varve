---
set: a-read-over-http-is-pinned-for-its-response
namespace: varve
adr: 0095
decisions:
  - key: PinHeldForTheResponse
    statement: "A read's pin or as-of view is taken when the endpoint starts and released when the response has finished, through RegisterForDisposeAsync"
  - key: ReadBoundedTwice
    statement: "A read is bounded by QueryTimeout for evaluation and PinnedReadLifetime for the pin, from the injected clock, linked with the request's abort"
  - key: ResultSizeCap
    statement: "ResultSizeCap bounds the bytes a read writes, and reaching it is reaching a limit"
  - key: CutReadsAreVisible
    statement: "A cut read is a 503 problem before the response starts, a Varve-Error trailer where trailers are supported, an error record in the line format, and an aborted connection otherwise"
  - key: LiveFeedIsNotAPinnedRead
    statement: "A live feed holds no pin between commits, and PinnedReadLifetime does not apply to it"
---

The rulings of [ADR 0095](../adr/0095-a-read-over-http-is-pinned-for-its-response.md), filed unaccepted by milestone 7a of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
