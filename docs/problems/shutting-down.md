# `shutting-down`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/shutting-down` |
| **Title** | The server is shutting down. |
| **Status** | `503` |
| **Members** | none |

A write during graceful shutdown, or the end of a live tail when the host stops (ADR 0101).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
