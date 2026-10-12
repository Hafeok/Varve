# `as-of-distance-exceeded`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/as-of-distance-exceeded` |
| **Title** | The as-of position is too far from its nearest checkpoint. |
| **Status** | `422` |
| **Members** | `limit`, `actual` |

A `Varve-As-Of` position more than `Varve:Limits:MaxAsOfDistance` commits above its nearest checkpoint, refused before any log is read (ADR 0114). `limit` and `actual` are commits.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
