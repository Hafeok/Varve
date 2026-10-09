# `dataset-exists`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/dataset-exists` |
| **Title** | A dataset by that name exists. |
| **Status** | `409` |
| **Members** | none |

`PUT /datasets/{name}` of a name in use with a body asking for another storage; the same body again is `204` (ADRs 0106, 0118).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
