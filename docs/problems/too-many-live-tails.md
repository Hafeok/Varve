# `too-many-live-tails`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/too-many-live-tails` |
| **Title** | The client holds too many live tails open. |
| **Status** | `429` |
| **Members** | `limit` |

A live tail beyond `Varve:Limits:MaxLiveTailsPerClient` for one client, the token's subject or the remote address (ADR 0114). `limit` is the bound.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
