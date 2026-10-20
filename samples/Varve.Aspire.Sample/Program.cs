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
// reads. The people dataset is declared by varve-declare, as varve-deployer,
// an operator (ADR 0163); its setup, setup/people.yaml, is applied by
// varve-setup-people with the writer's identity once the server is healthy. Varve:ImageTag picks another image tag, as CI does for the one it
// just built.
//
// The issuer's image is pinned by digest, the one .github/ci-images.yaml
// pins (#118). Varve:IssuerImage names another repository holding that
// digest: CI sets this repository's private mirror,
// ghcr.io/hafeok/varve-ci/mock-oauth2-server, and anyone else runs the
// upstream image.
namespace Varve.Aspire.Sample;

internal static class Program
{
    private const string IssuerImage = "ghcr.io/navikt/mock-oauth2-server";
    private const string IssuerTag = "6.0.4";
    private const string IssuerSha256 = "47374fe063995d9b6d39ebb6ea21eb5f765025bf0aa08b61b08b439eb91adaf0";

    private static void Main(string[] args)
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

        string issuerImage = builder.Configuration["Varve:IssuerImage"] is { Length: > 0 } image ? image : IssuerImage;
        IResourceBuilder<ContainerResource> issuer = builder.AddContainer("mock-oauth2", issuerImage, IssuerTag)
            .WithImageSHA256(IssuerSha256)
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithEnvironment("JSON_CONFIG_PATH", "/config/mock-oauth2.json")
            .WithBindMount(Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "tests", "fixtures", "auth", "mock-oauth2.json")), "/config/mock-oauth2.json", isReadOnly: true)
            .WithHttpHealthCheck("/varve/.well-known/openid-configuration");

        // The setup's identity: a client of the same issuer with write on
        // people. The mock checks no secret; a real issuer's goes in user
        // secrets or the environment, as any Aspire parameter does.
        IResourceBuilder<ParameterResource> setupSecret = builder.AddParameter("varve-setup-secret", "not-checked-by-the-mock", secret: true);

        IResourceBuilder<VarveResource> varve = builder.AddVarve("varve")
            .WithOidc(ReferenceExpression.Create($"{issuer.GetEndpoint("http")}/varve"), "api://varve", roleClaimType: "roles", requireHttpsMetadata: false)
            .WithOperators("varve.operator")
            .WithManagementClient("varve-deployer", setupSecret, scope: "api://varve-manage")
            .WithSetupClient("varve-writer", setupSecret, scope: "api://varve")
            .WithDataset("people", read: ["varve.read"], write: ["varve.write"], setup: "setup/people.yaml")
            .WaitFor(issuer);

        if (builder.Configuration["Varve:ImageTag"] is { Length: > 0 } tag)
        {
            varve.WithImageTag(tag);
        }

        builder.Build().Run();
    }
}
