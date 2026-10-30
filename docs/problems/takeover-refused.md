# `takeover-refused`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/takeover-refused` |
| **Title** | This instance refuses to take the dataset over. |
| **Status** | `409` |
| **Members** | `reason`, `epoch` |

A takeover request, `PUT /manage/datasets/{d}/state` with
`{"takeover": {"epoch": n}}`, asked this instance to claim the dataset's
writer epoch after `n` (ADR 0179 point 6). It did not, and `reason` says why:
- `handover-off`: the dataset does not declare `varve:automaticHandover true`,
  or this instance has no object store;
- `not-in-writer-set`: this instance is neither the dataset's `placedOn` nor
  one of its `standbyOn` (ADR 0180);
- `cold`: this instance is not a warm standby of the dataset. Its readiness
  says which condition of warm fails;
- `epoch-moved`: the writer record no longer shows epoch `n`. Someone already
  took the dataset over, so the request is a no-op.

The reasons are checked in this order, except that `epoch-moved` comes
before `cold`, so a trigger repeated to the instance that already took the
dataset over reads as the no-op it is. `epoch` is the writer epoch the
record shows now. Nothing was claimed. A
trigger that fires late or twice for the same failure meets `epoch-moved`.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
