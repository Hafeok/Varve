# `management-behind`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/management-behind` |
| **Title** | The server has not applied the management position asked for. |
| **Status** | `503` |
| **Members** | `position`, `appliedPosition` |

The request sent `Varve-Min-Management-Position: p`, and this instance has
applied the system dataset only up to `appliedPosition`, below `position`
(ADR 0163 point 3). It advanced its follower once before answering, and was
still behind: the grants a management write established are not yet in force
here. The response carries `Varve-Management-Position`; retry, or send the
request to the system writer. A write is refused with this problem too when
its dataset already records a later management position than the one it was
authorised at (ADR 0165): retry it, and it is authorised again.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
