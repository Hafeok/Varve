# `forbidden`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/forbidden` |
| **Title** | The caller does not hold the permission the request needs. |
| **Status** | `403` |
| **Members** | `permission`, `scope`, `dataset`, `agent`, `plane` |

An authenticated caller without the permission the request needs on the dataset it names (ADRs 0037, 0091, 0106). `WWW-Authenticate: Bearer error="insufficient_scope"` (RFC 6750 §3.1).

The body says what was needed and who asked (ADR 0242): `permission` is the
permission checked (`read`, `write`, `admin`, `server-admin`, …); `scope` is
`dataset`, with `dataset` naming it, or `fleet`; `agent` is the caller's
agent IRI as the server resolved it from the token, when it resolved one; and
`plane` is `data` or `management`, the plane the token was accepted for. It
names no grant and no other agent. A caller that is not authenticated gets
[`unauthorized`](unauthorized.md), which says nothing more.

## What to do

- **Check the agent first.** If `agent` is not who you meant, the token is the wrong one: sign in as the right identity, or for a workload, check which identity issued it ([Authenticate](../operator/authenticate.md)).
- **If the agent is right, it lacks the grant.** An admin of the dataset grants `permission` on it ([The model](../operator/authenticate.md#the-model)); for `scope` `fleet`, an operator of the fleet does. Retrying without a new grant is refused again.
- **A management read** needs `admin` on the dataset, or the operator role on the fleet; a data-plane token is not accepted there, and `plane` says which one was.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
