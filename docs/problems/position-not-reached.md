# `position-not-reached`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/position-not-reached` |
| **Title** | The position is after the head. |
| **Status** | `404` |
| **Members** | `headPosition` |

A `Varve-As-Of` position, a commits range's `to`, a single commit's position or a checkpoint's `at` after the head. `headPosition` is the head (ADR 0096).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
