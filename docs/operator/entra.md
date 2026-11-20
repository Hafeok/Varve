# Microsoft Entra ID

A procedure for a server whose tokens come from one Entra tenant. No secret
and no certificate is created anywhere, and none is needed:

- **Automation** authenticates by **workload identity federation**. The
  platform it runs on already issues it a token (GitHub Actions, Kubernetes
  workload identity, a managed identity). Entra accepts that token as the
  client's credential, because a *federated identity credential* on the
  client's registration names its issuer and subject.
- **People** sign in through public clients, which hold no credential at all,
  with their own MFA.

The concepts behind each step (the two planes, grants, the agent a commit
records) are in [Authenticate](authenticate.md). Every step here needs a
tenant, and CI's test tenant is not provisioned (ADR 0223), so no CI job runs
this page; it is listed under [Not run in CI](#not-run-in-ci).

**What you need:**
- a tenant, and in it the right to create app registrations and grant admin
  consent (Application Administrator, or Cloud Application Administrator);
- `az` signed in to that tenant;
- the Bicep CLI, `az bicep install`.

## 1. Create the registrations

`deploy/azure/entra/main.bicep` creates every registration this page uses, at
tenant scope, with the Microsoft Graph Bicep extension. The deploying identity
needs `Application.ReadWrite.All` and `DelegatedPermissionGrant.ReadWrite.All`.

| Registration | What it is | Its credential |
|---|---|---|
| Varve's API, `api://varve` | the data plane's audience: the app roles `Varve.Read`, `Varve.Write`, `Varve.Admin`, and the delegated scope `access_as_user` | none; it is a resource |
| The management API, `api://varve-manage` | the management plane's audience: the app role `Varve.Operator`, and its own `access_as_user` | none; it is a resource |
| The deployer | what `varve apply` runs as in CI, granted `Varve.Operator` | a federated credential for the GitHub environment `varve-deploy` |
| The Web UI | a single-page application, code flow with PKCE | none: a public client |
| The command line | a public client for the device code flow | none: a public client |
| A calling application | a website or an MCP server that calls Varve as its users (step 7) | a federated credential for the GitHub environment `entra-obo` |
| The sign-in client | the public client the on-behalf-of leg's user signs in through | none: a public client |
| CI's provider leg | the client `docs/auth-providers.md`'s Entra leg runs as | a federated credential for the GitHub environment `auth-providers` |

<!-- ci: none — it needs an Entra tenant, and the test tenant is not provisioned (ADR 0223); the bicep job builds and lints the template on every pull request -->
```sh
az deployment tenant create --name varve-entra --location westeurope \
  --template-file deploy/azure/entra/main.bicep \
  --parameters environmentName=prod githubRepository=<owner>/<repo> \
    webUiRedirectUris='["https://varve.example/ui/"]'
```

**Check:** the deployment's outputs name every client id this page uses.

<!-- ci: none — it needs an Entra tenant, and the test tenant is not provisioned (ADR 0223) -->
```sh
az deployment tenant show --name varve-entra --query properties.outputs
```

| Output | Used in |
|---|---|
| `varveAudience`, `managementAudience` | step 3, the server's audiences |
| `deployerClientId` | step 4 |
| `webUiClientId`, `dataScope`, `manageScope` | step 6 |
| `cliClientId` | step 6 |
| `callerClientId`, `exchangeScope`, `signInClientId` | step 7 |
| `providerLegClientId` | `docs/auth-providers.md` |

A deployment of your own needs only the registrations it uses. The template
creates them all, and each is independent of the others except through the
two APIs.

## 2. Assign the roles

The app roles decide what a caller may do. Varve reads them from the `roles`
claim, and grants refer to them by value (step 5).

- **People and groups:** under *Enterprise applications → Varve API → Users
  and groups*, assign `Varve.Read`, `Varve.Write` or `Varve.Admin`. Under
  *Varve management API*, assign `Varve.Operator` to whoever operates the
  fleet.
- **Applications and managed identities:** an application permission, by the
  identity's service principal object id. The template has already done this
  for the deployer, the calling application and CI's provider leg.

<!-- ci: none — it needs an Entra tenant, and the test tenant is not provisioned (ADR 0223) -->
```sh
api=$(az ad sp show --id api://varve --query id -o tsv)
role=$(az ad sp show --id api://varve --query "appRoles[?value=='Varve.Write'].id" -o tsv)
az rest --method POST \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$api/appRoleAssignedTo" \
  --body "{\"principalId\":\"<identity-object-id>\",\"resourceId\":\"$api\",\"appRoleId\":\"$role\"}"
```

**Check:** each assignment is listed on the API's service principal.

<!-- ci: none — it needs an Entra tenant, and the test tenant is not provisioned (ADR 0223) -->
```sh
az rest --method GET \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$api/appRoleAssignedTo" \
  --query "value[].{who:principalDisplayName, role:appRoleId}"
```

## 3. Configure the server

The registrations issue v2 tokens, so the issuer is
`https://login.microsoftonline.com/{tenant}/v2.0`. `oid` is the user's or the
service principal's stable id across every client application, so it is the
subject, and a commit's agent is `https://login.microsoftonline.com/{tenant}/v2.0#{oid}`.

<!-- ci: none — it needs an Entra tenant; auth (providers) runs these settings and skips until the tenant exists (ADR 0223) -->
```json
"Varve": {
  "Auth": {
    "Mode": "Oidc",
    "Authority": "https://login.microsoftonline.com/{tenant}/v2.0",
    "Audiences": [ "api://varve" ],
    "SubjectClaim": "oid",
    "RoleClaimType": "roles",
    "Production": true,
    "Management": { "Audience": "api://varve-manage" }
  },
  "Manage": { "BootstrapOperators": [ "Varve.Operator" ] }
}
```

`BootstrapOperators` names the role, so anyone holding `Varve.Operator`, the
deployer included, is an operator from the first start.

**Check:** a request with no token is refused, and names the scheme.

<!-- ci: none — it needs an Entra tenant and a server configured for it, and the test tenant is not provisioned (ADR 0223) -->
```sh
curl -si https://varve.example/manage/system | head -n 3
# HTTP/1.1 401 Unauthorized
# WWW-Authenticate: Bearer
```

## 4. Declare the fleet from CI, keyless

`varve apply` runs as the deployer. Its only credential is the GitHub Actions
job's own OIDC token for the `varve-deploy` environment. With
`--client-assertion github`, the command asks GitHub for that token, for the
audience `api://AzureADTokenExchange`. It then presents the token to Entra as
the client's assertion, in the client credentials grant with a JWT client
assertion (RFC 7523 §2.2), and receives a token for the management audience.

Restrict the `varve-deploy` environment to `main` (*Settings → Environments →
Deployment branches*): the federated credential trusts any job that runs in
that environment.

<!-- ci: none — it needs an Entra tenant (ADR 0223); ci.yml container runs the same varve apply --client-assertion github against mock-oauth2 -->
```yaml
jobs:
  declare:
    runs-on: ubuntu-latest
    environment: varve-deploy
    permissions:
      contents: read
      id-token: write
    steps:
      - uses: actions/checkout@<sha>
      - run: |
          varve apply declarations/*.trig --server https://varve.example \
            --authority https://login.microsoftonline.com/${{ vars.VARVE_ENTRA_TENANT }}/v2.0 \
            --client-id ${{ vars.VARVE_DEPLOYER_CLIENT_ID }} \
            --client-assertion github \
            --scope api://varve-manage/.default
```

Both values are identifiers, kept as environment variables, not secrets.

**Elsewhere:**
- **On Kubernetes with workload identity**, the platform mounts a token file
  and names it in `AZURE_FEDERATED_TOKEN_FILE`; pass that path, as in
  `--client-assertion "$AZURE_FEDERATED_TOKEN_FILE"`. The deployer's
  registration then needs a federated credential for the service account's
  subject instead of GitHub's.
- **On Container Apps**, `varve manage` (not `varve apply`) can take the app's
  managed identity with `--managed-identity --scope api://varve-manage`
  ([Azure Container Apps](azure-container-apps.md)).

**Check:** the job's log ends with the position the declarations were applied
at. Every later response names a position at least as far, in
`Varve-Management-Position`.

## 5. Grant the roles

Grants are declarations, applied in step 4. A grant whose grantee is a role
names the role's value:

<!-- ci: ci.yml build — Varve.Server.Tests: role grants declared as these are (not this file); Entra's values need a tenant -->
```trig
<https://example.org/manage/readers> a varve:Role ; varve:claimValue "Varve.Read" .
<https://example.org/manage/read> a varve:Grant ; varve:grantee <https://example.org/manage/readers> ;
  varve:permission varve:read ; varve:allGraphs true .
```

[Datasets](datasets.md) has a complete declaration. **Check:** the step 6
sign-in reads with `Varve.Read` and is refused a write with `403 forbidden`.

## 6. People: the Web UI and the command line

**The Web UI** signs in with the code flow and PKCE, through its own public
client, and asks for one token per plane. Give the server the Web UI's client
and the two scopes ([The Web UI](web-ui.md#signing-in)):

<!-- ci: none — signing in with Entra needs a tenant, and the test tenant is not provisioned (ADR 0223) -->
```json
"Varve": {
  "WebUi": {
    "Auth": {
      "ClientId": "<webUiClientId>",
      "DataScope": "api://varve/access_as_user",
      "ManageScope": "api://varve-manage/access_as_user"
    }
  }
}
```

Add every origin the console is served from to `webUiRedirectUris` (step 1),
each ending in `/ui/`.

**The command line** signs a person in by device code, through the command
line's public client. It prints a URL and a code; approve the code in a
browser, and the command continues ([The command line](authenticate.md#the-command-line)):

<!-- ci: none — a device code sign-in against Entra needs a tenant and a person to approve it (ADR 0223) -->
```sh
varve query https://varve.example/datasets/orders/ -q 'ASK {}' \
  --authority https://login.microsoftonline.com/{tenant}/v2.0 \
  --client-id <cliClientId> --scope api://varve/access_as_user
```

**Check:** the query answers. The commit a write of yours makes names your
`oid` as its agent.

## Automation on the data plane

**The data-plane commands have no keyless sign-in of their own yet** (#301).
`varve query`, `update`, `load` and the rest take a bearer token, a client
secret, or a person's device code. Only `varve apply` and `varve manage` take
`--client-assertion`. Until #301 lands, automation that reads or writes data
gets its token from the platform and passes it in `VARVE_TOKEN`, which keeps
it keyless:

<!-- ci: none — it needs an Entra tenant and an identity in it (ADR 0223) -->
```sh
# a managed identity, or any az login that is federated rather than secret
export VARVE_TOKEN=$(az account get-access-token --resource api://varve --query accessToken -o tsv)
varve update https://varve.example/datasets/orders/ -u 'INSERT DATA { <urn:a> <urn:b> <urn:c> }'
```

An application written in .NET passes a token provider to
`SparqlHttpClient` instead. Azure.Identity's `DefaultAzureCredential` takes
the same managed or workload identity.

## 7. An application acting for its users: on-behalf-of

A website or an MCP server holds its user's token for **its own** API. To
call Varve as that user, it exchanges the token at Entra for one whose
audience is Varve: the on-behalf-of flow. Varve validates the result like any
token.

- **The commit's agent is the user** (`oid`), and the request span names the
  application (`azp`, as `varve.azp`, ADR 0127).
- **One `SparqlHttpClient` serves every user.** Its token provider is called
  for each request and answers the current user's token.

The template creates both halves:
- **Varve's API** exposes the delegated scope `access_as_user`
  (`api://varve/access_as_user`), consentable by admins. The users' app-role
  assignments arrive in the exchanged token's `roles` as in any token for
  Varve.
- **The calling application** is a confidential client with its own
  application ID URI, the audience of the tokens its users send it. It is
  granted Varve's `access_as_user`, delegated, with admin consent. Its
  credential is a federated credential (a managed identity, a workload
  identity, GitHub Actions' OIDC token), never a secret.

**The exchange.** For each user, the application posts the user's token to
the tenant's token endpoint. Its own credential is again a client assertion,
its platform's token:

<!-- ci: none — on-behalf-of against Entra runs only in entra-obo.yml, by hand with the maintainer's MFA sign-in (ADR 0127), and the tenant is not provisioned -->
```http
POST https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token
Content-Type: application/x-www-form-urlencoded

grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer
&client_id={calling-app-id}
&client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer
&client_assertion={the application's federated token}
&assertion={the user's token for the calling application}
&scope=api://varve/access_as_user
&requested_token_use=on_behalf_of
```

MSAL does this as `AcquireTokenOnBehalfOf`, with a per-user cache, and
Microsoft Identity Web wraps it as `ITokenAcquisition`.
`samples/Varve.OnBehalfOf.Sample` is a minimal API that does exactly this and
passes the result to the client's token provider.

**What Varve reads from the exchanged token:**
- `iss` and `aud`, which it validates;
- **`oid`**, the user: the commit's agent, `<issuer>#<oid>`;
- `roles`, the user's app roles on Varve's registration: its permissions;
- **`azp`**, the calling application: the span attribute `varve.azp`, never
  written to the log (`Auth:ApplicationClaim`; `appid` for a v1 token).

`scp` (`access_as_user`) is Entra's evidence of the delegation. Varve grants
by `roles` alone.

**Check:** a write the application makes for a user names the user's `oid` as
its agent. A write it makes on its own account, with its client credentials
token, names its service principal: [the attribution
rule](authenticate.md#the-attribution-rule).

## Not run in CI

What on this page no CI job runs, and why (ADR 0248):

- **Every step.** Each needs an Entra tenant, and the test tenant is not
  provisioned (ADR 0223).
  - The `bicep` job builds and lints the template on every pull request.
  - `auth (providers)` runs step 3's settings on `main` once the tenant exists.
  - `ci.yml`'s `container` job runs step 4's `varve apply --client-assertion github` against mock-oauth2-server.
- **On-behalf-of** runs only in `entra-obo.yml`, dispatched by hand with the
  maintainer's MFA sign-in (ADR 0127).
- **The command line's device code** against Entra needs a person to approve
  it.
