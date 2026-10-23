# `event-catalogue-moved`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/event-catalogue-moved` |
| **Title** | The dataset's event declarations are on the management plane. |
| **Status** | `409` |
| **Members** | `manage` |

The commit changes a catalogue graph, `varve:events-catalogue/{enc(G)}`, of a
dataset whose event declarations are moving, or have moved, to the
management plane (ADR 0167). From the management position that froze them,
`varve:eventDeclarations varve:Moving`, the dataset's writer refuses every
write to its catalogue graphs, on every write path. `manage` is the path of
the dataset's declarations in their new home, `/manage/datasets/{name}/events`:
declare, change or retire an event type there, or in the dataset's
declaration file under `varve apply`. Nothing was committed. The catalogue
graphs' history stays readable as of any position.

While a move is `Moving`, an operator or the dataset's admin can still cancel
it by retracting `varve:Moving` from the dataset's graph; catalogue writes
authorised after the cancel are accepted again.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
