# `operation-failed`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/operation-failed` |
| **Title** | An operation failed; nothing was committed. |
| **Status** | `400` |
| **Members** | none |

An update operation that failed before anything was committed (a `LOAD` the endpoint policy refused, a `CREATE` of a graph that exists), or a query that failed while evaluating, when the failure came before the first byte (ADRs 0095, 0104).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
