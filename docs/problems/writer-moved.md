# `writer-moved`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/writer-moved` |
| **Title** | The instance a write was forwarded to no longer writes the dataset. |
| **Status** | `503` |
| **Members** | `epoch`, `requestId` |

A write was forwarded once, carrying `Varve-Forwarded`, and the instance it
reached does not write the dataset now (ADR 0168 point 4): the writer
changed between the route and the forward. A forwarded request is never
forwarded again, so a request crosses at most one hop. `epoch` is the epoch
the answering instance knows; `requestId` names the request. Nothing was
committed by the answering instance: send the write again, and it is routed
to the writer anew.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
