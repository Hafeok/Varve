# `agent-required`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/agent-required` |
| **Title** | The commit records its agent, and the caller has none. |
| **Status** | `403` |
| **Members** | none |

A commit that records its agent, a settings change, from a caller with none: anonymous mode names nobody (ADRs 0094, 0106).

**`varve serve` never answers it.** The server always has a management
plane, and there a settings change is a declaration its writer commits as
its own agent (ADR 0163). The type is answered only by an application that
hosts `Varve.Protocol` itself without a management plane
(`MapVarveManagement` with no plane), whose `PUT` and `PATCH
/manage/datasets/{name}/settings` take the change from the caller
(`AdminEndpoints`).

## What to do

- A client of such an application: send the change with a bearer token, so
  the commit names you. A host in anonymous mode names nobody, so it cannot
  take this change from any caller
  ([Anonymous mode](../operator/authenticate.md#anonymous-mode)).
- The application's operator: run it with authentication, or host the
  management plane, where the change is declared and the writer commits it
  as its own agent ([Datasets](../operator/datasets.md#the-management-resources)).
- Sending the same request again without a token is refused again.

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
