# `writer-stepped-down`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/writer-stepped-down` |
| **Title** | The writer released the dataset and commits nothing more. |
| **Status** | `503` |
| **Members** | `reason` |

The dataset is in an object store, and the instance that wrote it has
released its writer epoch (ADR 0179 point 6). It abdicated for one of these
reasons, which `reason` gives:
- `shutdown`: a graceful shutdown;
- `placement`: the dataset was placed elsewhere;
- `storage-error`: a batch failed against the store;
- `local-disk-error`: its local disk could no longer take the memtable's
  spills.

Nothing in the refused request was committed. The instance answers the
requests it held in flight this way, and later direct requests until it has
re-opened as a follower; after that it forwards them. A standby, or the
instance's own restart, takes the dataset over. Send the write again: through
any instance it is routed to the new writer, and a forwarded one that met this
answer is answered `writer-moved`.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
