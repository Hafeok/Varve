# `validation-behind`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/validation-behind` |
| **Title** | The validation report has not reached the position asked for. |
| **Status** | `503` |
| **Members** | `binding`, `position`, `projectedPosition` |

The read named a shapes binding's report graph,
`https://w3id.org/varve/ns#validation/{enc(binding)}`, or matched every graph,
at `position`. The binding's validation projection had reached only
`projectedPosition` (ADR 0174). The server waited once, a bounded wait, and the
projection was still behind. The response carries `Retry-After: 1`. Retry, or
read a position the projection has reached.

Only a read that needs the report is refused. A read of the data graphs alone
never waits for the projection. A projection's lag is reported per binding on
`/health/ready`, beside its dataset's line, and never makes the dataset
unready or behind.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
