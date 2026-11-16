# `shutting-down`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/shutting-down` |
| **Title** | The server is shutting down. |
| **Status** | `503` |
| **Members** | none |

A write during graceful shutdown, or the end of a live tail when the host stops (ADR 0101).

The `detail` says that this instance is draining: what it was doing completes,
and it takes no new write. In a fleet it adds that the other instances keep
serving (ADR 0242).

## What to do

- **A client:** send the write again. In a fleet it reaches another instance; behind one address, a retry after a short wait does. A live tail resumes from its last position on another instance.
- **An operator:** nothing to fix. A rolling restart drains one instance at a time ([Deployment](../operator/fleet.md#deployment)).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
