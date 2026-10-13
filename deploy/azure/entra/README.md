# Entra registrations for on-behalf-of

`main.bicep` creates the registrations of the on-behalf-of chain
(ADR 0127; [Authenticate](../../../docs/operator/authenticate.md), "An
application acting for its users"):

- **Varve's API.** Application ID URI `api://varve`, v2 tokens, the app roles
  `Varve.Read`, `Varve.Write` and `Varve.Admin`, and the delegated scope
  `access_as_user`.
- **A calling application**, granted that scope with admin consent for the
  tenant, and `Varve.Write` as an application permission for the writes it
  makes on its own account. It is a website, an MCP server, or any API that
  holds its users' tokens.
- **A sign-in client**, public and with no credential, through which a
  person signs in by device code for a token to the calling application.

It runs at tenant scope with the Microsoft Graph Bicep extension
(`bicepconfig.json`, `microsoftgraph/v1.0:1.0.0` from `mcr.microsoft.com`).
The deploying identity needs `Application.ReadWrite.All` and
`DelegatedPermissionGrant.ReadWrite.All`:

```sh
az deployment tenant create --location westeurope --template-file deploy/azure/entra/main.bicep \
  --parameters environmentName=prod
```

No secret is created, and none is needed:
- **The calling application** is given one federated credential: GitHub
  Actions' OIDC token for the `entra-obo` environment of this repository
  (`githubRepository`, `githubEnvironment`). Restrict that environment to
  `main`. A deployment of your own adds its own federated credential (a
  managed identity, a workload identity) or a certificate.
- **The sign-in client** is a public client with no credential. A person
  signs in through it by device code for the token the on-behalf-of leg
  starts from.

The `entra-obo` workflow (`workflow_dispatch` only) runs that leg. Its
environment holds identifiers only, as variables:
- `VARVE_ENTRA_TENANT`;
- `VARVE_ENTRA_AUDIENCE` (`api://varve`);
- `VARVE_ENTRA_OBO_CALLER_ID`;
- `VARVE_ENTRA_OBO_CALLER_AUDIENCE` (`api://varve-caller`);
- `VARVE_ENTRA_OBO_SIGN_IN_ID`, the `signInClientId` output.
Assign the app roles to users and groups under *Enterprise applications*.
Configure the server with the outputs:

```json
"Auth": {
  "Mode": "Oidc",
  "Authority": "https://login.microsoftonline.com/{tenant}/v2.0",
  "Audiences": [ "api://varve" ],
  "SubjectClaim": "oid",
  "Datasets": { "orders": { "Read": [ "Varve.Read" ], "Write": [ "Varve.Write" ] } }
}
```

CI builds the template on every pull request (`bicep` in `ci.yml`). The
deployment itself is the operator's.
