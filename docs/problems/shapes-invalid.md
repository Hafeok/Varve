# `shapes-invalid`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/shapes-invalid` |
| **Title** | The binding's shapes graph is refused. |
| **Status** | `422` |
| **Members** | `binding`, `problems` |

A shapes binding's shapes graph does not load: a shape is ill-formed (ADR
0151), the graph is not stratifiable (ADR 0150), a constraint needs an
extension the server does not have, or a SPARQL-based constraint calls
`NOW()`, `RAND()`, `UUID()`, `STRUUID()`, or `BNODE()` whose value can reach a
result, which a binding refuses because a validation that uses one cannot be
repeated (ADR 0169). Under a `reject` binding, a validation that fails — a
pattern over its time limit, a SPARQL query that fails — is answered the same
way. Nothing was committed.

- `binding` is the binding's IRI, in canonical N-Triples.
- `problems` lists each problem by `kind`, `message`, and, when known,
  `shape`, `focusNode` and the refused `cycle`.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
