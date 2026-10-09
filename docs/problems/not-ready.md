# `not-ready`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/not-ready` |
| **Title** | The server is not ready. |
| **Status** | `503` |
| **Members** | `datasets` |

`GET /health/ready` while a dataset that should be open is not, a projection is failed or behind `Varve:Health:ReadyLag`, or the host is draining; `datasets` lists each with its state and reason (ADR 0113).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
