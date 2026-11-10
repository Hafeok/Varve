# `as-of-distance-exceeded`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/as-of-distance-exceeded` |
| **Title** | The as-of position is too far from its nearest checkpoint. |
| **Status** | `422` |
| **Members** | `limit`, `actual` |

A `Varve-As-Of` position more than `Varve:Limits:MaxAsOfDistance` commits above its nearest checkpoint, refused before any log is read (ADR 0114). `limit` and `actual` are commits.

## What to do

- A client: read at a position closer to a checkpoint, or ask an operator
  for one. Sending the same request again is refused again.
- An operator: take a checkpoint at or below the position, with `varve
  checkpoint` or `POST /manage/datasets/{name}/checkpoints`
  ([Back up and restore](../operator/backup-and-restore.md#checkpoints)), or
  raise `Varve:Limits:MaxAsOfDistance` ([Configure](../operator/configure.md)).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
