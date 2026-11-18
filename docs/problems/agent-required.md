# `agent-required`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/agent-required` |
| **Title** | The commit records its agent, and the caller has none. |
| **Status** | `403` |
| **Members** | none |

A commit that records its agent, a settings change, from a caller with none: anonymous mode names nobody (ADRs 0094, 0106).

## What to do

- A client: send the change with a bearer token, so the commit names you. A
  server in anonymous mode names nobody, so it cannot take this change from
  any caller ([Anonymous mode](../operator/authenticate.md#anonymous-mode)).
- An operator: run the server with `Varve:Auth:Mode` set to `Oidc`, or
  declare the setting in the dataset's declaration, where its writer commits
  the change as its own agent ([Datasets](../operator/datasets.md#the-management-resources)).
- Sending the same request again without a token is refused again.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
