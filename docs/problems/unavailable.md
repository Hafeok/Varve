# `unavailable`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/unavailable` |
| **Title** | The dataset cannot answer now. |
| **Status** | `503` |
| **Members** | none |

The dataset cannot answer now: its projection failed, it is closing, or a dataset asked to open did not (ADRs 0094, 0106).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
