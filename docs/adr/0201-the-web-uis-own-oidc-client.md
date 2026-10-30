# 0201 — The Web UI's own OIDC client: one token per plane

## Status

**Accepted — filed unaccepted by Web UI part 1 (#167, milestone #96),
2026-10-28** (ADR 0066). Acceptance is the maintainer's act on the pull
request. Decided by the maintainer on the part 1 plan (#96, 2026-10-27), **D1**:
- the Web UI has its own minimal OIDC public client, internal to it, not
  public API;
- **the refresh-token grant for the management scope is required.** The
  `prompt=none` second redirect for the management token is dropped. An issuer
  that cannot redeem a refresh token for the other scope is not supported, and
  the guide says so issuer by issuer;
- `prompt=none` stays, for a reload;
- this ADR records that the ID token's signature is not checked in the
  browser (OIDC Core §3.1.3.7), while `iss`, `aud`, `exp` and `nonce` are;
- the RFC 7636 Appendix B vector is a test, as are the random source and
  lengths of the verifier and the state, and property tests that a replayed
  state, a mismatched state and a mismatched nonce are refused.

**D3:** Entra is documented, not run in CI. One manual Entra sign-in is
recorded in traceability if one is done.

**Open:** Zitadel cannot give a second plane's token through the refresh grant
(measured, below). Put to the maintainer on #96 on 2026-10-28; until ruled, the
Zitadel leg is out.

Records decision set [`the-web-uis-own-oidc-client`](../decisions/the-web-uis-own-oidc-client.md).

## Context

The server holds two planes apart by audience (ADR 0163). A data-plane token
is for `Auth:Audiences`, a management token for `Auth:Management:Audience`,
and a token for both is refused by both. An operator's screens read both
planes, often on one page, so the Web UI holds two access tokens at once.

`Microsoft.AspNetCore.Components.WebAssembly.Authentication`'s OIDC provider
keeps one user with one access token. A request for scopes the stored user
lacks calls `signinSilent` with them, which replaces that user. Two planes on
one page would then trade the stored token back and forth through the issuer,
and two requests at once would race on it. MSAL keeps a cache per resource,
but ADR 0037 refuses MSAL.

## Decision

1. **Authorisation code with PKCE, written in the Web UI** (`SignIn/`, about
   650 lines on the BCL, no package and no JavaScript beyond two session
   storage calls).
   - **Discovery** from `{authority}/.well-known/openid-configuration`.
   - **The redirect** asks for `response_type=code`, PKCE `S256`, a `state`
     and a `nonce`, and `scope=openid offline_access <data scope>`, with the
     exact `redirect_uri` `{web ui}/signin`. The verifier, the state and the
     nonce are each 32 bytes from `RandomNumberGenerator`, base64url, 43
     characters.
   - **The callback** takes the sign-in in flight out of session storage, so a
     second use of an answer finds none. It then checks:
     - `state`;
     - `iss` (RFC 9207), when discovery advertises it or the answer carries it;
     - an `error`. From a silent sign-in, an error that needs the person
       (`login_required` and its kin) starts an interactive one; any other
       error is a failure, never a loop;
     - the code exchange, with the verifier;
     - the ID token's `iss`, `aud` (the client id), `exp` and `nonce`.

     A failure keeps nothing and says why, with a button to start again.
   - **The ID token's signature is not checked.** The token came directly from
     the token endpoint over TLS, which OIDC Core §3.1.3.7 point 6 allows in
     place of it. It is read for the display name only and authorises
     nothing. The planes' access tokens are what the server checks, against
     the issuer's keys.
2. **One token per plane, never both scopes in one request.**
   - **Data plane:** the code exchange's access token.
   - **Management plane:** the refresh-token grant with
     `scope=<management scope>` alone.
   - A token within the client's 30 s skew of expiry is renewed by the same
     grant with its own plane's scope. A rotated refresh token replaces the
     old one. Renewals take turns, since there is one refresh token.
   - Each plane's token is a `BearerTokenProvider` (ADR 0127), handed to the
     clients that read that plane.
   - **An issuer that will not redeem the refresh token for the management
     scope** leaves the management plane signed out, and the shell says so.
     The data plane works.
3. **Where tokens live.**
   - Access tokens and the refresh token are in the WebAssembly heap, for the
     tab's life, never in storage.
   - Session storage holds only the sign-in in flight: state, nonce, verifier
     and return path. It is written before the redirect, and taken out by the
     callback.
   - **A reload loses the heap.** The layout starts a sign-in with
     `prompt=none` on the first screen, which an issuer session makes
     invisible, and an interactive one if the issuer needs the person. The
     callback and the signed-out screen start none.
   - **Sign-out** drops both tokens and goes to `end_session_endpoint` with
     `id_token_hint`, returning to `{web ui}/signedout`. Without one, it goes
     straight there.
4. **The shell shows each plane's state** (ADR 0206's `Shell`): "signed in",
   or "signed out" marked absent, per plane. **Sign out** is the bar's action
   while signed in. Anonymous mode shows both planes "anonymous" and no
   action.
5. **Settings**, in the bundle's `appsettings.json` under `Varve:Auth`:
   - `Mode`: `Oidc` or `Anonymous`;
   - `Authority`;
   - `ClientId`, the Web UI's public client;
   - `DataScope`;
   - `ManageScope`.

   The server sends the authority's origin in the Web UI's `connect-src`
   (ADR 0202).
6. **Issuers** (the guide's table):
   - **Entra:** a SPA platform registration, so the token endpoint answers
     CORS, with delegated scopes on both APIs. Entra's refresh tokens redeem
     across resources. Documented; not in CI (D3).
   - **mock-oauth2-server** (ADR 0100, layer b): the fixture maps the
     `varve-webui` client to the data audience, and the management scope to
     the management audience. It answers CORS on its token endpoint. Proven
     in a browser.
   - **Zitadel:** a refresh never narrows a token's audience and cannot add a
     scope (`invalid_scope`). Only token exchange (RFC 8693) with `audience`
     gives a single-plane token. **Not supported by this ADR's grant**;
     pending the ruling on #96.
   - **Keycloak, Auth0, Okta:** two audiences as two client scopes or two APIs.
     Whether the refresh grant redeems across them is per issuer, and the
     guide says to check before relying on it.

## Alternatives considered

- **The ASP.NET Core WebAssembly authentication library.** One stored user
  with one token, and a silent sign-in that replaces it for each plane: a
  round trip per switch, and a race between two requests. Refused, with this
  evidence, on the plan (D1).
- **MSAL.js.** A cache per resource, but a JavaScript dependency, and refused
  by ADR 0037.
- **A second top-level redirect with `prompt=none` for the management
  token.** It would make every sign-in two visible round trips, and fail
  outright where the issuer has no session cookie in the context. Dropped on
  the plan (D1).
- **Checking the ID token's signature in the browser.** It needs the JWKS,
  key rotation and a JWS library or hand-written RSA and EC verification, for
  a token that authorises nothing and came over TLS from the issuer.
- **Tokens in session or local storage, to survive a reload.** Any script on
  the origin could read them. The CSP makes that unlikely, but the heap makes
  it impossible, and `prompt=none` makes the reload cheap.

## Consequences

- **The Web UI is a public client:** `varve-webui` in the mock fixture, a SPA
  registration in Entra.
- **The mock fixture maps the client.** The code exchange carries no `scope`,
  so its claims come from the `client_id` mapping. That mapping's `aud` names
  the client too, because the mock puts the same claims in the ID token.
- **Tests:** `SignInTests` covers:
  - the RFC 7636 vector;
  - the random source and the 32 bytes;
  - the redirect's parameters;
  - a full sign-in with one token per plane;
  - the properties (replay, state, nonce, 50 samples each);
  - each check refusing on its own;
  - silent to interactive, and no loop;
  - an issuer that will not redeem;
  - renewal with the rotated refresh token;
  - sign-out.

  The browser tests (ADR 0204), with the gates, run the flow against
  mock-oauth2-server, including the dual-audience refusal. Until then the
  traceability record holds the flow driven in Chromium.
- **Checks:**
  - Against the accepted ADRs: 0037 (OIDC only, no MSAL), 0100 (the test
    issuers), 0127 (`BearerTokenProvider`), 0163 (two audiences, the dual
    refusal), 0200, 0202 and 0206. No conflict.
  - Layer ownership: `Varve.WebUi`, layer 6; internal.
  - Analyzer rule: none new. The sign-in types are outside the level
    namespaces, and VARVE0006 does not count them.

## Amendment, 2026-10-28 — both planes by token exchange

Filed by Web UI part 1 (#167, milestone #96), unaccepted until the maintainer
accepts it (ADR 0066). Decided by the maintainer on that milestone's pull
request (#187): token exchange as a generic RFC 8693 path, not issuer code
(2026-10-28); and, for an issuer whose exchange only narrows, a sign-in that
names both planes, under the conditions below (2026-10-30).

1. **`Varve:Auth:ManageGrant`** is `refresh`, point 2's grant and the
   default, or `exchange`. Any other value refuses to start, naming the
   setting. It is opt-in per issuer: `exchange` applies only where it is
   configured, and Entra ID stays on `refresh`.
2. **With `exchange`,** the sign-in asks for both planes' scopes, and each
   plane's token is the sign-in's access token exchanged (RFC 8693 §2.1) for
   that plane's scope. This replaces, for `exchange` only, point 2's "both
   scopes are never asked for in one request".
   - It runs from the public client: `client_id`, with no client secret and
     no actor token.
   - It names `audience` when `Varve:Auth:DataAudience` or
     `Varve:Auth:ManageAudience` is set, for the data and the management
     exchange.
   - It asks for `requested_token_type=urn:ietf:params:oauth:token-type:jwt`,
     because the server validates JWTs alone, and an issuer asked for an
     access token may answer an opaque one (Zitadel does).
   - A data exchange the issuer refuses fails the sign-in and keeps nothing.
     A refused management exchange signs the management plane out, with the
     reason (point 4), as a refused refresh does.
3. **A token naming both planes is sent only to the issuer's token endpoint,
   never to the server.**
   - **The combined token** (the sign-in's) is kept as briefly as possible:
     it is a local of the sign-in, dropped once both exchanges are answered.
   - **Renewal:** a plane's token near expiry is renewed by the refresh grant,
     with no scope, for a fresh combined token, which is exchanged for that
     plane and dropped. The refresh token stays the one long-lived
     credential, as in point 2; only the combined token's window is shorter.
   - **The client enforces it.** The bearer provider of each plane refuses a
     token whose `aud` names more than one audience, before the request is
     sent. It reads the claim without validating, which is enough for a
     refusal. A token that is not a JWT names no audience it can read and is
     left to the server.
   - **The server's refusal** of a token naming both planes (ADR 0163
     point 1) is unchanged and stays the guarantee; the client's check only
     keeps such a token off the network.
4. **The issuers' table** in `docs/operator/web-ui.md` says which issuers use
   `refresh` and which use `exchange`, and what each needs.

The ruling is `ManagementTokenByExchange`.
