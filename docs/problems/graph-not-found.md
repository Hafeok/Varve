# `graph-not-found`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/graph-not-found` |
| **Title** | No graph by that name holds a quad. |
| **Status** | `404` |
| **Members** | none |

A Graph Store `GET`, `PUT` or `DELETE` of a named graph that holds no quad, or one outside the caller's readable scope, which is answered the same (ADRs 0092, 0107).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
