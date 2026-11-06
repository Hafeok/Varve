# `entailment-unsupported`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/entailment-unsupported` |
| **Title** | The server does not answer this request under that entailment regime. |
| **Status** | `400` |
| **Members** | `value`, `supported` |

A request's `Varve-Entailment` names no regime this server answers: an
unknown token or IRI, more than one value, or a regime a later part of
Inference answers (`owl2-rl`, `owl2-ql`). An update with any value other
than `simple` is answered the same way: its `WHERE` is evaluated under
simple entailment (ADR 0192).

- `value` is the header as sent.
- `supported` lists the tokens the server serves: `simple`, `rdf`, `rdfs`
  and `d`.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
