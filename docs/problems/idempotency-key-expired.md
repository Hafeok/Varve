# `idempotency-key-expired`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/idempotency-key-expired` |
| **Title** | Whether a write with this idempotency key committed cannot be determined. |
| **Status** | `422` |
| **Members** | `keyPosition`, `headPosition`, `window` |

The write is a retry: it carried an `Idempotency-Key` and a
`Varve-Key-Position`, the head the client knew when it made the key
(ADR 0246). No commit in the dataset's window holds the key. But the head,
`headPosition`, is more than `window` commits past `keyPosition`, so the
original may have committed at a position the window no longer covers.
Whether it did cannot be determined, so nothing was committed.

## What to do

A client: read the dataset to find out whether the original's effect is
there, then, if the write is still wanted, send it as a new write with a new
key. Do not send it again with this key: it will be refused the same way.
`Varve.Protocol.Client` sends `Varve-Key-Position` only on a retry, so a
first attempt is never answered this way.

An operator whose clients meet this often: their retries come later than the
window allows. Raise `Varve:Limits:IdempotencyWindow`
([Configure](../operator/configure.md)), at about 56 bytes of the writer's
memory per keyed commit ([Run on a small machine](../operator/small-machine.md)).

The window is the server's `Varve:Limits:IdempotencyWindow`, 100,000 commits
by default, counted in commits and never time.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
