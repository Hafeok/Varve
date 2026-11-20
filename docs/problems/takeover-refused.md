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

**A promotion is refused the same way.** A promotion request,
`PUT /manage/datasets/{d}/state` with
`{"promote": {"source": "<address>", "position": p}}`, asked this instance
to promote the replica `d` (ADR 0185). Nothing was changed, and `reason`
says why:
- `not-in-writer-set`: as above;
- `cold`: this instance does not follow the replica warm yet;
- `not-a-replica`: the dataset has no lineage record, or one this version
  does not read, or was already promoted, or this instance has no object
  store;
- `source-mismatch`: the copy's lineage record names another source than
  the one asked for;
- `position-mismatch`: the copy's last position is not the one asked for.
  `detail` gives both;
- `epoch-moved`: another promotion or another claim came first.

## What to do

For a promotion, by `reason`:
- `not-in-writer-set`, `cold`: ask the instance the dataset is placed on,
  once its readiness shows it warm;
- `not-a-replica`: read the dataset's state. If it was promoted already,
  there is nothing to do;
- `source-mismatch`: check which copy you mean to promote. The lineage
  names the source it was shipped from;
- `position-mismatch`: the copy holds less, or more, than you expected.
  Decide whether that is what you mean to keep, and ask again with that
  position. Commits the source acknowledged past it are not on this copy;
- `epoch-moved`: read the dataset's state, and promote again only if it is
  still a replica.

For a takeover, by `reason`:
- `handover-off`: declare `varve:automaticHandover true` on the dataset, with
  an object store;
- `not-in-writer-set`: name the instance in `varve:standbyOn`, or ask a member;
- `cold`: ask another standby, or wait until this one's readiness shows
  `warm`;
- `epoch-moved`: nothing; the dataset was already taken over
  ([The fleet](../operator/fleet.md#taking-over-from-a-failed-writer)).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
