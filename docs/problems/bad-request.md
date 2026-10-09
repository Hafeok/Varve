# `bad-request`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/bad-request` |
| **Title** | The request is not one the protocol accepts. |
| **Status** | `400` |
| **Members** | none |

A request the protocol does not accept: a missing or repeated parameter, a malformed header such as `Varve-As-Of`, `If-Match` or `Last-Event-ID`, `using-graph-uri` together with `USING`, a body that is not the JSON an admin endpoint takes, or a `GET` that asks for the store itself (ADR 0092).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
