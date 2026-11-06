# 0223 — The provider legs by federated credentials: GitHub's OIDC token for Entra ID and Google

## Status

**Filed unaccepted by #101, 2026-11-06** (ADR 0066). Acceptance is the
maintainer's act on the pull request. Decided by the maintainer on #101
(2026-11-06): the provider legs move to federated credentials from GitHub's
OIDC token, with no client secret and no service-account key, here or
anywhere; the test tenants are set up later, so the legs skip until they are,
and the setup is documented.

**Supersedes in part [ADR 0100](0100-authentication-tested-in-three-layers.md)**,
point 4, which is accepted and is not edited: how the Entra ID and Google
legs get their tokens, and what the `auth-providers` environment holds.
`EntraAndGoogleBySecret` moves to this ADR's set under its key, restated (ADR
0068 points 2 and 4). The rest of 0100 stands, and so does what point 4
asserts: `oid` as the subject and the app role in `roles` for Entra, the
permission granted to the `email` claim for Google, one test each, and a job
on pushes to `main` that is not a required check.

Records decision set
[`provider-legs-by-federated-credentials`](../decisions/provider-legs-by-federated-credentials.md).

## Context

ADR 0100 point 4 has the Entra leg take a client secret and the Google leg a
service account's key file, each from the `auth-providers` environment. Both
are long-lived credentials, and neither was ever added: the weekly
repo-standard check reported them "declared and not present" from 2026-10-10
until #101 diagnosed it.

The standing rule since ADR 0037 is that Varve accepts and holds no credential
but an OIDC bearer token, and every other CI leg that reaches a cloud already
signs in without a stored secret:
- the object-storage legs, by the job's OIDC token as a federated credential
  for a storage token (ADR 0137, `azure-objects.yml`);
- the on-behalf-of leg, by the same token as the calling application's client
  assertion (ADR 0127, `entra-obo.yml`).

Both providers accept GitHub Actions' OIDC token, so the provider legs can do
the same:
- **Entra ID** trusts a federated identity credential on an app registration,
  naming an issuer, a subject and an audience. The token is presented as the
  client assertion of the client-credentials grant (RFC 7521 §4.2, RFC 7523
  §2.2).
- **Google** trusts a workload identity pool's OIDC provider. Its Security
  Token Service exchanges the token for a federated access token (RFC 8693),
  which can call IAM Credentials as a principal of the pool.

The job's token is minted by GitHub per job and lives minutes. A pull
request's job can't get one whose subject names the `auth-providers`
environment, because only `main` deploys to it.

## Decision

1. **Entra ID: client credentials with a federated identity credential.**
   - The job requests GitHub's OIDC token for the audience
     `api://AzureADTokenExchange`, with `id-token: write` on that job only.
   - The test sends it to the tenant's v2 token endpoint,
     `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token`, with
     `grant_type=client_credentials`, the client's id, `scope={audience}/.default`,
     `client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer`,
     and the token as `client_assertion`.
   - The client's registration has one federated credential: issuer
     `https://token.actions.githubusercontent.com`, subject
     `repo:Hafeok/Varve:environment:auth-providers`, audience
     `api://AzureADTokenExchange`.
   - What is asserted is unchanged: the API's registration issues v2 tokens,
     the subject is `oid`, the app role (`Varve.Write` unless
     `VARVE_ENTRA_ROLE` names another) is in `roles`, and that token writes.
2. **Google: Workload Identity Federation.**
   - The job's OIDC token is requested for the audience
     `https://iam.googleapis.com/{provider}`, the provider's own default
     allowed audience, where `{provider}` is the provider's resource name
     `projects/{number}/locations/global/workloadIdentityPools/{pool}/providers/{id}`.
   - It is exchanged at `https://sts.googleapis.com/v1/token` (token exchange,
     audience `//iam.googleapis.com/{provider}`, the `cloud-platform` scope)
     for a federated access token.
   - That token calls IAM Credentials'
     `projects/-/serviceAccounts/{email}:generateIdToken` with an audience of
     our choosing (`https://w3id.org/varve/tests`) and `includeEmail`.
   - The pool's provider maps `assertion.repository` and
     `assertion.environment` to attributes, and its attribute condition
     admits only this repository and the `auth-providers` environment. The
     service account grants the pool's principals for that repository
     `roles/iam.serviceAccountOpenIdTokenCreator`.
   - What is asserted is unchanged: the issuer is `https://accounts.google.com`,
     and the permission is granted to the `email` claim, which writes.
3. **No third-party action does either exchange**: not `azure/login`, not
   `google-github-actions/auth`. The exchanges are plain HTTP calls in
   `ProviderTests`, three for Google and two for Entra, GitHub's token request
   included, over `HttpClient`. Constraint 4 is the reason, and the calls are
   short.
4. **The configuration is identifiers, not credentials**:
   - Entra: `VARVE_ENTRA_TENANT`, `VARVE_ENTRA_CLIENT_ID`,
     `VARVE_ENTRA_AUDIENCE`;
   - Google: `VARVE_GOOGLE_WORKLOAD_IDENTITY_PROVIDER` (the resource name) and
     `VARVE_GOOGLE_SERVICE_ACCOUNT_EMAIL`.

   They live in the `auth-providers` environment, which only `main` deploys
   to. They are declared as environment secrets in `.github/repo-standard.yaml`
   only because repo-standard cannot yet declare an environment's variables
   (#239). When it can, they move to variables. `VARVE_ENTRA_CLIENT_SECRET`
   and `VARVE_GOOGLE_SERVICE_ACCOUNT` are removed, and no successor of either
   is ever added.
5. **The skip.**
   - A leg with none of its identifiers skips, and the message names every
     one that is missing.
   - A leg with any of them is configured. A missing identifier, a missing
     OIDC token or a failed exchange then fails it, and is never turned into
     a skip.
   - `VARVE_AUTH_LEGS_REQUIRED` (ADR 0100 point 5) turns the skip into a
     failure for `entra` and `google` as for the container legs, for the
     maintainer to set on the job once the tenants exist.
   - `auth (providers)` stays a job on pushes to `main` that is not a required
     check, as ADR 0100 has it.
6. **The setup is the maintainer's**, step by step in `docs/auth-providers.md`:
   the Entra registrations and federated credential, the Google pool, provider,
   condition, service account and binding, the values for the environment,
   and what a correct first run reports.

## Alternatives considered

- **A client secret or a service-account key in the environment** (ADR 0100
  point 4 as written). A long-lived credential, against the standing rule and
  the spirit of ADR 0037. Restricting the environment to `main` limits who can
  read it, but not how long it is good for, or what a copy of it can do.
- **Certificate credentials** for Entra, or a key pair registered on the
  service account for Google. Still a long-lived private key to store and
  rotate. It is only harder to read by accident.
- **The vendors' login actions** (`azure/login`, `google-github-actions/auth`).
  Each is a third-party dependency, with its own pin and its own updates, for
  two or three HTTP calls. They would also do the exchange outside the test,
  so the test would no longer show which calls the providers need.

## Consequences

- No credential for a provider exists anywhere: not in GitHub, not in a
  tenant, not on a machine. Nothing needs rotating and nothing can leak.
- Until the maintainer sets up the tenants, `repo-standard check` reports the
  five identifiers as "declared and not present", and both legs skip. That is
  setup, not drift.
- The legs now also test the federation: a misconfigured credential or
  condition fails them, as a misconfigured provider would.
- A run outside GitHub Actions can't run a configured leg. It has no OIDC
  token, and the leg fails rather than skips. Locally, leave the identifiers
  unset.

## Checks

- **Checked against the accepted ADRs** (0001–0215 that are accepted) and
  specification 1.8. Touches:
  - **0100**: point 4 superseded in part, as above;
  - **0037**: holds, and more strictly;
  - **0127** and **0137**: the same federated pattern, which this follows;
  - **0039**: what the declaration holds for `auth-providers`;
  - **0009**: no package added.

  No other conflict.
- **Layer ownership.** Tests of `Varve.Server` (6) and the workflow. No
  shipped code changes.
- **Analyzer rule.** None.
- **Open questions owned.** None. Environment variables in repo-standard are
  #239.
