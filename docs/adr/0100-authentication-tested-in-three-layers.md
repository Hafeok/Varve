# 0100 — Authentication tested in three layers

## Status

**Proposed — filed unaccepted by milestone 7a of #11, 2026-10-07** (ADR 0066).
Decided by the maintainer in the 7a prompt (A9). Acceptance is the maintainer's
act on the pull request. **Refines ADR 0037**'s testing consequence, which
named a local issuer and an Entra leg skipped without secrets; this ADR keeps
both and adds two legs between them.

## Context

The server accepts one credential, an OIDC bearer token (ADR 0037). Whether it
is validated correctly can only be shown against an issuer. Each kind of issuer
answers a different question:

| Issuer | Answers |
|---|---|
| one written in the test | every permission decision and every way a token can be wrong, deterministically, anywhere the suite runs |
| a standards mock | that real OAuth 2.0 flows produce tokens the middleware accepts |
| a real provider | that a real provider's tokens, keys and claims work, with its own quirks |

A test may only use what exists where it runs. The cloud sandbox has no Docker,
Linux CI has it, and Windows and macOS runners do not.

## Decision

1. **(a) An in-process OIDC issuer in `Varve.Server.Tests`.**
   - Kestrel on a loopback port serves `/.well-known/openid-configuration` and
     a JWKS.
   - A fresh RSA key is generated per run.
   - `Mint(claims)` signs RS256 tokens with the BCL alone. Options cover the
     lifetime, the audience, the issuer and a foreign key.
   - **Every permission test and every token-validation test uses it**, on
     every platform the suite runs on, the cloud sandbox included:
     - the full matrix of endpoint × {no token, expired, not yet valid, wrong
       audience, wrong issuer, bad signature, unknown key, `read`, `write`,
       `admin`};
     - anonymous mode's warning;
     - its refusal under `Production`.
2. **(b) `ghcr.io/navikt/mock-oauth2-server` on Linux CI**, started by
   `docker compose` from `.github/mock-oauth2/compose.yaml`, pinned by digest.
   Its JSON configuration, `tests/fixtures/auth/mock-oauth2.json`, is
   committed and mounted. Tests exercise:
   - **client credentials**: obtain a token, write, and find the agent IRI the
     token names in the feed;
   - **authorization code with PKCE**: post the mock's login form as a person
     would, exchange the code with the verifier, read, and be refused a write.

   Both go against the real `JwtBearer` middleware with discovery over HTTP.
   **The mock has no device-code grant** (its discovery document advertises no
   device-authorization endpoint, and the endpoint answers `405`), so device
   code is tested against Zitadel in (c), which has one.
3. **(c) Zitadel on Linux CI**, the real-provider leg.
   - Zitadel and PostgreSQL are started by `docker compose` in the workflow
     (`.github/zitadel/compose.yaml`), with images pinned by digest.
   - `eng/zitadel-seed.cs` seeds them through the management API:
     - one project;
     - one API application;
     - one service user with client credentials;
     - one human user for device code;
     - the project roles `read`, `write` and `admin`, granted so that the
       claim maps to the server's permissions.
   - There is one end-to-end test per flow:
     - **client credentials**, the service user writing with its project role;
     - **device code**, the human user's code approved through Zitadel's
       session and OIDC services with the login client's token, exactly as a
       custom login UI approves it, so no browser and no login UI container
       are needed. The token reads and is refused a write.
   - Zitadel's roles claim is a JSON object whose property names are the roles
     (`urn:zitadel:iam:org:project:<id>:roles`). The server reads that shape,
     and layer (a) tests it too, so the parsing is covered on every platform.
4. **Entra ID and Google**: one end-to-end test each, skipped unless their
   secrets exist. The secrets live only in the `auth-providers` environment,
   which only `main` can deploy to; the job `auth (providers)` runs on pushes
   to `main`, so it is not a required check.
   - **Entra**: client credentials against a v2-token app registration, the
     subject claim `oid` and the app role in `roles`.
   - **Google**: a service account's ID token for an audience of our choosing
     (the JWT-bearer grant with a self-signed assertion). Google issues no
     roles, so the permission is granted to the account's `email` claim.
5. **The containers are the workflow's.** No test starts a container and no
   package does (ADR 0099).
   - A (b) or (c) test reads the provider's address from an environment
     variable, and is skipped when it is absent.
   - On each Linux auth job the workflow sets the variables and names its leg
     in `VARVE_AUTH_LEGS_REQUIRED` (`mock-oauth2`, `zitadel`, or both,
     comma-separated), which turns that leg's skip into a failure, so a broken
     container cannot pass silently. A list rather than a flag, because each
     job runs one provider.
6. **The devcontainer runs (b) and (c) locally** with Docker-in-Docker, from
   the same compose files and seed script, by `.devcontainer/auth-legs.sh`,
   which requires both legs and tears the containers down (ADR 0036).
   Docker-in-Docker rather than the host's socket, so that the compose files'
   bind mounts resolve against the checkout's own paths.
7. **The CI jobs are named** `auth (mock-oauth2)` and `auth (zitadel)`. They
   are required checks, declared in `.github/repo-standard.yaml` (ADR 0039)
   alongside the server's AOT job (ADR 0101).

## Alternatives considered

- **Only (a).** Deterministic and everywhere, and it proves only that we agree
  with ourselves about what a token looks like.
- **Keycloak in place of Zitadel.** Equally real. Zitadel was the maintainer's
  choice; its management API seeds a fresh instance without a realm export
  file.
- **Testcontainers.** Rejected in ADR 0099.
- **Entra in every run, with a CI tenant.** It would put a credential into
  every pull request's environment, including from forks. A `main`-restricted
  environment cannot leak to a fork.

## Consequences

- A permission mistake fails everywhere, in seconds, with no network.
- A provider change (key rotation, claim shape) fails a Linux job, not a user.
- Two new required checks, and the workflow's time grows by the containers'
  start time; measured in the 7a record.

## Checks

- **Checked against the accepted ADRs** (0001–0090) and specification 1.5.
  Touches:
  - **0037**: its test consequence, refined;
  - **0036**: the devcontainer;
  - **0039**: required checks declared;
  - **0088**: checks by job name.

  No conflict.
- **Layer ownership.** Tests of `Varve.Server` (6), the workflow, and `eng/`.
- **Analyzer rule.** None.
- **Open questions owned.** None.
