# `rdf-syntax`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/rdf-syntax` |
| **Title** | The RDF body does not parse. |
| **Status** | `400` |
| **Members** | none |

A Graph Store body, or one part of a `multipart/form-data` body, that does not parse in the syntax its `Content-Type` names (ADR 0092).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
