# `entailment-not-available`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/entailment-not-available` |
| **Title** | The materialised closure is not available to this caller. |
| **Status** | `403` |
| **Members** | none |

A read that needs a materialising binding's closure, by a caller whose grant
restricts rows over the binding's data graphs (ADR 0238 point 2). The read
can be over a view (`varve:explicit`, `varve:implicit`), a reserved graph
under `varve:inferred/`, `Varve-Entailment: owl2-rl`, or explain. A
conclusion's premises are rows, so the closure can neither be filtered by row
without leaking or losing entailments, nor served whole: it is refused.

The problem carries no member, the binding included: the caller did not
send one, and a `403` names only what the caller sent (ADR 0242 point 1; ADR
0238, amended 2026-11-16).

## What to do

- A client: query under a regime the rewriting serves, `rdfs`, `rdf` or `d`
  (`Varve-Entailment`), which is evaluated over the rows the caller reads and
  is exact for them ([Inference](../operator/inference.md)).
- An admin of the dataset: a caller who needs the closure needs a grant
  without subject terms over the binding's graphs
  ([Row-level access](../operator/row-level-access.md)).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
