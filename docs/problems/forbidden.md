# `forbidden`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/forbidden` |
| **Title** | The caller does not hold the permission the request needs. |
| **Status** | `403` |
| **Members** | none |

An authenticated caller without the permission the request needs on the dataset it names (ADRs 0037, 0091, 0106). `WWW-Authenticate: Bearer error="insufficient_scope"` (RFC 6750 §3.1).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
