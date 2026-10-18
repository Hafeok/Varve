# `writer-superseded`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/writer-superseded` |
| **Title** | A newer writer holds the dataset; this instance serves reads only. |
| **Status** | `503` |
| **Members** | none |

The dataset is in an object store, and another instance has claimed its
writer epoch since this one opened it (ADR 0136): a restart elsewhere, a
second replica, or an operator's `varve lease --take`. This instance's next
write found the log's next slot taken, so the write was refused and nothing
was committed. The instance stays open and serves reads; it never claims the
epoch again on its own, and `/health/ready` reports the dataset `superseded`.
Send writes to the instance that holds the epoch; `varve lease` names it.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
