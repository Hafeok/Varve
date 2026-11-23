# Any other OpenID Connect issuer

Keycloak, Zitadel, Auth0, Okta and the rest work with the same server code as
[Microsoft Entra ID](entra.md). This page says what an issuer must provide,
then follows CI's Zitadel step by step. Every step on it is a step a CI job
runs. The concepts (the two planes, grants, the agent a commit records) are
in [Authenticate](authenticate.md).

## What the server needs of an issuer

1. **Discovery** at `{Authority}/.well-known/openid-configuration`.
   - Its `issuer` must equal `Authority` exactly, trailing slash included.
   - Its `jwks_uri` serves the signing keys, refreshed as the middleware does.
   - `Varve:Auth:RequireHttpsMetadata` is `true` by default, and the
     `Authority` must then be `https`. Set it to `false` only for a test
     issuer on plain `http`, as CI's Zitadel and mock issuers are.
2. **JWT access tokens**, not opaque ones.
   - They are signed with an asymmetric key, and carry `iss`, `aud`, `exp`
     and `sub`. `aud` must be one of `Audiences`.
   - Some issuers put the client id in `aud` by default. Those need an
     *audience*:
     - Keycloak: an audience mapper on the client scope;
     - Zitadel: the project's `…:aud` scope;
     - Auth0 and Okta: an API or authorization server whose identifier is
       the audience.
3. **Roles as a claim of strings**, named by `RoleClaimType`.
   - Keycloak's `realm_access.roles` is nested, so add a *User Realm Role*
     mapper with a flat claim name such as `roles`.
   - Zitadel's project roles arrive as an object keyed by role, under
     `urn:zitadel:iam:org:project:{id}:roles`. The server reads it by its
     keys.
   - Groups work the same way when the issuer emits them as strings.
4. **A stable subject:** `sub` by default. `SubjectClaim` picks another: `oid`
   for Entra, or `email` where that is what is stable.

<!-- ci: ci.yml auth-zitadel; ci.yml auth-mock-oauth2 — these settings with each issuer's values (ProviderTests) -->
```json
"Varve": {
  "Auth": {
    "Mode": "Oidc",
    "Authority": "https://login.example/realms/varve",
    "Audiences": [ "varve-api" ],
    "RoleClaimType": "roles",
    "Management": { "Audience": "varve-manage" }
  },
  "Manage": { "BootstrapOperators": [ "operator" ] }
}
```

**Google** issues no JWT access token for an API of your own. What CI
exercises for Google is an ID token with an audience of its choosing, from IAM
Credentials' `generateIdToken`, granted by its `email` claim
(`RoleClaimType: email`, ADR 0223). That is the only Google path tested
([docs/auth-providers.md](../auth-providers.md)).

## Keyless automation needs federation at the issuer

`varve apply` and `varve manage` authenticate unattended without a secret
through `--client-assertion` ([Authenticate](authenticate.md#the-management-plane)):
- the platform's own token (a GitHub Actions job's, a Kubernetes service
  account's) is presented as the client's assertion;
- the grant is client credentials with a JWT client assertion (RFC 7523 §2.2).

That works only where **the issuer trusts the platform's token as a client's
credential**: a federated credential, or a trust policy that names the
platform's issuer and subject.
- **Entra does,** through federated identity credentials ([Entra](entra.md#4-declare-the-fleet-from-ci-keyless)).
- **CI runs the flow against mock-oauth2-server,** in `ci.yml`'s `container`
  job, with the job's own GitHub token.

**Entra is the only real issuer `--client-assertion` is written and meant to
be tested against.** It isn't tested there yet: `auth (providers)` will test
it once the tenant exists (ADR 0223). Today only mock-oauth2-server, a test
issuer, runs it. **Keycloak, Auth0 and Okta are untested.**

- **Before relying on any other issuer, check that it does.** An issuer that
  doesn't leaves two choices:
  - a token obtained some other way, passed as `--token` or `VARVE_TOKEN`;
  - a client secret, which this guide does not recommend for production.

## Zitadel, as CI runs it

CI's `auth (zitadel)` job runs a real Zitadel (v4.19.4, with PostgreSQL) and
seeds it through its API with `eng/zitadel-seed.cs`. The same steps run on
your machine, for a local trial. Docker and the .NET SDK are all you need.

### 1. Start Zitadel

The compose file starts Zitadel on `http://localhost:8081`, API only, with no
TLS and no login UI. Its first-instance setup writes two throwaway personal
access tokens to `.github/zitadel/bootstrap/`: the administrator's, which the
seed uses, and the login client's, which approves a device code. In CI the
images come from the repository's private mirror. Locally, an override starts
the upstream images at the same digests ([CONTRIBUTING.md](../../CONTRIBUTING.md)):

<!-- ci: ci.yml auth-zitadel; ci.yml container — the Start Zitadel step, from the mirror rather than the local override -->
```sh
local=$(dotnet run eng/ci-images.cs -- --local .github/zitadel/compose.yaml)
docker compose --file .github/zitadel/compose.yaml --file "$local" up --detach --wait
```

**Check:** `--wait` returns once Zitadel's health check passes. Discovery
then answers at `http://localhost:8081/.well-known/openid-configuration`, and
its `issuer` is `http://localhost:8081`.

### 2. Seed it

<!-- ci: ci.yml auth-zitadel; ci.yml container — the Seed it step -->
```sh
dotnet run eng/zitadel-seed.cs -- --output artifacts/zitadel-seed.json
```

The seed creates, through the management API:

| What | Why |
|---|---|
| a project, `varve`, with the roles `read`, `write` and `admin`, asserting its roles in tokens | the data plane's audience is the project's id, and the roles arrive under `urn:zitadel:iam:org:project:{id}:roles` |
| an API application in that project | the server, as Zitadel knows it |
| a **machine user**, `varve-writer`, with JWT access tokens and **a client secret**, granted `write` | automation, by client credentials |
| a human user, `varve-reader`, with a random password, granted `read`; and a native application `varve-cli`, auth method none, allowed the device code grant | a person at the command line, by device code |
| a second project, `varve-manage`, with the role `operator` granted to the human user | the management plane's audience |
| a user agent application, `varve-webui`, auth method none, allowed code, refresh and token exchange; the instance's `oidcTokenExchange` feature | the Web UI ([The Web UI](web-ui.md#zitadel)) |

It writes what the tests need to `artifacts/zitadel-seed.json`, which is not
committed.

> **The machine user signs in with a client secret, and that is not a
> production pattern.** It exists for a local trial and for the
> `auth (zitadel)` job, against a throwaway instance whose every credential
> dies with it.
>
> **Why a secret:** Zitadel offers a machine user no keyless sign-in that
> Varve's command line can use.
> - **With a key**, a machine user signs in by the JWT profile grant (RFC 7523
>   §2.1: `grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer`, with the
>   JWT, signed by the user's own key, as `assertion`).
>   `varve … --client-assertion` sends the client credentials grant with a
>   client assertion (§2.2) instead.
> - **Its client credentials grant** is documented with `client_id` and
>   `client_secret` only.
> - **Zitadel accepts no other platform's token** as a machine user's
>   credential. Its token exchange takes Zitadel's own tokens, and a
>   self-signed JWT only together with an actor token, for impersonation.
>
> Sources: Zitadel's [grant types](https://zitadel.com/docs/apis/openidoauth/grant-types),
> [endpoints](https://zitadel.com/docs/apis/openidoauth/endpoints) and
> [token exchange](https://zitadel.com/docs/guides/integrate/token-exchange),
> read on 2026-11-19.
>
> **In production with Zitadel**, unattended automation therefore needs
> either a client secret, kept and rotated as one, or a token obtained some
> other way and passed as `--token`.

### 3. Configure the server

The audience is the `varve` project's id, the role claim is its roles claim,
and the subject is `sub`. CI's `container` job starts the image with these
values from the seed file:

<!-- ci: ci.yml container — the Start the container against Zitadel step -->
```sh
seed=artifacts/zitadel-seed.json
docker run --detach --name varve-zitadel --read-only --network host \
  --volume varve-zitadel:/var/lib/varve \
  --env ASPNETCORE_URLS=http://+:8090 \
  --env VARVE__AUTH__MODE=Oidc \
  --env "VARVE__AUTH__AUTHORITY=$(jq -r .issuer "$seed")" \
  --env "VARVE__AUTH__AUDIENCES__0=$(jq -r .audience "$seed")" \
  --env "VARVE__AUTH__ROLECLAIMTYPE=$(jq -r .roleClaim "$seed")" \
  --env VARVE__AUTH__SUBJECTCLAIM=sub \
  --env VARVE__AUTH__REQUIREHTTPSMETADATA=false \
  --env VARVE__MANAGE__BOOTSTRAPOPERATORS__0=operator \
  ghcr.io/hafeok/varve:<version> serve
```

**The dataset is declared first** (ADR 0163 point 7). Before this, the job
runs the same image on the same volume in anonymous mode and declares a
dataset `d`, with grants to the roles `read` and `write`
([Datasets](datasets.md)).
- **Why:** the seed's machine user has a role in the `varve` project only, so
  none of its tokens is for the management plane's audience.
- **To declare from automation instead:** put the machine user in the
  `varve-manage` project with the `operator` role, ask for that project's
  audience scope, and run `varve apply --client-id … --client-secret …`.

**Check:** the server is ready.

<!-- ci: ci.yml container — the same step waits on readiness -->
```sh
curl --silent --fail http://localhost:8090/health/ready
```

### 4. Automation: a token by client credentials

The machine user's token, asked for with the project's audience scope, writes
with the `write` role:

<!-- ci: ci.yml auth-zitadel; ci.yml container — ProviderTests.Zitadel_client_credentials_token_writes_with_its_project_role posts this grant and writes with the token -->
```sh
seed=artifacts/zitadel-seed.json
curl -s http://localhost:8081/oauth/v2/token \
  -d grant_type=client_credentials \
  --data-urlencode "client_id=$(jq -r .writer.clientId "$seed")" \
  --data-urlencode "client_secret=$(jq -r .writer.clientSecret "$seed")" \
  --data-urlencode "scope=$(jq -r .scope "$seed")" | jq -r .access_token
```

The command line does the same with `--authority http://localhost:8081
--client-id … --client-secret … --scope …`.

**Check:** a write with the token commits, and the commit names
`http://localhost:8081#<machine user id>` as its agent.

### 5. A person: the device code

**CI runs this flow over HTTP, not through `varve query`.**
`ProviderTests.Zitadel_device_code_token_reads_and_may_not_write` starts it,
approves it and polls for the token itself. **The local seed has no login
page:** the compose file starts Zitadel's API only, so no browser page takes
the code. You approve it here instead, with the same three API calls a custom
login UI makes, and the test makes.

First, start the command line's device code flow, through `varve-cli`. It
prints a URL and a user code, and waits:

<!-- ci: ci.yml auth-zitadel; ci.yml container — ProviderTests.Zitadel_device_code_token_reads_and_may_not_write runs this flow over HTTP, not through varve query -->
```sh
seed=artifacts/zitadel-seed.json
varve query http://localhost:8090/datasets/d/ -q 'ASK {}' \
  --authority http://localhost:8081 \
  --client-id "$(jq -r .deviceClientId "$seed")" \
  --scope "$(jq -r .scope "$seed")" --no-store
```

Then, in a second terminal, approve the code as `varve-reader`. The calls use
the login client's token, which Zitadel's setup wrote and the seed copied into
its file:
1. create a session for `varve-reader`, with the seed's password;
2. look up the device authorization by the user code `varve query` printed;
3. approve that authorization with the session.

<!-- ci: ci.yml auth-zitadel; ci.yml container — ProviderTests.Zitadel_device_code_token_reads_and_may_not_write makes these three calls -->
```sh
seed=artifacts/zitadel-seed.json
code=<the user code varve query printed>
login=(-H "Authorization: Bearer $(jq -r .loginClientToken "$seed")" -H 'Content-Type: application/json')

session=$(curl -s "${login[@]}" http://localhost:8081/v2/sessions -d "$(jq -n \
  --arg name "$(jq -r .reader.loginName "$seed")" --arg password "$(jq -r .reader.password "$seed")" \
  '{checks: {user: {loginName: $name}, password: {password: $password}}}')")

request=$(curl -s "${login[@]}" "http://localhost:8081/v2/oidc/device_authorization/$code" \
  | jq -r .deviceAuthorizationRequest.id)

curl -s "${login[@]}" "http://localhost:8081/v2/oidc/device_authorization/$request" \
  -d "$(jq -n --argjson s "$session" '{session: {sessionId: $s.sessionId, sessionToken: $s.sessionToken}}')"
```

`varve query` then receives a token whose `read` role reads and may not write.

**To check:** the query should answer `true`, and an update with the same
token should be `403 forbidden`.

### 6. Run what CI runs

<!-- ci: ci.yml auth-zitadel — the Test against it step -->
```sh
VARVE_ZITADEL_SEED=$PWD/artifacts/zitadel-seed.json VARVE_AUTH_LEGS_REQUIRED=zitadel \
  dotnet test --project tests/Varve.Server.Tests/Varve.Server.Tests.csproj \
  --configuration Release --filter-class Varve.Server.Tests.ProviderTests
```

The Entra and Google legs skip without their variables. The mock-oauth2 leg
skips without `VARVE_MOCK_OAUTH2`.

**To start again,** `docker compose --file .github/zitadel/compose.yaml down
--volumes`: the seed seeds a fresh instance once.
