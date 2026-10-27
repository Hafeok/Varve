// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Azure;
using Varve.Store.Azure.Model;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The three Azure token sources, the cache, and the rule that a token never
/// leaves the adapter (ADR 0137).
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class AzureCredentialTests
{
    private const string Secret = "eyJTOKEN-THAT-MUST-NEVER-APPEAR";

    /// <summary>Answers each request with what the test says, and records what it was sent.</summary>
    private sealed class Answers(Func<HttpRequestMessage, string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Sent.Add((request, body));
            return answer(request, body);
        }
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task the_managed_identity_endpoint_is_asked_with_its_header_and_its_token_is_cached_until_five_minutes_before_expiry()
    {
        ManualClock clock = ManualClock.Epoch();
        long expiry = clock.Now.AddHours(1).ToUnixTimeSeconds();
        Answers handler = new((request, _) => Json("{\"access_token\":\"" + Secret + "\",\"expires_on\":\"" + expiry + "\"}"));
        using HttpClient http = new(handler);
        Dictionary<string, string> environment = new() { ["IDENTITY_ENDPOINT"] = "http://localhost:42356/msi/token", ["IDENTITY_HEADER"] = "header-value" };
        ITokenSource source = TokenSources.FromEnvironment(name => environment.GetValueOrDefault(name), http, clock);
        Assert.IsType<TokenSources.ManagedIdentity>(source);
        using CachedToken cached = new(source, clock);

        AccessToken first = await cached.GetAsync(T.Ct);
        Assert.Equal(Secret, first.Value);
        Assert.Equal("header-value", Assert.Single(handler.Sent[0].Request.Headers.GetValues("X-IDENTITY-HEADER")));
        Assert.Contains("resource=https%3A%2F%2Fstorage.azure.com%2F", handler.Sent[0].Request.RequestUri!.Query, StringComparison.Ordinal);

        clock.Now = clock.Now.AddMinutes(54);
        await cached.GetAsync(T.Ct);
        Assert.Single(handler.Sent);

        clock.Now = clock.Now.AddMinutes(2);
        await cached.GetAsync(T.Ct);
        Assert.Equal(2, handler.Sent.Count);
    }

    [Fact]
    public async Task workload_identity_sends_the_token_file_as_a_client_assertion_and_reads_it_afresh_each_time()
    {
        ManualClock clock = ManualClock.Epoch();
        string file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "assertion-one\n", T.Ct);
        Answers handler = new((_, _) => Json("{\"access_token\":\"" + Secret + "\",\"expires_in\":3600}"));
        using HttpClient http = new(handler);
        Dictionary<string, string> environment = new()
        {
            ["AZURE_FEDERATED_TOKEN_FILE"] = file,
            ["AZURE_TENANT_ID"] = "tenant",
            ["AZURE_CLIENT_ID"] = "client",
        };

        ITokenSource source = TokenSources.FromEnvironment(name => environment.GetValueOrDefault(name), http, clock);
        Assert.IsType<TokenSources.WorkloadIdentity>(source);
        AccessToken token = await source.FetchAsync(T.Ct);
        Assert.Equal(clock.Now.AddHours(1), token.ExpiresOn);
        Assert.Equal("https://login.microsoftonline.com/tenant/oauth2/v2.0/token", handler.Sent[0].Request.RequestUri!.ToString());
        Assert.Contains("client_assertion=assertion-one", handler.Sent[0].Body, StringComparison.Ordinal);
        Assert.Contains("grant_type=client_credentials", handler.Sent[0].Body, StringComparison.Ordinal);

        await File.WriteAllTextAsync(file, "assertion-two", T.Ct);
        await source.FetchAsync(T.Ct);
        Assert.Contains("client_assertion=assertion-two", handler.Sent[1].Body, StringComparison.Ordinal);
        File.Delete(file);
    }

    [Fact]
    public async Task with_neither_configured_a_workstation_asks_az()
    {
        int runs = 0;
        using HttpClient http = new();
        ITokenSource source = TokenSources.FromEnvironment(_ => null, http, ManualClock.Epoch(), _ =>
        {
            runs++;
            return new ValueTask<string>("{\"accessToken\":\"" + Secret + "\",\"expires_on\":1800000000}");
        });

        Assert.IsType<TokenSources.AzureCli>(source);
        AccessToken token = await source.FetchAsync(T.Ct);
        Assert.Equal(Secret, token.Value);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1800000000), token.ExpiresOn);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task a_token_reaches_no_message_and_no_rendering_whatever_the_store_answers()
    {
        ManualClock clock = ManualClock.Epoch();
        long expiry = clock.Now.AddHours(1).ToUnixTimeSeconds();
        List<string> seen = [];
        Answers handler = new((request, _) =>
        {
            if (request.RequestUri!.Host == "identity")
            {
                return Json("{\"access_token\":\"" + Secret + "\",\"expires_on\":" + expiry + "}");
            }

            // The store: every refusal it could give.
            HttpStatusCode status = request.Method == HttpMethod.Put ? HttpStatusCode.Forbidden : request.Method == HttpMethod.Get ? HttpStatusCode.InternalServerError : HttpStatusCode.Unauthorized;
            HttpResponseMessage response = new(status) { Content = new StringContent("<Error><Code>AuthorizationFailure</Code></Error>") };
            response.Headers.Add("x-ms-error-code", "AuthorizationFailure");
            return response;
        });

        using HttpClient http = new(handler);
        Dictionary<string, string> environment = new() { ["IDENTITY_ENDPOINT"] = "http://identity/token", ["IDENTITY_HEADER"] = "h" };
        CachedToken cached = new(TokenSources.FromEnvironment(name => environment.GetValueOrDefault(name), http, clock), clock);
        using AzureBlobStore store = new(new AzureBlobStoreOptions { ContainerUrl = new Uri("https://account.blob.core.windows.net/c"), Clock = clock }, new BearerSigner(cached), http);

        foreach (Func<Task> call in new Func<Task>[]
        {
            async () => await store.CreateIfAbsentAsync(new ObjectName("a"), new byte[] { 1 }, T.Ct),
            async () => await store.ReplaceIfUnchangedAsync(new ObjectName("a"), new ObjectVersion("\"v\""), new byte[] { 1 }, T.Ct),
            async () => await store.ReadAsync(new ObjectName("a"), new ByteOffset(0), new ByteCount(10), T.Ct),
            async () => await store.ListAsync(new ObjectName("p/"), T.Ct),
            async () => await store.DeleteAsync(new ObjectName("a"), T.Ct),
        })
        {
            Exception error = await Assert.ThrowsAnyAsync<Exception>(call);

            for (Exception? e = error; e is not null; e = e.InnerException)
            {
                seen.Add(e.ToString());
            }
        }

        seen.Add((await cached.GetAsync(T.Ct)).ToString());
        seen.Add(store.ToString()!);

        // The token was sent where it belongs, and nowhere else.
        Assert.Contains(handler.Sent, s => s.Request.Headers.Authorization?.Parameter == Secret);
        Assert.All(seen, text => Assert.DoesNotContain(Secret, text, StringComparison.Ordinal));
        Assert.Contains(seen, text => text.Contains("Storage Blob Data Contributor", StringComparison.Ordinal));
    }
}
