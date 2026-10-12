# `not-acceptable`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/not-acceptable` |
| **Title** | Nothing in Accept is a format this endpoint writes. |
| **Status** | `406` |
| **Members** | none |

Nothing in `Accept` that the endpoint can write (ADR 0092).

Or the format `Accept` chose cannot spell the graph (ADR 0092, amended
2026-10-12): a triple term in JSON-LD, a predicate with no XML qualified
name in RDF/XML, a character XML 1.0 cannot carry. The `detail` names the
cause; asking for N-Triples, Turtle, N-Quads or TriG reads the same graph.
JSON-LD meets the refusal before the first byte, always. RDF/XML, which
streams, may meet it after: the response is then cut, with a
`Varve-Error: not-acceptable` trailer where it carries trailers and an
aborted connection where it does not (ADR 0095).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
