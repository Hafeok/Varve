# `event-move-cancelled`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/event-move-cancelled` |
| **Title** | The move of the event declarations was cancelled while it ran. |
| **Status** | `409` |
| **Members** | `position` |

A move of a dataset's event declarations to the management plane,
`POST /manage/datasets/{name}/events/move` (`varve manage move-events`), was
in progress: this request had frozen the catalogue, or had found it frozen.
While it ran, another request cancelled the move by stating
`varve:eventDeclarations varve:InDataset` on the dataset's node, through
`varve apply` or a `PUT` of the dataset's declarations (ADR 0167 point 4).
`position` is the management position at which this request found the
cancel.

The cancel stands. This request committed nothing more, and it does not
freeze the catalogue again: the declarations are in the dataset, `GET
/manage/datasets/{name}/events` answers `in-dataset`, and catalogue writes
are accepted (ADR 0167, amended for #254).

## What to do

An operator, or the dataset's admin: find out who cancelled the move and
why. The cancel is a management commit at `position`, and its header names
its agent. If the declarations should still move, send the move again with
`varve manage move-events {name} …` ([Moving event schemas](../operator/moving-event-schemas.md)).

A client does not send the same request again on its own. A retry would undo
another caller's deliberate cancel, so `Varve.Protocol.Client` never retries
it (ADR 0246 point 10).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
