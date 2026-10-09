# Varve.Aspire

The .NET Aspire hosting integration for Varve (ADR 0117): the server's
container in an AppHost, with its authentication and its datasets declared
where the rest of the application is.

```csharp
using Varve.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

var varve = builder.AddVarve("varve")
    .WithOidc(ReferenceExpression.Create($"https://login.example/"), "api://varve", roleClaimType: "roles")
    .WithDataset("people", read: ["varve.read"], write: ["varve.write"]);

builder.Build().Run();
```

- **`AddVarve(name, tag?)`** adds `ghcr.io/hafeok/varve` at this build's
  version (or `tag`), the datasets volume at `/var/lib/varve`, the `http`
  endpoint on 8080, OTLP to the AppHost's dashboard, and `GET /health/ready`
  as the health check. Without `WithOidc` the server runs **anonymous in run
  mode and refuses to publish**: the manifest step fails naming `WithOidc`.
- **`WithOidc(authority, audience, roleClaimType?, subjectClaim?,
  requireHttpsMetadata)`** sets `Varve:Auth`. The authority is a
  `ReferenceExpression`, so it may be another resource's endpoint:
  `ReferenceExpression.Create($"{issuer.GetEndpoint("http")}/realm")`.
- **`WithDataset(name, read?, write?, admin?)`** declares a dataset the
  server opens at start, creating it under its root when it is not there,
  and the roles that may read, write and administer it.

The package references `Aspire.Hosting` and nothing from Varve; the sample
AppHost under `samples/` runs the server beside a test issuer and is not
shipped. `docs/operator/` is the operator's guide to what the container
does.
