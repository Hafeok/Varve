# `graph-not-writable`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/graph-not-writable` |
| **Title** | The request changes a graph outside the caller's writable scope. |
| **Status** | `403` |
| **Members** | `graph` |

A write that changes a graph outside the caller's writable scope (ADR 0107). `graph` names it, `default` for the default graph; nothing was committed.

A write to a validation report graph, under `https://w3id.org/varve/ns#validation/`, is refused the same way whoever makes it, an administrator included: the report is computed by its binding's projection and never written (ADR 0174).

So is a write to a materialised closure's graphs: the views `https://w3id.org/varve/ns#explicit` and `https://w3id.org/varve/ns#implicit`, and every graph under `https://w3id.org/varve/ns#inferred/` (ADR 0198). Their content is computed by the binding's inference projection.

## What to do

- Write the graph a quad belongs in. A grant that writes it is the operator's: `varve apply` with a `varve:write` grant naming the graph.
- To change what a closure holds, change the quads it is computed from: a data graph of its binding, or its TBox graphs.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
