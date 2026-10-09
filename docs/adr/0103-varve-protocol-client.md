# 0103 — `Varve.Protocol.Client`: the HTTP client at layer 5, and the endpoint policy

## Status

**Accepted — filed unaccepted by milestone 7b of #11, 2026-10-08** (ADR 0066).
Decided by the maintainer on the 7b plan. Acceptance is the maintainer's act
on the pull request.

## Context

Milestone 7b has three callers of a SPARQL endpoint over HTTP: the evaluator's
`SERVICE` handler (ADR 0055 reserved its HTTP implementation to "the server,
at milestone 7, at layer 5"), `LOAD` (ADR 0057, the same reservation), and the
CLI in remote mode (ADR 0037, point 8). All three send requests the SPARQL
1.1 Protocol defines, read results `Varve.Sparql.Results` already parses, and
read RDF documents `Varve.Turtle` already parses. None belongs in
`Varve.Protocol`: that package is the server side of the same protocol, and a
host embedding an endpoint has no reason to carry a client, nor a client a
server.

Two of the callers run inside a server on behalf of a query or an update
someone else wrote. A `SERVICE <iri>` or a `LOAD <iri>` is an outbound request
whose address the caller chose: the classic server-side request forgery. The
7a server makes no outbound call at all; 7b's does, and must say to whom.

## Decision

1. **`Varve.Protocol.Client`, layer 5**, a packable library over `HttpClient`
   from the BCL. It references `Varve.Sparql` (to serialise), `Varve.Sparql.Results`
   and `Varve.Turtle` (to parse), `Varve.Sparql.Evaluation` (the `SERVICE`
   contract, ADR 0104), `Varve.Store` (`ByteCount`), `Varve.Rdf` and
   `Varve.Iri`. It references neither `Varve.Protocol` nor
   `Varve.Sparql.Store`: both are layer 5 (ADR 0060). Its public types:
   - `SparqlHttpClient`: query (`GET` or `POST`, the results format asked
     for), update (`application/sparql-update`), the Graph Store's `GET`,
     `PUT`, `POST` and `DELETE`, the change feed as a byte stream, and the
     admin calls of ADR 0106, each over a dataset's base address, each
     carrying a bearer token the caller supplies and the `If-Match`,
     `Varve-As-Of` and `Accept` headers of ADRs 0094 and 0096;
   - `HttpServiceHandler` and `RdfDocumentClient` (ADR 0104);
   - `EndpointPolicy` and `ClientLimits`, below;
   - `Varve.Protocol.Client.Model`: `RdfDocument`, `EndpointRefusedException`,
     and the responses the admin calls return.
2. **An endpoint policy decides every outbound address before a connection
   is opened**, by the IRI as written in the query, the update or the
   command line:
   - **An allow-list of IRI prefixes, and the default is empty**: with no
     entry, every `SERVICE` and every `LOAD` is refused, exactly as the
     refusing defaults of ADRs 0055 and 0057 refuse them. The operator opts
     in, prefix by prefix (`https://query.wikidata.org/`).
   - **Only `http` and `https`.**
   - **Loopback, link-local, private and unspecified addresses are refused
     unless the operator says `AllowPrivateAddresses`**: an IP literal in
     `127/8`, `::1`, `10/8`, `172.16/12`, `192.168/16`, `169.254/16`,
     `fc00::/7`, `fe80::/10`, `0.0.0.0` and `::`, and the names `localhost`
     and `*.localhost`. This is the server-side request forgery
     consideration: an allow-list entry that an operator wrote is not a
     licence for a query to reach the server's own loopback, the cloud
     metadata address or a neighbour. A name that resolves to a private
     address is **not** caught by this check: the policy sees names, not
     resolutions, and the ADR says so. An operator who allows a name is
     answerable for what it resolves to. Rebinding between the policy's
     check and the connection is not defended here either.
   - **Redirects are not followed.** A `3xx` is a failure of the request. A
     redirect is a second address the policy did not see.
   - **Credentials in the IRI's authority are refused.**
3. **Every request is bounded**, by `ClientLimits` the host configures: a
   timeout for the whole exchange and a cap on the bytes read from the
   response, after which the response is a failure, never a truncated
   answer presented as whole.
4. **`System.Uri` is allowed in this package**, for transport addresses only,
   by `VarveSpeaksHttp` in its project file; ADR 0004 carries the dated
   amendment. An IRI is still a `Varve.Iri` value, and the policy's checks
   run on the IRI's bytes before any `Uri` is built.
5. **No package.** `HttpClient`, `SocketsHttpHandler` and `System.Text.Json`
   are the BCL. The host owns the `HttpClient` and its handler (connection
   pooling, proxies, `AllowAutoRedirect = false`), and hands it in.

## Alternatives considered

- **The client inside `Varve.Protocol`.** One package for both directions,
  and an embedding host that mounts an endpoint would carry a client, with
  a policy it never configured. Two packages, each with one reason to change.
- **Resolve names and check the addresses.** Catches `evil.example`
  resolving to `127.0.0.1`, and still loses to rebinding between the check
  and the connection unless the resolved address is pinned into the socket,
  which `HttpClient` does not offer without a custom `ConnectCallback`.
  Recorded as the extension for a deployment that needs it; the honest
  statement today is that names are the operator's responsibility.
- **Allow everything by default, as Oxigraph's server does.** Rejected: a
  SPARQL endpoint that makes requests to whatever a query names is a proxy,
  and the brief's authentication ADR (0037) made the server's callers
  explicit for the same reason.
- **A deny-list.** Cannot be complete; the private ranges above are a
  floor, and the allow-list is the fence.

## Consequences

- The server and the CLI reach other endpoints through one client, one
  policy and one set of limits, configured in one place (ADR 0104).
- A `SERVICE` or `LOAD` refused by the policy is refused with a message that
  names the IRI and the reason, so an operator can add the prefix.
- The package starts a public API baseline, like every packable package.

## Checks

- **Checked against the accepted ADRs** (0001–0101) and specification 1.6.
  Touches:
  - **0004**: `System.Uri`, by amendment;
  - **0009**: no new package;
  - **0055** and **0057**: the refusing defaults remain the defaults;
  - **0060**: layer 5, no same-layer reference;
  - **0091**: `Varve.Protocol` is unchanged.

  No conflict.
- **Layer ownership.** `Varve.Protocol.Client`, layer 5.
- **Analyzer rule.** None. `DD0001` enforces the layer; the banned-symbols
  file enforces `System.Uri` elsewhere.
- **Open questions owned.** None.
