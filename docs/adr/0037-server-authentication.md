# 0037 — Authentication and authorisation for the server

Status: Accepted (2026-09-22). Number assigned on merge: it follows 0036, the
last ADR in `docs/adr/` at that point.

## Context

Varve runs in three hosts from one core: embedded library and CLI, standalone server, and browser. Only the server has callers it must authenticate. The server is expected to run under Aspire in development, as an Azure Container App in production, and self-hosted anywhere a container runs.

The primary identity provider for our deployments is Microsoft Entra ID. Entra issues standard OpenID Connect tokens. Microsoft ships two ways to consume them in ASP.NET Core: `Microsoft.AspNetCore.Authentication.JwtBearer`, part of the ASP.NET Core shared framework, and `Microsoft.Identity.Web`, a package that adds Entra-specific conveniences and depends on MSAL and its tree.

Constraints that bear on the choice: constraint 4 (every third-party package needs an ADR and must earn its place), constraint 2 (Native AOT and trimming), and ADR 0003 (the store at layer 4 must not know about the server at layer 5). The store must also remain usable, and securable, by people who have no Azure tenant.

## Decision

1. The server authenticates callers by validating OIDC bearer tokens with `JwtBearer` from the shared framework, configured with an authority, one or more audiences, and issuer validation. No `Microsoft.Identity.Web`, no MSAL.
2. Entra ID is the default, documented and tested configuration, including tokens obtained through managed identity for service-to-service calls. Any other OIDC issuer works with the same code and configuration; it is supported but not tested by us.
3. Authorisation is claim-based. Configuration maps claims (Entra app roles or group ids by default; claim type and values are configurable) to three permissions per dataset: `read`, `write`, `admin`. `admin` covers settings commits, erasure, checkpoint and projection management. There is no user store in Varve.
4. Anonymous mode exists for development and embedded use. It must be enabled explicitly and the server logs a warning on every start while it is on. The Aspire hosting integration enables it in development by default and never in publish.
5. The commit agent (`meta.agent` in the log and projection model, section 1) is the caller's stable subject identifier from the token (`oid` for Entra, `sub` otherwise), recorded as a term, so provenance survives independently of the identity provider. In erasure mode the classifier may make it a private term.
6. Ownership: all of this lives in `Varve.Server` (layer 5). `Varve.Store` never receives a principal, a claim or a token. The CLI, embedded, performs no authentication; the dataset directory's file permissions are its boundary, and the lease file in `derived/` prevents a CLI and a server from opening one dataset concurrently.
7. TLS termination is the deployment's job (reverse proxy, Container Apps ingress). The server can serve TLS directly with a supplied certificate but does not manage certificates.
8. The CLI, when it talks to a remote server rather than opening a dataset directly, authenticates with the same bearer tokens, obtained through the OAuth 2.0 device code flow or client credentials; it stores nothing but the refresh token the issuer hands it, in the platform credential store.

## Alternatives considered

- **`Microsoft.Identity.Web`.** Rejected: adds MSAL and a large dependency tree for conveniences (token acquisition, downstream API calls) the server does not need; it is a consumer of tokens, not an acquirer. AOT and trimming status would have to be verified and defended under constraint 2. It also makes Entra a hard dependency of an open-source store.
- **Entra-only with the tenant baked into the design.** Rejected for the last reason above; the generic OIDC path is the same code with different configuration, so exclusivity would cost adoption and buy nothing.
- **A built-in user and password store.** Rejected: a second identity system to secure, back up and audit, and the one thing every deployment already has elsewhere.
- **API keys, in any form.** Rejected permanently: long-lived secrets to rotate and leak, and a second credential system beside the one every deployment already has. Automation obtains tokens through the OAuth 2.0 flows the issuer already provides: client credentials for services, managed identity on Azure, device code for interactive CLI use against a remote server. A caller that cannot obtain an OIDC token is not a supported caller.
- **Authorisation inside the store (permissions as data in the graph).** Rejected: violates ADR 0003 and ADR 0005 in spirit, and puts a policy decision on the hot path of every read. A SPARQL-level policy layer is a later integration package, not a store feature.
- **Mutual TLS.** Not rejected, deferred: usable today through the reverse proxy; native support is not needed for 1.0.

## Consequences

- No new runtime package. `JwtBearer` is in the shared framework; the register gains no entry.
- Configuration surface for 1.0: `Auth:Mode` (`Oidc` or `Anonymous`), `Auth:Authority`, `Auth:Audiences`, `Auth:RoleClaimType`, and a per-dataset permission map. Documented in the operator guide with a complete Entra example (app registration, app roles, managed identity) and a generic issuer example.
- The Azure Container Apps sample uses the built-in bearer validation rather than Container Apps easy-auth, so behaviour is identical across hosts; easy-auth remains an option in front of it.
- Tests: token validation against a local OIDC issuer in the test suite; the Entra path is an integration test that runs only when a tenant is configured in CI secrets and is otherwise skipped, not failed.
- Analyzer implication: none new. The layer rule already prevents `Varve.Store` from referencing authentication types.
- No revisit condition for the credential model: OIDC bearer tokens are the only accepted credential, and this ADR is not to be superseded by one that adds API keys or another secret-based scheme. The claim mapping and the anonymous development mode may be amended.

Checked against ADRs 0001 to 0027 and the log and projection model, version 1.1. Touches 0003 (layer ownership), 0005 (store stays free of host concerns) and the spec's definition of commit metadata.
