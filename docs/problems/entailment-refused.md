# `entailment-refused`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/entailment-refused` |
| **Title** | The entailment regime refuses this query. |
| **Status** | `400` |
| **Members** | `reason` |

The regime a query asked for refuses it: SPARQL 1.1 Entailment Regimes'
`QueryRequestRefused` (§1.4), such as a query that is not legal under OWL 2
Direct semantics, or an ontology inconsistent under it (`reason`
`inconsistent`). Reserved: RDF, RDFS and D, the regimes served today, never
refuse a query, and nothing emits it until a later part of Inference (#95)
serves a regime that does (ADR 0192).

- `reason` says why, in a word.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
