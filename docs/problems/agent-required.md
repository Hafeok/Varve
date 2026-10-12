# `agent-required`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/agent-required` |
| **Title** | The commit records its agent, and the caller has none. |
| **Status** | `403` |
| **Members** | none |

A commit that records its agent, a settings change, from a caller with none: anonymous mode names nobody (ADRs 0094, 0106).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
