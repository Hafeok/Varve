# `memory-limit-exceeded`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/memory-limit-exceeded` |
| **Title** | The request needs more memory than the server allows one request. |
| **Status** | `422` |
| **Members** | `limit`, `actual` |

An evaluation whose materialising operators charged more than `Varve:Limits:MaxQueryMemory`, counted, not collected (ADR 0114). `limit` and `actual` are bytes. After the first byte the same type arrives in the `Varve-Error` trailer (ADR 0095).

## What to do

- A client: narrow the query, so its joins, sorts, groups and `DISTINCT` hold
  fewer rows. Sending the same query again is refused again.
- An operator: raise `Varve:Limits:MaxQueryMemory` if such queries are
  expected ([Configure](../operator/configure.md)).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
