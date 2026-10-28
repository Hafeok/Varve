---
set: the-web-uis-own-oidc-client
namespace: varve
adr: 0201
decisions:
  - key: OwnPublicClientWithPkce
    statement: "The Web UI signs in with its own internal OIDC public client: authorisation code with PKCE S256, and state, nonce and verifier of 32 bytes each from RandomNumberGenerator"
  - key: CallbackChecksStateIssAndIdToken
    statement: "The callback takes the sign-in in flight out of session storage, checks state and iss, exchanges the code with the verifier, and checks the ID token's iss, aud, exp and nonce; a failure keeps nothing"
  - key: IdTokenSignatureNotCheckedInTheBrowser
    statement: "The ID token's signature is not checked in the browser, as OIDC Core 3.1.3.7 point 6 allows for a token from the token endpoint over TLS; it is read for display only and authorises nothing"
  - key: OneTokenPerPlaneByRefreshGrant
    statement: "The data plane's token comes from the code exchange and the management plane's from the refresh-token grant with the management scope alone; both scopes are never asked for in one request, and an issuer that cannot redeem across them is unsupported"
  - key: TokensInTheHeapOnly
    statement: "Access and refresh tokens live in the WebAssembly heap for the tab's life; session storage holds only the sign-in in flight"
  - key: ReloadThroughPromptNone
    statement: "A reload starts a sign-in with prompt=none, and an interactive one only if the issuer needs the person"
---

The rulings of [ADR 0201](../adr/0201-the-web-uis-own-oidc-client.md), one line each.
Filed unaccepted by Web UI part 1 (#167); every citation is `CS0618` until the maintainer accepts
them (ADR 0066).
