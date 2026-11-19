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

- **`owl2-rl` on a dataset with no `varve:OWL2RL` binding**: that regime is
  answered by an OWL 2 RL closure, which only such a binding keeps (ADR 0197
  point 6).
- **A view** (`varve:explicit`, `varve:implicit`) under `rdf`, `rdfs` or
  `d`: a view is a part of the closure, which those regimes, answered by
  rewriting, do not have (ADR 0198 point 1).
- **An update** with any value other than `simple`: its `WHERE` is evaluated
  under simple entailment (ADR 0192).

On a dataset that has an `OWL2RL` binding, a graph the query reads that no
`OWL2RL` binding covers is not yet refused: it is read with its explicit
quads only, and nothing in the answer says so. That is a known gap against
the ruling of 2026-11-19 (ADR 0197, amended 2026-11-19, point 4), proposed
to be refused here.

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
