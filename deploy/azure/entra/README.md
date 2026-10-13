# Entra registrations for on-behalf-of

`main.bicep` creates the two registrations of the on-behalf-of chain
(ADR 0127; [Authenticate](../../../docs/operator/authenticate.md), "An
application acting for its users"):

- **Varve's API.** Application ID URI `api://varve`, v2 tokens, the app roles
  `Varve.Read`, `Varve.Write` and `Varve.Admin`, and the delegated scope
  `access_as_user`.
- **A calling application**, granted that scope with admin consent for the
  tenant, and `Varve.Write` as an application permission for the writes it
  makes on its own account. It is a website, an MCP server, or any API that
  holds its users' tokens.

It runs at tenant scope with the Microsoft Graph Bicep extension
(`bicepconfig.json`, `microsoftgraph/v1.0:1.0.0` from `mcr.microsoft.com`).
The deploying identity needs `Application.ReadWrite.All` and
`DelegatedPermissionGrant.ReadWrite.All`:

```sh
az deployment tenant create --location westeurope --template-file deploy/azure/entra/main.bicep \
  --parameters environmentName=prod
```

No secret is created. Give the calling application a federated credential
(a managed identity or a workload identity) or a certificate yourself.
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
