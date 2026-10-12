# `too-many-requests`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/too-many-requests` |
| **Title** | The client sent more requests than the endpoint allows in its window. |
| **Status** | `429` |
| **Members** | none |

More probes of `/health/live` or `/health/ready` from one client address than `Varve:Health:RateLimit` allows a minute (ADR 0113). The health endpoints are unauthenticated, so they are the ones a scanner hits first; the limit is per address and the window a minute.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
