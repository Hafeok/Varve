# `idempotency-key-in-flight`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/idempotency-key-in-flight` |
| **Title** | A request with this idempotency key is still being answered. |
| **Status** | `409` |
| **Members** | none |

Another request from the same agent, with the same `Idempotency-Key`, is
being evaluated or committed by the dataset's writer now (ADR 0246). Nothing
was committed for this one. The response carries `Retry-After: 1`.

## What to do

A client: send the same request again, with the same key, after
`Retry-After`. If the first request committed, the retry is answered from its
commit, with `Varve-Replayed: true`. If it did not, the retry is evaluated as
a new write. `Varve.Protocol.Client` does this by itself.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
