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

<!-- ci: ci.yml build — Varve.Server.Tests: grants by graph, declared as these are (not this file) -->
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

## Issuers

Two pages set an issuer up from nothing, each a procedure with a check after
every step:

- **[Microsoft Entra ID](entra.md):**
  - every registration, from `deploy/azure/entra/main.bicep`;
  - automation by workload identity federation, with no secret anywhere;
  - the Web UI and the command line as public clients;
  - on-behalf-of for an application acting for its users.
- **[Any other OpenID Connect issuer](oidc-issuer.md):**
  - what the server needs of an issuer;
  - why keyless automation needs the issuer to accept federated credentials;
  - Zitadel step by step, as CI's `auth (zitadel)` job runs it.

## The attribution rule

**A write an application makes for its user, through on-behalf-of
([Entra](entra.md#7-an-application-acting-for-its-users-on-behalf-of)), is
recorded with the end user as its agent.** A write the application makes **on its own account**, such as an
import or a scheduled publish, is made with the application's **client
credentials** token, and is recorded with **the application as its agent**
(its service principal's `oid`). These are different agents in the history.
An application that routes its own writes through a user's token attributes
them to that user. **Which one a write is, is the caller's choice, write by
write**, and Varve records what the token says.

In the commits list, one application making both kinds of write:

<!-- ci: example — what the commits list shows for the two writes -->
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

## What a refusal looks like

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

  <!-- ci: ci.yml container — the step that declares the dataset with varve apply by workload identity federation, against mock-oauth2 -->
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

<!-- ci: ci.yml aot — eng/server-smoke.cs starts the binary in anonymous mode -->
```json
"Varve": { "Auth": { "Mode": "Anonymous" } }
```

Every caller holds every permission on every graph, the management plane's
included, commits name no agent,
and the server warns at every start. `Production: true` refuses it.

## The command line

`varve` against a URL carries a bearer token in one of three ways (ADR 0105):

1. **`--token`, or `VARVE_TOKEN`**: a token obtained elsewhere — a CI job's,
   a managed identity's. Nothing is stored.
2. **Client credentials**, for automation.
   - **Keyless:** on `varve apply` and `varve manage`, `--authority <issuer>
     --client-id <id> --client-assertion github|<file>`. The platform's own
     token is the client's assertion (workload identity federation; the
     management plane, above). `varve manage` also takes `--managed-identity`
     on Container Apps. The data-plane commands don't take these yet (#301):
     pass a token the platform issued as `--token` instead.
   - **With a secret:** `--client-secret <secret>`, or `VARVE_CLIENT_SECRET`,
     on every command, with `--scope` where the issuer needs one.
   - No refresh token is issued, so nothing is stored. Each invocation
     authenticates again, which is cheap.
3. **The device code flow**, for a person: `--authority <issuer> --client-id
   <id>`. The command prints a URL and a code; approve it in a browser, and
   the command continues. The issuer's refresh token is kept, and the next
   invocation uses it first.

### The credential file

The refresh token — keyed by the dataset URL, the issuer and the client id it
was obtained for, and nothing else, never an access token — is kept in one
file, one refresh token per dataset URL:

| Platform | Path |
|---|---|
| Linux, macOS | `$XDG_CONFIG_HOME/varve/credentials.json`, else `~/.config/varve/credentials.json`, created mode `0600`; a file readable by group or others is refused |
| Windows | `%LOCALAPPDATA%\varve\credentials.json`, the whole file protected with DPAPI for the current user |

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
