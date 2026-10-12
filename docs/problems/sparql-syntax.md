# `sparql-syntax`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/sparql-syntax` |
| **Title** | The request does not parse. |
| **Status** | `400` |
| **Members** | `line`, `column`, `offset` |

A query or update that does not parse, on `/sparql`. `line`, `column` and `offset` (bytes) point into the request's own text (ADR 0092; `docs/spec/sparql-grammar.md`).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
