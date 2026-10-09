# `before-first-commit`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/before-first-commit` |
| **Title** | The time is before the first commit. |
| **Status** | `404` |
| **Members** | none |

A `Varve-As-Of` time before the first commit, or any time in an empty dataset (ADR 0096).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
