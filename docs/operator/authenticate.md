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
| `admin` | the dataset's status, settings and checkpoints; grants both; dataset-wide, every graph (ADR 0107) |
| server admin | `Auth:Server:Admin`: creating, opening, closing and deleting datasets, and every permission on every dataset (ADR 0106) |

A caller holds a permission when one of the values of its role claim
(`Auth:RoleClaimType`, `roles` by default) is listed for it. The claim is a
string, several strings, or an object whose property names are the roles, as
Zitadel's project roles are.

**Graph-scoped grants** (ADR 0107) bound a `read` or `write` to graphs:

```json
"Datasets": {
  "people": {
    "Admin": [ "Varve.Admin" ],
    "Grants": [
      { "Claim": "Varve.People", "Permission": "write", "Graphs": [ "default", "https://example.org/graphs/people" ] },
      { "Claim": "Varve.Public", "Permission": "read",  "GraphPrefixes": [ "https://example.org/graphs/public/" ] }
    ]
  }
}
```

A caller's readable graphs are the union of its `read` and `write` grants',
its writable graphs the union of its `write` grants'; the `Read` and `Write`
lists are the every-graph case. A graph outside the readable set is not
there for that caller: a query does not see it, a `FROM` naming it names an
empty graph, a Graph Store `GET`, `PUT` or `DELETE` of it is `404`, and the
feed and the diff omit its changes. A write that changes a graph outside the
writable set is `403 graph-not-writable` and commits nothing.

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
  "Datasets": { "people": { "Read": [ "Varve.Read" ], "Write": [ "Varve.Write" ], "Admin": [ "Varve.Admin" ] } }
}
```

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
  "RoleClaimType": "roles",
  "Datasets": { "people": { "Read": [ "reader" ], "Write": [ "writer" ] } },
  "Server": { "Admin": [ "operator" ] }
}
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

## Anonymous mode

```json
"Auth": { "Mode": "Anonymous" }
```

Every caller holds every permission on every graph, commits name no agent,
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
