# `storage-full`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/storage-full` |
| **Title** | The dataset's storage has no space for the write; nothing was committed. |
| **Status** | `507` |
| **Members** | none |

The disk or the quota under the dataset's directory was full when the write
needed space (ADR 0214). The commit was never closed in the log, and nothing
of it is visible: the dataset is at the position it held before. Reads go
on. A commit that was closed and whose flush failed is answered
[`commit-outcome-unknown`](commit-outcome-unknown.md) instead.

There is nothing to reopen. The dataset's next write tries the storage again
by itself: it recovers the log's tail as an open would, and when space is
back, the write proceeds. Until then each write is answered with this type.
Free space and send the write again. Readiness reports the dataset's writes
as refused for lack of space, and stays ready.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
