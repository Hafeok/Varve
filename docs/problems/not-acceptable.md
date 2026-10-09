# `not-acceptable`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/not-acceptable` |
| **Title** | Nothing in Accept is a format this endpoint writes. |
| **Status** | `406` |
| **Members** | none |

Nothing in `Accept` that the endpoint can write (ADR 0092).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
