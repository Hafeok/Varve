# Authenticate

Varve validates OpenID Connect bearer tokens and maps their claims to
permissions (ADRs 0037, 0099, 0100). It has no user store, issues no
credential, and knows no API key. Anonymous mode exists for development and
is refused in production.

## The model

Each request names a dataset and needs one permission on it:

| Permission | Covers |
|---|---|
| `read` | queries, Graph Store reads, the service description, the feed, the diff |
| `write` | updates and Graph Store writes; grants `read` |
| `admin` | the dataset's management resources — its declaration, settings, state, status, checkpoints and grants; grants both; dataset-wide, every graph (ADR 0107) |
| operator | every permission on every dataset, and the fleet's management resources: the fleet graph, every declaration, `/manage/apply`, `/manage/system` (ADR 0163) |

**Grants are declarations** in the management plane (ADRs 0163, 0166), one
graph per dataset in the fleet's system dataset, applied with `varve apply`;
[Datasets](datasets.md) shows a complete one. A grant names its grantee as an
agent IRI (`<issuer>#<subject>`), a role (a value of the role claim,
`Auth:RoleClaimType`, `roles` by default), or a group (a value of another
claim it names). The role claim is a string, several strings, or an object
whose property names are the roles, as Zitadel's project roles are.

**Grants are scoped by graph** (ADR 0107): explicit graph IRIs and
`varve:defaultGraph`, IRI prefixes, every graph (`varve:allGraphs true`), or a
template over the caller, such as `https://example.org/users/{sub}/`, for
on-behalf-of users who own their graphs:

```trig
<urn:people:writers> a varve:Grant ; varve:grantee <urn:people:people> ;
  varve:permission varve:write ;
  varve:graph varve:defaultGraph, <https://example.org/graphs/people> .
<urn:people:public> a varve:Grant ; varve:grantee <urn:people:public-role> ;
  varve:permission varve:read ; varve:graphPrefix "https://example.org/graphs/public/" .
```

A caller's readable graphs are the union of its `read` and `write` grants',
its writable graphs the union of its `write` grants'. A graph outside the readable set is not
there for that caller: a query does not see it, a `FROM` naming it names an
empty graph, a Graph Store `GET`, `PUT` or `DELETE` of it is `404`, and the
feed and the diff omit its changes. A write that changes a graph outside the
writable set is `403 graph-not-writable` and commits nothing.

**The grants in force are those at the management position the instance has
applied** (ADR 0163 point 3). Every response names it in
`Varve-Management-Position`. A request that must see a later grant, such as
one a deployment just applied, sends `Varve-Min-Management-Position: p`; the
instance advances once, and answers `503 management-behind` if it is still
behind. A commit records the position it was authorised at (ADR 0165).

The commit's agent is the token's `Auth:SubjectClaim` (`sub`; `oid` for
Entra), written as `<issuer>#<subject>`; it is in every feed record and
survives the provider.

## Microsoft Entra ID

**The API's registration.** Register an application for the server, set an
application ID URI (`api://varve`, the audience), and under *Token
configuration* set `accessTokenAcceptedVersion` to `2` in the manifest, so
that tokens are v2 and `iss` is `https://login.microsoftonline.com/{tenant}/v2.0`.
Define **app roles** on it, allowed for users/groups and for applications:
`Varve.Read`, `Varve.Write`, `Varve.Admin`, or any names you choose; they
arrive in the `roles` claim. Assign them to users and groups under
*Enterprise applications → Users and groups*, and to calling applications by
granting the application permission and consenting.

**A daemon or a service**, with its own registration and a secret or a
certificate, takes a token by client credentials with
`scope=api://varve/.default`; the app roles granted to it as application
permissions are its `roles`. **A managed identity** (a VM, a container app,
a function) does the same through the instance metadata endpoint with
`resource=api://varve`, and is assigned app roles by its object id:

```sh
az ad app show --id api://varve --query appRoles        # the role ids
az rest --method POST --uri "https://graph.microsoft.com/v1.0/servicePrincipals/{identity-object-id}/appRoleAssignments" \
  --body '{"principalId":"{identity-object-id}","resourceId":"{api-service-principal-id}","appRoleId":"{Varve.Write role id}"}'
```

**A person** at the `varve` command line signs in by device code with a
public client registration that has `api://varve/.default` as a delegated
permission (`varve … --authority https://login.microsoftonline.com/{tenant}/v2.0
--client-id {cli-app-id}`); the roles the person holds through assignment
arrive in `roles`. `oid` is the stable subject across tokens and client
applications, so `SubjectClaim` is `oid`, and a commit's agent is
`https://login.microsoftonline.com/{tenant}/v2.0#{oid}`.

CI runs this leg against a real tenant on `main` (ADR 0100, the
`auth (providers)` job), with the secrets the maintainer holds.

```json
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
```

For the management plane, register a second application ID URI,
`api://varve-manage`, with its own app roles (`Varve.Operator`, and any a
dataset's admin grant names); a token for it is asked for with
`scope=api://varve-manage/.default`.

### An application acting for its users: on-behalf-of

A website or an MCP server holds its user's token for **its own** API. To
call Varve as that user it exchanges the token at Entra for one whose
audience is Varve: the on-behalf-of flow (OAuth 2.0 token exchange). Varve
validates the result like any token. **The commit's agent is the user**
(`oid`), and the request span names the application (`azp`, as
`varve.azp`, ADR 0127). One `SparqlHttpClient` serves every user: its
token provider is called for each request and answers the current user's
token.

1. **Varve's registration** exposes a delegated scope as well as its app
   roles. Under *Expose an API*, add `access_as_user` (`api://varve/access_as_user`),
   consentable by admins. The users' app-role assignments (`Varve.Read`,
   `Varve.Write`, …) are unchanged, and arrive in the exchanged token's
   `roles` as they do in any token for Varve.
2. **The calling application's registration** is a confidential client of
   its own: its own application ID URI, which is the audience of the tokens
   its users send it, and its credential: a federated credential (a managed
   identity, a workload identity, GitHub Actions' OIDC token) or a
   certificate, never a secret if it can be helped.
   Under *API permissions* it is granted Varve's `access_as_user`, delegated,
   with admin consent.
3. **The exchange.** For each user, the application posts the user's token
   to the tenant's token endpoint:

   ```http
   POST https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token
   Content-Type: application/x-www-form-urlencoded

   grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer
   &client_id={calling-app-id}
   &client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer
   &client_assertion={the application's federated token, or a certificate's signed assertion}
   &assertion={the user's token for the calling application}
   &scope=api://varve/access_as_user
   &requested_token_use=on_behalf_of
   ```

   MSAL does this as `AcquireTokenOnBehalfOf`, with a per-user cache, and
   Microsoft Identity Web wraps it as `ITokenAcquisition`.
   `samples/Varve.OnBehalfOf.Sample` is a minimal API that does exactly this
   and passes the result to the client's token provider.
4. **What Varve reads** from the exchanged token:
   - `iss` and `aud` (validated);
   - **`oid`**, the user: the commit's agent, `<issuer>#<oid>`;
   - `roles`, the user's app roles on Varve's registration: its permissions;
   - **`azp`**, the calling application: the span attribute `varve.azp`,
     never written to the log (`Auth:ApplicationClaim`; `appid` for a v1
     token).

   `scp` (`access_as_user`) is Entra's evidence of the delegation. Varve
   grants by `roles` alone.

`deploy/azure/entra/` creates both registrations with the Microsoft Graph
Bicep extension: Varve's API with its roles and scope, and a calling
application granted the scope.

#### The attribution rule

**A write made through on-behalf-of is recorded with the end user as its
agent.** A write the application makes **on its own account**, such as an
import or a scheduled publish, is made with the application's **client
credentials** token, and is recorded with **the application as its agent**
(its service principal's `oid`). These are different agents in the history.
An application that routes its own writes through a user's token attributes
them to that user. **Which one a write is, is the caller's choice, write by
write**, and Varve records what the token says.

In the commits list, one application making both kinds of write:

```
commit 41 Data 2026-10-13T09:12:04.1234567Z
agent <https://login.microsoftonline.com/{tenant}/v2.0#4e2d…alice-oid>
cause "0HN7Q4:00000003"
+ <https://shop.example/orders/1001> <https://shop.example/ns#placedBy> <https://shop.example/customers/7>

commit 42 Data 2026-10-13T09:30:00.0000000Z
agent <https://login.microsoftonline.com/{tenant}/v2.0#9b1f…shop-service-principal-oid>
cause "0HN7Q4:00000009"
+ <https://shop.example/prices/2026-10/p1> <https://shop.example/ns#amount> "19.90"^^<http://www.w3.org/2001/XMLSchema#decimal>
```

Commit 41 is Alice placing an order through the shop, on her behalf.
Commit 42 is the shop's nightly price import, on its own account. Both
requests' spans carry the shop's `varve.azp`. Only the history says who.

## Any other issuer

Keycloak, Zitadel, Google, Auth0, Okta and the rest work with the same code.
What the server needs of an issuer:

1. **Discovery** at `{Authority}/.well-known/openid-configuration`, whose
   `issuer` equals `Authority` exactly (trailing slash included) and whose
   `jwks_uri` serves the signing keys; keys are refreshed as the middleware
   does. `RequireHttpsMetadata` is on unless the issuer is a loopback test
   issuer.
2. **JWT access tokens** (not opaque ones), signed with an asymmetric key,
   with `iss`, `aud`, `exp` and `sub`; `aud` is one of `Audiences`. An
   issuer that puts the client id in `aud` by default needs an *audience*
   (Keycloak: an audience mapper on the client scope; Zitadel: the project's
   `…:aud` scope; Auth0 and Okta: an API/authorization server whose
   identifier is the audience).
3. **Roles as a flat claim of strings** named by `RoleClaimType`: Keycloak's
   `realm_access.roles` is nested, so add a *User Realm Role* mapper with a
   flat claim name such as `roles`; Zitadel's project roles arrive as an
   object keyed by role under `urn:zitadel:iam:org:project:{id}:roles`, which
   the server reads by its keys. Groups work the same way when the issuer
   emits them as strings.
4. **A subject** that is stable: `sub` by default; `SubjectClaim` picks
   another (`oid` for Entra, `email` where that is what is stable).

```json
"Auth": {
  "Mode": "Oidc",
  "Authority": "https://login.example/realms/varve",
  "Audiences": [ "varve-api" ],
  "RoleClaimType": "roles"
},
"Manage": { "BootstrapOperators": [ "operator" ] }
```

Zitadel and mock-oauth2-server are exercised in CI on every pull request,
in process and against the container image (ADR 0100, ADR 0111).

### What a refusal looks like

A request without a usable token is `401 unauthorized` with
`WWW-Authenticate: Bearer`, or `Bearer error="invalid_token"` when a token
was sent and failed (expired, wrong audience, unknown key), and a thin
problem body; a token that lacks the permission is `403 forbidden` with
`Bearer error="insufficient_scope"` (RFC 6750, ADR 0119). Neither says which
datasets exist. The request id in `Varve-Request-Id` and the problem's
`instance` finds the refusal in the server's log, where the middleware's
reason is at `Information` under `Microsoft.AspNetCore.Authentication`.

## The management plane

Management is a plane of its own, under `/manage/` (ADR 0163 point 1):

- **Its own audience.** A management route takes a token for
  `Auth:Management:Audience`, `api://varve-manage` by default; a data route
  takes one for `Auth:Audiences`. Each plane runs its own bearer scheme and
  accepts only its own audience, and a token whose `aud` names both is
  refused by both. `Varve:Manage:Urls` puts the management plane on a
  listener of its own, for network policy.
- **Operators and dataset admins.** An operator is a bootstrap operator
  (`Varve:Manage:BootstrapOperators`: agent IRIs or role values, always
  operators) or holds a `varve:operator` grant in the fleet graph. A dataset's
  admin holds an `admin` grant on it, and reaches that dataset's resources
  alone. Only an operator declares a new dataset.
- **Bootstrap.** The first system writer records the configured bootstrap
  operators; an instance whose configured set differs is not ready. `varve
  serve --record-bootstrap` on the system writer records a new set.
- **CI authenticates by workload identity federation**: no key, no secret.
  The job's own OIDC token is the client's assertion, exchanged at the issuer
  for a token for the management audience:

  ```sh
  varve apply declarations/*.trig --server https://varve.example \
    --authority https://login.microsoftonline.com/{tenant}/v2.0 \
    --client-id {deployer-app-id} --client-assertion github \
    --scope api://varve-manage/.default
  ```

  `--client-assertion github` asks GitHub Actions for the job's token (the
  job needs `id-token: write`; `--assertion-audience` names its audience,
  `api://AzureADTokenExchange` by default); `--client-assertion <file>` reads
  a federated token file, as Kubernetes workload identity mounts one.

## Anonymous mode

```json
"Auth": { "Mode": "Anonymous" }
```

Every caller holds every permission on every graph, the management plane's
included, commits name no agent,
and the server warns at every start. `Production: true` refuses it.

## The command line

`varve` against a URL carries a bearer token in one of three ways (ADR 0105):

1. **`--token`, or `VARVE_TOKEN`**: a token obtained elsewhere — a CI job's,
   a managed identity's. Nothing is stored.
2. **Client credentials**, for automation: `--authority <issuer> --client-id
   <id> --client-secret <secret>` (or `VARVE_CLIENT_SECRET`), with `--scope`
   where the issuer needs one. No refresh token is issued, so nothing is
   stored; each invocation authenticates again, which is cheap.
3. **The device code flow**, for a person: `--authority <issuer> --client-id
   <id>`. The command prints a URL and a code; approve it in a browser, and
   the command continues. The issuer's refresh token is kept, and the next
   invocation uses it first.

### The credential file

The refresh token — with the server, the issuer and the client id it was
obtained for, and nothing else, never an access token — is kept in one file:

| Platform | Path |
|---|---|
| Linux, macOS | `$XDG_CONFIG_HOME/varve/credentials.json`, else `~/.config/varve/credentials.json`, created mode `0600`; a file readable by group or others is refused |
| Windows | `%LOCALAPPDATA%\varve\credentials.json`, each token protected with DPAPI for the current user |

`--credentials <path>` names another file. **`--no-store`** obtains a token
and writes nothing: use it in CI and on a shared machine.

**On macOS the file is weaker than the Keychain**: any process running as the
user can read it, where the Keychain prompts per application. What bounds the
exposure: the token is a refresh token the issuer can revoke, it is scoped to
the client id you registered for the command line, and `--no-store` keeps it
off the disk altogether. A token the issuer no longer accepts is dropped from
the file, and the flow starts over.

### The server's own outbound calls

`SERVICE` and `LOAD` reach other servers anonymously, within the endpoint and
source allow-lists ([Configure](configure.md)). Varve attaches no credential
to an outbound request.
