# Varve.Aspire

The .NET Aspire hosting integration for Varve (ADR 0117): the server's
container in an AppHost, with its authentication and its datasets declared
where the rest of the application is.

```csharp
using Varve.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

var varve = builder.AddVarve("varve")
    .WithOidc(ReferenceExpression.Create($"https://login.example/"), "api://varve", roleClaimType: "roles")
    .WithOperators("varve.operator")
    .WithManagementClient("varve-deployer", deployerSecret, scope: "api://varve-manage/.default")
    .WithDataset("people", read: ["varve.read"], write: ["varve.write"]);

builder.Build().Run();
```

- **`AddVarve(name, tag?)`** adds `ghcr.io/hafeok/varve` at this build's
  version (or `tag`), the datasets volume at `/var/lib/varve`, the `http`
  endpoint on 8080, OTLP to the AppHost's dashboard, and `GET /health/ready`
  as the health check. Without `WithOidc` the server runs **anonymous in run
  mode and refuses to publish**: the manifest step fails naming `WithOidc`.
- **`WithOidc(authority, audience, roleClaimType?, subjectClaim?,
  requireHttpsMetadata, managementAudience)`** sets `Varve:Auth`, with the
  management plane's audience, `api://varve-manage` by default (ADR 0163). The authority is a
  `ReferenceExpression`, so it may be another resource's endpoint:
  `ReferenceExpression.Create($"{issuer.GetEndpoint("http")}/realm")`.
- **`WithAzureBlobStorage(containerUrl, cacheBytes?)`** puts the datasets in
  an Azure Blob Storage container instead of files on the volume (ADR 0142),
  each under its own prefix, and names the endpoint as the address writes are
  forwarded to (ADR 0168); the server reaches it as its managed identity,
  with no key or connection string. The volume holds the page cache. For a
  deployed container: an emulator in a neighbouring container is not on
  loopback, so its well-known key is refused (ADR 0137).
- **`WithOperators(principals)`** names the bootstrap operators, agent IRIs
  or role values (ADR 0163 point 7); a server under `WithOidc` needs one.
- **`WithDataset(name, read?, write?, admin?, setup?)`** declares a dataset
  in the management plane, and the roles that may read, write and
  administer it. The declarations are one TriG file, applied with
  `varve apply` by a container of the server's image, `{server}-declare`,
  once the server is healthy; under `WithOidc` it authenticates as
  **`WithManagementClient(clientId, secret, scope?)`** says. A setup file
  is applied after it, by `varve setup apply` (ADR 0131).

The package references `Aspire.Hosting` and nothing from Varve; the sample
AppHost under `samples/` runs the server beside a test issuer and is not
shipped. `docs/operator/` is the operator's guide to what the container
does.
