# `server-busy`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/server-busy` |
| **Title** | The server has as many reads in flight as it allows. |
| **Status** | `503` |
| **Members** | `limit` |

As many reads in flight and queued as `Varve:Limits:MaxConcurrentReads` and `ReadQueueLength` allow; `Retry-After` says when to try again and `limit` is the concurrency bound (ADR 0114).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
