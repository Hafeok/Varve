# `graph-not-writable`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/graph-not-writable` |
| **Title** | The request changes a graph outside the caller's writable scope. |
| **Status** | `403` |
| **Members** | `graph` |

A write that changes a graph outside the caller's writable scope (ADR 0107). `graph` names it, `default` for the default graph; nothing was committed.

A write to a validation report graph, under `https://w3id.org/varve/ns#validation/`, is refused the same way whoever makes it, an administrator included: the report is computed by its binding's projection and never written (ADR 0174).

## What to do

- An admin of the dataset: if the caller should write `graph`, grant `write`
  on it in the dataset's declaration and apply it with `varve apply`
  ([Authenticate](../operator/authenticate.md#the-model)).
- A client: a validation report graph is never written; change the data or
  the shapes instead ([Shapes](../operator/shapes.md#the-report-graph)).
  Sending the same write again is refused again.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
