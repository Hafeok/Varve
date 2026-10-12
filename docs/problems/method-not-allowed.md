# `method-not-allowed`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/method-not-allowed` |
| **Title** | The endpoint does not serve this method. |
| **Status** | `405` |
| **Members** | none |

A method the endpoint does not serve. `Allow` lists the ones it does (ADR 0092).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
