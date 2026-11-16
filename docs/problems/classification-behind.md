# `classification-behind`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/classification-behind` |
| **Title** | The classification has not reached the position asked for. |
| **Status** | `503` |
| **Members** | `binding`, `position`, `projectedPosition` |

A row-restricted read at `position` needed a classifier binding's
classification there, and its projection had reached only
`projectedPosition` (ADR 0233). The server waited once, a bounded wait of two
seconds, and the projection was still behind. The response carries
`Retry-After: 1`. Retry, or read a position the projection has reached.

Only a caller whose subject set restricts something reads the projection: an
unrestricted read never waits. A projection's lag is reported per binding on
`/health/ready`, as `classification` beside its dataset's line, and never
makes the dataset unready or behind.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
