# `entailment-unsupported`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/entailment-unsupported` |
| **Title** | The server does not answer this request under that entailment regime. |
| **Status** | `400` |
| **Members** | `value`, `supported` |

A request's `Varve-Entailment` names no regime this server answers for it:
an unknown token or IRI, more than one value, or a regime a later part of
Inference answers (`owl2-ql`). Also:

- **`owl2-rl` naming a graph no `varve:OWL2RL` binding covers**, or on a
  dataset with no such binding at all: that regime is answered by an OWL 2
  RL closure, which only such a binding keeps (ADR 0197 point 6, amended
  2026-11-19). A graph is named by `FROM`, `FROM NAMED`,
  `default-graph-uri`, `named-graph-uri` or `GRAPH <g>`, in the query or a
  subquery; `detail` names the graph. A binding covers its data graphs
  (not its TBox graphs), its reserved graph of TBox conclusions and its
  premise-set graphs.
- **A view** (`varve:explicit`, `varve:implicit`) under `rdf`, `rdfs` or
  `d`: a view is a part of the closure, which those regimes, answered by
  rewriting, do not have (ADR 0198 point 1). Under `simple` or `owl2-rl`, a
  view naming a graph no `varve:RDFS` or `varve:OWL2RL` binding covers, for
  the same reason.
- **An update** with any value other than `simple`: its `WHERE` is evaluated
  under simple entailment (ADR 0192).

`GRAPH ?g` names no graph and is not refused: it is answered over the named
graphs the query reads, and those with no closure are read with their
explicit quads only. The answer lists them in **`Varve-Without-Closure`**:
their IRIs in angle brackets, comma-separated, at most twenty, then
`+N more`; only graphs the caller reads are listed.

- `value` is the header as sent.
- `supported` lists the tokens the server serves for every dataset:
  `simple`, `rdf`, `rdfs` and `d`. `owl2-rl` is served besides on a dataset
  with an `OWL2RL` binding.

## What to do

A client: send one of the tokens `supported` lists, or no header at all; an
update takes none but `simple`. From the command line, `varve query
--entailment rdfs` ([Inference](../operator/inference.md#asking-for-a-regime)).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
