# `inference-behind`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/inference-behind` |
| **Title** | The closure has not reached the position asked for. |
| **Status** | `503` |
| **Members** | `binding`, `position`, `projectedPosition` |

A read at `position` needed a materialising binding's closure there, and its
inference projection had reached only `projectedPosition` (ADR 0195). The
read can be over a view (`varve:explicit`, `varve:implicit`), a reserved graph
under `varve:inferred/`, `Varve-Entailment: owl2-rl`, or explain. The server
waited once, a bounded wait of two seconds, and the projection was still
behind. The response carries `Retry-After: 1`. A query that streams has its
cut after the first byte as a `Varve-Error` trailer.

A projection's lag is reported per binding on `/health/ready`, as `inference`
beside its dataset's line, and never makes the dataset unready or behind.

## What to do

- Retry after `Retry-After`, or read a position the projection has reached.
- An operator whose projection stays behind reads its lag on
  `/health/ready`. After a restart, a projection rebuilds the closure at the
  head before it serves; the operator guide's inference page states what
  that costs.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
