# `unsupported-media-type`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/unsupported-media-type` |
| **Title** | The request body is in a media type or charset this endpoint does not read. |
| **Status** | `415` |
| **Members** | none |

A request body in a media type or charset the endpoint does not read, UTF-8 being the only charset (ADR 0092).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
