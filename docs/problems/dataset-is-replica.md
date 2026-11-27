# `dataset-is-replica`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/dataset-is-replica` |
| **Title** | The dataset is a replica and takes no write. |
| **Status** | `409` |
| **Members** | `reason` |

The write was sent to a dataset that is a copy of another one, kept up to
date by log shipping (ADR 0185 decision 3). Nothing but the shipper writes
it, so it has no writer, and nothing in the request was committed. `reason`
says which case it is:
- `replica`: the dataset is a replica. It takes writes only after an
  operator promotes it;
- `promotion-incomplete`: an operator promoted it, and the promotion did
  not finish. It takes writes only after it is promoted again.

An ordinary dataset with no writer answers `503`, because a writer that
starts will claim it and a retry then succeeds. Here a retry cannot help:
nothing in Varve promotes a replica by itself.

Reads are served as for any dataset.

## What to do

- A client: do not retry. Send the write to the dataset this one is a copy
  of, through the fleet that serves it.
- An operator, for `replica`: if the source is lost and this copy is to
  take over, promote it with `PUT /manage/datasets/{d}/state` and
  `{"promote": {"source": "<address>", "position": p}}`, sent to the
  instance the dataset is placed on. Commits the source acknowledged and
  had not yet shipped are not on the copy; readiness on the source showed
  that lag.
- An operator, for `promotion-incomplete`: promote it again, with the same
  source and position. That completes it.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
