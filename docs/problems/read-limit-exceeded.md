# `read-limit-exceeded`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/read-limit-exceeded` |
| **Title** | The read was cut by a server limit. |
| **Status** | `503` |
| **Members** | none |

A read cut by `Varve:Limits:QueryTimeout`, `PinnedReadLifetime` or `ResultSizeCap` before the first byte; after it the type arrives in the `Varve-Error` trailer or ends a commits stream as an `error` record (ADR 0095).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
