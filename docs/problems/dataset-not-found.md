# `dataset-not-found`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/dataset-not-found` |
| **Title** | No dataset by that name. |
| **Status** | `404` |
| **Members** | none |

A dataset name that is not valid or names no open dataset. The two are one answer, so an unauthorised caller cannot probe which names exist (ADR 0093).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
