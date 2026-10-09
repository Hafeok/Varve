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

Register an application for the API with an application ID URI (the
audience), define app roles — `Varve.Read`, `Varve.Write`, `Varve.Admin`, or
any names — and assign them to users, groups or the client applications and
managed identities that call. Tokens must be v2 tokens; `oid` is the stable
subject.

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

Keycloak, Zitadel, Google and the rest work with the same code: the
authority is the issuer, the audience is what the issuer puts in `aud` for
the API, and `RoleClaimType` names the claim that carries roles (Keycloak's
`realm_access.roles` needs a mapper to a flat claim; Zitadel's project roles
arrive as an object, which is read as above). Zitadel and mock-oauth2-server
are exercised in CI (ADR 0100).

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
