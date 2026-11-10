# `writer-superseded`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/writer-superseded` |
| **Title** | A newer writer holds the dataset; this instance serves reads only. |
| **Status** | `503` |
| **Members** | `writer`, `epoch` |

The dataset is in an object store, and another instance has claimed its
writer epoch since this one opened it (ADR 0136): a restart elsewhere, a
second replica, or an operator's `varve lease --take`. This instance's next
write found the log's next slot taken, so the write was refused and nothing
was committed. The instance stays open and serves reads; it never claims the
epoch again on its own, and `/health/ready` reports the dataset `superseded`.
Send writes to the instance that holds the epoch; `varve lease` names it.

In a fleet, the body names that instance (ADR 0242): `writer` is the address
the fleet now routes the dataset's writes to, read again when the write was
refused, and `epoch` the epoch it holds. A fleet of one has no other writer to
name, and leaves both out.

## What to do

- **A takeover or a restart elsewhere** (an operator's `varve lease --take`, a replica that started): nothing is wrong. Send writes to `writer`, or through any instance of the fleet, which forwards them ([Where a write goes](../operator/fleet.md#where-a-write-goes-forwarding)). Retrying here is refused again.
- **Two instances that both claim the dataset**, each taking the epoch in turn: their configuration disagrees. Usually two instances have `Varve:Fleet:Writer` set to `true`, or the dataset is placed on both. Reconfigure so one writes it ([The system dataset](../operator/fleet.md#the-system-dataset), [placement](../operator/fleet.md#who-writes-a-dataset-placement)), then restart this instance.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
