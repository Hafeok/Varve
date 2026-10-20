// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace Varve.Aspire.Sample.Tests;

/// <summary>
/// The sample AppHost end to end (ADR 0117): the server comes up healthy
/// beside the issuer, a writer's token writes, a reader's reads and may not
/// write. The issuer names itself by the host it was asked for, so the token
/// request carries the host the server sees it by on the container network:
/// the authority Aspire gave the server, read from its environment, which is
/// <c>mock-oauth2.dev.internal</c> under Aspire 13 rather than the resource's
/// bare name.
/// </summary>
public class SampleTests
{
    [Fact]
    public async Task The_server_comes_up_behind_the_issuer_and_takes_its_tokens()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Varve_Aspire_Sample>(cancellationToken);
        await using DistributedApplication app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        ResourceEvent healthy = await app.ResourceNotifications.WaitForResourceHealthyAsync("varve", cancellationToken).WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
        string issuerHost = new Uri(healthy.Snapshot.EnvironmentVariables.Single(variable => variable.Name == "VARVE__AUTH__AUTHORITY").Value!).Authority;

        Uri issuer = app.GetEndpoint("mock-oauth2", "http");
        using HttpClient provider = new(new HttpClientHandler { UseProxy = false });
        string writer = await TokenAsync(provider, issuer, issuerHost, "varve-writer", cancellationToken);
        string reader = await TokenAsync(provider, issuer, issuerHost, "varve-cli", cancellationToken);

        using HttpClient varve = app.CreateHttpClient("varve", "http");
        using HttpResponseMessage unauthenticated = await varve.GetAsync("datasets/people/sparql?query=ASK%7B%7D", cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using HttpResponseMessage written = await SendAsync(varve, HttpMethod.Post, "datasets/people/sparql", writer,
            new StringContent("INSERT DATA { <http://ex/s> <http://ex/p> \"from the sample\" }", Encoding.UTF8, "application/sparql-update"), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);

        using HttpResponseMessage asked = await SendAsync(varve, HttpMethod.Get, "datasets/people/sparql?query=" + Uri.EscapeDataString("ASK { ?s ?p \"from the sample\" }"), reader, null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.Contains("\"boolean\":true", await asked.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);

        // The dataset's setup ran once the server was healthy (ADRs 0130, 0131):
        // its migration is recorded, and its baseline is in its graph.
        // A container that ran to its end is Exited, not Finished: wait for any
        // terminal state, then require the command's own success.
        ResourceEvent setup = await app.ResourceNotifications.WaitForResourceAsync(
            "varve-setup-people", e => KnownResourceStates.TerminalStates.Contains(e.Snapshot.State?.Text), cancellationToken).WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
        Assert.Equal(0, setup.Snapshot.ExitCode);
        using HttpResponseMessage recorded = await SendAsync(varve, HttpMethod.Get, "datasets/people/sparql?query=" + Uri.EscapeDataString(
            "ASK { GRAPH <https://w3id.org/varve/ns#setup> { ?m <https://w3id.org/varve/ns#migrationId> \"2026-10-13-founder\" } GRAPH <https://people.example/vocabulary> { ?class ?p ?o } }"), reader, null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
        Assert.Contains("\"boolean\":true", await recorded.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);

        using HttpResponseMessage refused = await SendAsync(varve, HttpMethod.Post, "datasets/people/sparql", reader,
            new StringContent("INSERT DATA { <http://ex/s> <http://ex/p> \"not allowed\" }", Encoding.UTF8, "application/sparql-update"), cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    private static async Task<string> TokenAsync(HttpClient provider, Uri issuer, string issuerHost, string client, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(issuer, "varve/token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = client,
                ["client_secret"] = "not-checked-by-the-mock",
                ["scope"] = "api://varve",
            }),
        };
        request.Headers.Host = issuerHost;
        using HttpResponseMessage response = await provider.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.IsSuccessStatusCode, (int)response.StatusCode + " from the issuer: " + body);
        return (string)JsonNode.Parse(body)!["access_token"]!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient varve, HttpMethod method, string path, string token, HttpContent? content, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await varve.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// <c>WithAzureBlobStorage</c> (ADR 0142): the server's environment names the
/// container, the cache on the volume and the address writes are forwarded
/// to (ADR 0168); datasets are declarations, not configuration (ADR 0163);
/// and no credential is among the variables (ADR 0137).
/// </summary>
public class AzureBlobStorageTests
{
    [Fact]
    public async Task Datasets_go_to_the_container_with_no_credential_in_the_environment()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder();
        IResourceBuilder<VarveResource> varve = builder.AddVarve("varve")
            .WithOidc(ReferenceExpression.Create($"https://login.example/"), "api://varve")
            .WithAzureBlobStorage(ReferenceExpression.Create($"https://account.blob.core.windows.net/datasets"))
            .WithDataset("people");

        await using DistributedApplication app = builder.Build();
        DistributedApplicationExecutionContext publish = new(new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Publish) { Services = app.Services });
        IExecutionConfigurationResult configuration = await ExecutionConfigurationBuilder.Create(varve.Resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(publish, null, CancellationToken.None);
        Assert.Null(configuration.Exception);
        Dictionary<string, string> environment = configuration.EnvironmentVariables.ToDictionary(variable => variable.Key, variable => variable.Value);

        Assert.Equal("Azure", environment["VARVE__OBJECTSTORAGE__PROVIDER"]);
        Assert.Equal("https://account.blob.core.windows.net/datasets", environment["VARVE__OBJECTSTORAGE__CONTAINERURL"]);
        Assert.Equal("/var/lib/varve/cache", environment["VARVE__OBJECTSTORAGE__CACHEPATH"]);
        Assert.True(environment.ContainsKey("VARVE__FLEET__INTERNALURL"));
        Assert.DoesNotContain(environment.Keys, key => key.StartsWith("VARVE__DATASETS__", StringComparison.Ordinal) || key.StartsWith("VARVE__AUTH__DATASETS__", StringComparison.Ordinal));
        Assert.DoesNotContain(environment.Keys, key => key.Contains("KEY", StringComparison.OrdinalIgnoreCase) || key.Contains("CONNECTIONSTRING", StringComparison.OrdinalIgnoreCase) || key.Contains("SAS", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// <c>WithDataset</c> and <c>WithOperators</c> (ADR 0163): the bootstrap
/// operators are configuration, and the datasets a declarations container
/// applies with <c>varve apply</c> once the server is healthy.
/// </summary>
public class DeclarationTests
{
    [Fact]
    public async Task Datasets_are_declared_by_a_container_and_operators_configured()
    {
        IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder();
        IResourceBuilder<VarveResource> varve = builder.AddVarve("varve")
            .WithOidc(ReferenceExpression.Create($"https://login.example/"), "api://varve")
            .WithOperators("Varve.Operator")
            .WithDataset("people", read: ["People.Reader"]);

        await using DistributedApplication app = builder.Build();
        DistributedApplicationExecutionContext publish = new(new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Publish) { Services = app.Services });
        IExecutionConfigurationResult configuration = await ExecutionConfigurationBuilder.Create(varve.Resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(publish, null, CancellationToken.None);
        Dictionary<string, string> environment = configuration.EnvironmentVariables.ToDictionary(variable => variable.Key, variable => variable.Value);

        Assert.Equal("Varve.Operator", environment["VARVE__MANAGE__BOOTSTRAPOPERATORS__0"]);
        Assert.Equal("api://varve-manage", environment["VARVE__AUTH__MANAGEMENT__AUDIENCE"]);
        Assert.Contains(builder.Resources, resource => resource.Name == "varve-declare");
    }
}
