# `request-too-large`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/request-too-large` |
| **Title** | The request body is over the server's limit. |
| **Status** | `413` |
| **Members** | `limit` |

A request body over `Varve:Limits:MaxRequestBody`; `limit` is the bound in bytes (ADR 0095).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
