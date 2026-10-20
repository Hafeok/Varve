# `dataset-open`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/dataset-open` |
| **Title** | The dataset is open; close it first. |
| **Status** | `409` |
| **Members** | none |

`DELETE /manage/datasets/{name}` while the dataset is declared open; declare it closed first, with `PUT /manage/datasets/{name}/state` (ADRs 0106, 0163). Every instance closes it, and its writer deletes its storage once the declaration is removed.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
