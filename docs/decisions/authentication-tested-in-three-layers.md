---
set: authentication-tested-in-three-layers
namespace: varve
adr: 0100
decisions:
  - key: InProcessIssuerForEveryPermissionTest
    statement: "An in-process OIDC issuer on loopback with a key per run mints the tokens of every permission and token-validation test, on every platform"
  - key: MockOAuthServerOnLinux
    statement: "navikt mock-oauth2-server on Linux CI, with a committed configuration, exercises client credentials and the authorization code with PKCE against the real middleware"
  - key: ZitadelOnLinux
    statement: "Zitadel and PostgreSQL on Linux CI, seeded through the management API by eng/zitadel-seed.cs, are the real-provider leg with one test each for client credentials and device code"
  - key: EntraAndGoogleBySecret
    statement: "Entra ID and Google have one end-to-end test each, skipped unless their secrets exist in a main-restricted environment"
  - key: MissingLegFailsWhereRequired
    statement: "A provider test skips when its address is absent, and fails instead where VARVE_AUTH_LEGS_REQUIRED names its leg"
  - key: AuthJobNames
    statement: "The CI jobs auth (mock-oauth2) and auth (zitadel) are required checks declared in .github/repo-standard.yaml"
---

The rulings of [ADR 0100](../adr/0100-authentication-tested-in-three-layers.md), filed unaccepted by milestone 7a of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.
