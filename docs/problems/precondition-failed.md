# `precondition-failed`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/precondition-failed` |
| **Title** | If-Match does not name the head. |
| **Status** | `412` |
| **Members** | `position`, `expectedPosition`, `headPosition` |

An `If-Match` that does not name the head, checked before anything is read or parsed. `headPosition` and `position` are the head; `expectedPosition` is what `If-Match` named (ADR 0094).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
