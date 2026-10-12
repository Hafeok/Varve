# `dataset-open`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/dataset-open` |
| **Title** | The dataset is open; close it first. |
| **Status** | `409` |
| **Members** | none |

`DELETE /datasets/{name}` while the dataset is open; close it first (ADR 0106).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
