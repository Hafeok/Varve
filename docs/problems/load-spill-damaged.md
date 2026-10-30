# `load-spill-damaged`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/load-spill-damaged` |
| **Title** | A bulk load's spill was damaged; nothing was committed. |
| **Status** | `503` |
| **Members** | none |

A bulk load sorts its input into spills, temporary blobs of its own, and
reads them back before it commits (ADR 0081). Each page of every spill is
checked as it is read (ADR 0213). One did not match what the load wrote: the
disk or the store under the load damaged it. A spill is the load's own
input, so the log cannot rebuild it, and the load ended before its commit:
nothing of it is in the dataset, which is at the position it held before.
Submitting the same input again is right. Damage that recurs is the storage
under the load failing; see the operator's guide on derived-data damage.

`varve load` names this type on its error line and exits with `1`; it is
the type of `BulkLoadSpillDamagedException`. No endpoint loads in bulk yet.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
