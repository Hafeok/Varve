# `not-found`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/not-found` |
| **Title** | Nothing is served at that address. |
| **Status** | `404` |
| **Members** | none |

An address nothing is served at: a path outside every dataset and the health endpoints (ADR 0119).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
