# 0127 — On-behalf-of: the client's token provider, `azp` on the span, and the attribution rule

## Status

**Accepted — filed unaccepted by milestone Client and integration of #93,
2026-10-13** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the Client and integration plan: the
exchange is Entra's and runs only on the optional leg; the in-process issuer
proves Varve's side with a minted token, and no fake exchange is built.

**Amends** ADR [0103](0103-varve-protocol-client.md) (the fixed token becomes a
provider, and the client no longer references `Varve.Store`) and ADR
[0112](0112-telemetry-in-two-layers.md) (one more `varve.` span attribute),
each by a dated block of its own (ADR 0068).

## Context

A website or an MCP server holds a user's token for *its own* API. To call
Varve as that user, it exchanges the token at the identity provider for one
whose audience is Varve: OAuth 2.0 token exchange in general (RFC 8693), the
on-behalf-of flow in Entra ID
(`grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer`,
`requested_token_use=on_behalf_of`). The result is an ordinary access token.
Its `oid` is the user's, and its `azp` (`appid` in a v1 token) names the
application that asked.

Varve already does everything the token needs. ADR 0037 validates bearer
tokens and refuses to acquire them. ADR 0094 makes the agent the issuer plus
the subject claim, which is `oid` for Entra. So this milestone changes nothing
about how Varve accepts a token. It changes three other things.

1. **The client.** `SparqlHttpClient.AccessToken` is one mutable string. A
   web application acting for many users would need one client per user, or
   a race on a shared property. It also has no notion of expiry, so the CLI's
   `feed` stops working after an hour.
2. **What the operator can see.** A request made through an application should
   say which application made it. That belongs in telemetry, not in the log:
   the log records who changed what (ADR 0037's agent), and an application
   identifier is neither a person nor stable across registrations.
3. **What the operator is told.** A write made through on-behalf-of and a write
   the application makes on its own account land in the history as different
   agents. Whether that is right is the application's decision for each
   write. Nobody has written that down.

## Decision

1. **The client takes a token provider.** `SparqlHttpClient` gets a constructor
   taking a `BearerTokenProvider`:
   `delegate ValueTask<BearerToken?> BearerTokenProvider(CancellationToken)`.
   `BearerToken` is `(string Value, DateTimeOffset ExpiresOn)`. The provider
   is **called for every request**, so one client serves many users when the
   provider reads the current user from its own ambient context
   (`HttpContext`, an `AsyncLocal`). Caching is the provider's business, as it
   is MSAL's.
   - **Expiry is honoured.** The client measures it with the `TimeProvider` it
     is given, never an ambient clock: the package stays `VarveDeterministic`.
   - A token that expires within the skew (30 seconds by default) is asked
     for once more.
   - A token still stale after that is refused with
     `BearerTokenExpiredException` before any byte is sent. The client never
     sends a token it knows has expired.
   - A null token means the request goes unauthenticated.
   - The token is opaque: a string and an instant as the issuer gave them,
     unwrapped at the client's boundary, since the client reads neither.
   - `AccessToken` is removed, not aliased (ADR 0118). The convenience
     `BearerToken.Fixed(value)` covers the CLI's `--token` and tests.
2. **The CLI moves onto it.** Remote mode builds a provider over its credential
   sources (ADR 0105): `--token` or `VARVE_TOKEN` as a fixed token; a refresh
   token or client credentials renewed before expiry. A long `varve feed`
   therefore outlives one token.
3. **The acting application is a span attribute.**
   - `Auth:ApplicationClaim` names the claim, default `azp`; Entra v1 tokens
     configure `appid`.
   - `ICallerIdentity` gains `ApplicationOf(caller)`, defaulting to none.
   - Its value goes on the request span as **`varve.azp`** (ADR 0112's
     prefix: there is no OpenTelemetry convention for it).
   - It is **never written to the log and never to the logger**: it is not
     part of the commit's provenance (ADR 0037), and the logger's request
     line keeps its fields.
4. **The agent stays the subject.** A token minted for user `u` and requested
   by application `a` makes a commit whose agent is
   `<issuer>#<u's oid>`. Nothing in the server distinguishes an exchanged
   token from any other, and nothing should: the token is the contract.
5. **The attribution rule, stated in the operator guide.**
   - A write made through on-behalf-of is recorded with the end user as its
     agent.
   - A write the application makes on its own account, such as an import or a
     scheduled publish, is made with the application's client-credentials
     token and recorded with the application as its agent.
   - The two are different agents in the history. An application that routes
     its own writes through a user's token attributes them to that user.
   - This is a choice the caller makes per write. The guide shows both in a
     commits-list example.
6. **The Entra chain is documented and deployable.** The operator guide's Entra
   section gains:
   - Varve's registration exposing a delegated scope (`access_as_user`)
     beside its `read`, `write` and `admin` app roles;
   - the calling application's registration, granted that scope;
   - the exchange request;
   - the claims Varve reads.

   `deploy/azure/entra/` creates both registrations with the Microsoft Graph
   Bicep extension.
7. **A sample shows it.** `samples/Varve.OnBehalfOf.Sample` is a minimal API
   that validates its own user's token and exchanges it with MSAL through
   `Microsoft.Identity.Web`. That package is admitted for `samples/` only
   (ADR 0133). The sample queries and updates Varve as the signed-in user.
   - **Its exchange runs against Entra only**, on the providers leg when its
     secrets exist (ADR 0100).
   - Varve's side of the contract is proven without the exchange: a token
     the in-process issuer mints with a user's `oid` and an application's
     `azp` is accepted, the commit's agent is the user, and the span carries
     the application.
   - **There is no simulated exchange in CI.** It would be test code proving
     nothing about Entra.

## Alternatives considered

- **The server performs the exchange.** It would hold a client secret and
  acquire tokens, which ADR 0037 rules out: the server consumes tokens and
  never acquires them.
- **A `DelegatingHandler` in place of a provider.** It is the idiomatic hook
  in `HttpClient`, but it puts the token outside the client's own expiry
  check. It also cannot be given per request without the
  `HttpRequestMessage.Options` dance. A provider is one delegate, and it is
  testable.
- **`azp` in the commit's metadata.** It would make the application part of
  the provenance the log keeps for ever, and the log records who, not
  through what (ADR 0037). Telemetry is where "which application" is asked.
- **Simulating on-behalf-of against the in-process issuer.** On-behalf-of is
  Entra's flow as MSAL implements it. A test issuer answering it would exercise
  a hand-written endpoint that no deployment runs, and prove nothing about
  Entra.

## Consequences

- One `SparqlHttpClient` serves a multi-user application. The breaking change
  (`AccessToken` removed) is listed in the release's summary.
- `varve.azp` is a new attribute in the operator guide's telemetry table.
- The optional providers leg grows by one case. CI on pull requests proves the
  token path end to end through the in-process issuer.

## Checks

- **Checked against the accepted ADRs** (0001–0126). Touches **0037** (the
  server still never acquires a token), **0094** (the agent, unchanged),
  **0100** (the legs), **0103** (amended), **0105** (the CLI's sources),
  **0112** (amended), **0118** (removal, not alias). No conflict.
- **Layer ownership.** `Varve.Protocol.Client`, layer 5. `ICallerIdentity` is
  `Varve.Protocol`'s, layer 5. The claim and the configuration are
  `Varve.Server`'s, layer 6.
- **Analyzer rule.** None. `VarveDeterministic` already forbids the ambient
  clock the expiry check would otherwise reach for.
- **Open questions owned.** None.
