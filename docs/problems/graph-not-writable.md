# `graph-not-writable`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/graph-not-writable` |
| **Title** | The request changes a graph outside the caller's writable scope. |
| **Status** | `403` |
| **Members** | `graph` |

A write that changes a graph outside the caller's writable scope (ADR 0107). `graph` names it, `default` for the default graph; nothing was committed.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
