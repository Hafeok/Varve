# `conflict`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/conflict` |
| **Title** | Another commit came first. |
| **Status** | `409` |
| **Members** | `position`, `expectedPosition`, `headPosition` |

The sequencer's `Conflict`: another commit came first at the position the write expected. `headPosition` and `position` are the head; `expectedPosition` is what `If-Match` named, when it did (ADRs 0011, 0094).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
