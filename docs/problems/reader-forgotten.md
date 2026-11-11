# `reader-forgotten`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/reader-forgotten` |
| **Title** | This instance's reader record was forgotten; it attaches again. |
| **Status** | `503` |
| **Members** | none |

The dataset is in an object store, and this instance reads extents through a
catalog version it announced in its reader record (ADR 0160). An operator
forgot that record with `varve storage readers forget` while the instance
still read — or another instance started under the same reader id and wrote
over it — so the writer was free to delete what the record held, and the
read found an extent gone. The read is failed, never answered wrong. The
instance attaches again, with a fresh record and the newest catalog; retry
the request. If it recurs, give each instance its own reader id
(`Varve:ObjectStorage:ReaderId`).

## What to do

- A client: retry the request.
- An operator: if it recurs, give each instance its own
  `Varve:ObjectStorage:ReaderId` ([Configure](../operator/configure.md)), and
  run `varve storage readers forget` only for a reader you know is gone
  ([Back up and restore](../operator/backup-and-restore.md#readers-and-retained-bytes)).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
