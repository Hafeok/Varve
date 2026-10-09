// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.IO;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

// A Varve server with a test issuer beside it (ADR 0117): mock-oauth2-server,
// the issuer of the auth tests' layer (b) (ADR 0100), configured by the same
// file, and the server's WithOidc pointing at it over the container network.
// A token from the mock for client varve-writer writes; one for varve-cli
// reads. Varve:ImageTag picks another image tag, as CI does for the one it
// just built.
namespace Varve.Aspire.Sample;

internal static class Program
{
    private static void Main(string[] args)
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

        IResourceBuilder<ContainerResource> issuer = builder.AddContainer("mock-oauth2", "ghcr.io/navikt/mock-oauth2-server", "6.0.4")
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithEnvironment("JSON_CONFIG_PATH", "/config/mock-oauth2.json")
            .WithBindMount(Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "tests", "fixtures", "auth", "mock-oauth2.json")), "/config/mock-oauth2.json", isReadOnly: true)
            .WithHttpHealthCheck("/varve/.well-known/openid-configuration");

        IResourceBuilder<VarveResource> varve = builder.AddVarve("varve")
            .WithOidc(ReferenceExpression.Create($"{issuer.GetEndpoint("http")}/varve"), "api://varve", roleClaimType: "roles", requireHttpsMetadata: false)
            .WithDataset("people", read: ["varve.read"], write: ["varve.write"])
            .WaitFor(issuer);

        if (builder.Configuration["Varve:ImageTag"] is { Length: > 0 } tag)
        {
            varve.WithImageTag(tag);
        }

        builder.Build().Run();
    }
}
