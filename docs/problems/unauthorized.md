# `unauthorized`

| | |
|---|---|
| **Type** | `https://w3id.org/varve/problems/unauthorized` |
| **Title** | The request carries no valid bearer token. |
| **Status** | `401` |
| **Members** | none |

No bearer token, or one that does not validate. Deliberately thin: the body carries the type, the title and the status and nothing else, and `WWW-Authenticate: Bearer` carries `error="invalid_token"` only when a token was presented (RFC 6750 §3; ADRs 0037, 0119).

## What to do

- A client: send a valid bearer token for the plane you call: data routes
  take `Varve:Auth:Audiences`, `/manage/` takes `Varve:Auth:Management:Audience`.
  The command line takes `--token` or `--authority` and `--client-id`
  ([Authenticate](../operator/authenticate.md#the-command-line)).
- An operator: find the reason in the server's log by the request id
  ([What a refusal looks like](../operator/authenticate.md#what-a-refusal-looks-like)).

Every problem is `application/problem+json` (RFC 9457) with `type`, `title`
and `status` as above, `detail` when there is more to say, and `instance`,
the request id the response also carries as `Varve-Request-Id` (ADR 0119).
The shape is fixed by `ProblemCatalogue` in `Varve.Protocol`, which a test
holds to this page.
