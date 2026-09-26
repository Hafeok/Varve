---
set: server-authentication
namespace: varve
adr: 0037
decisions:
  - key: OidcBearerTokensViaJwtBearer
    statement: "The server authenticates callers by validating OIDC bearer tokens with the shared framework's JwtBearer, with no Microsoft.Identity.Web and no MSAL"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: EntraDefaultAnyIssuer
    statement: "Entra ID is the default, documented and tested issuer, and any other OIDC issuer works with the same code"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: ClaimBasedDatasetPermissions
    statement: "Configured claims map to read, write and admin permissions per dataset, and Varve keeps no user store"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: AnonymousModeIsExplicit
    statement: "Anonymous mode is enabled only explicitly, logs a warning on every start, and is enabled by the Aspire integration only in development"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: CommitAgentIsTheTokenSubject
    statement: "The commit agent is the caller's stable subject identifier from the token, oid for Entra and sub otherwise, recorded as a term"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: AuthenticationOnlyInTheServer
    statement: "Authentication lives in Varve.Server, Varve.Store never receives a principal, claim or token, and the embedded CLI performs none"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: DatasetLeasePreventsConcurrentOpen
    statement: "A lease file in derived/ prevents a CLI and a server from opening one dataset concurrently"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: TlsTerminationIsTheDeployments
    statement: "TLS termination is the deployment's job, and the server serves a supplied certificate but manages none"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: RemoteCliUsesBearerTokens
    statement: "The CLI talking to a remote server uses bearer tokens from the device code or client credentials flow and stores only the issuer's refresh token, in the platform credential store"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: NoApiKeysEver
    statement: "OIDC bearer tokens are the only accepted credential, and no later decision adds API keys or another secret scheme"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
---

The rulings of [ADR 0037](../adr/0037-server-authentication.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

The ADR places `Varve.Server` at layer 5; ADR 0060's table, in its set, moved hosts to layer 6.
The statements here therefore name the package and not the layer.
