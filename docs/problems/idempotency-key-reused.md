# `idempotency-key-reused`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/idempotency-key-reused` |
| **Title** | The idempotency key was sent with another request. |
| **Status** | `422` |
| **Members** | `position` |

The write carried an `Idempotency-Key` that the same agent already sent with
a request that committed, and this request is not that one: its method,
path and query, `Content-Type` or body differ (ADR 0246). Nothing was
committed. `position` is the commit that holds the key.

A key names one write. Send a new write with a new key. A retry of the write
the key was first sent with is answered from its commit, with
`Varve-Replayed: true`, so this answer means the client sent two different
writes under one key. That is a defect in the client, not a race.

Keys are scoped to the agent: another caller's key never matches yours. A
key is held while its commit is among the dataset's last
`Varve:Limits:IdempotencyWindow` commits, and once its commit is older, the
key is free again.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
