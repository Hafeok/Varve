// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Xunit;

namespace Varve.OnBehalfOf.Sample.Tests;

/// <summary>
/// The sample's exchange against Entra (ADR 0127): a user's token for the
/// calling application, the sample exchanging it on the user's behalf, an
/// update and a query in Varve as that user, and the commit naming the user's
/// <c>oid</c>. The secrets are the providers leg's (ADR 0100); without them
/// the test is skipped, unless <c>VARVE_AUTH_LEGS_REQUIRED</c> names
/// <c>entra-obo</c>.
/// </summary>
public sealed class EntraTests
{
    [Fact]
    public async Task The_sample_places_an_order_in_varve_as_the_signed_in_user()
    {
        string tenant = Leg("VARVE_ENTRA_TENANT");
        string varveAudience = Leg("VARVE_ENTRA_AUDIENCE");
        string callerId = Leg("VARVE_ENTRA_OBO_CALLER_ID");
        string callerSecret = Leg("VARVE_ENTRA_OBO_CALLER_SECRET");
        string callerAudience = Leg("VARVE_ENTRA_OBO_CALLER_AUDIENCE");
        string username = Leg("VARVE_ENTRA_OBO_USERNAME");
        string password = Leg("VARVE_ENTRA_OBO_PASSWORD");
        string authority = "https://login.microsoftonline.com/" + tenant + "/v2.0";

        int varvePort = FreePort();
        using Process varve = StartVarve(varvePort, authority, varveAudience);

        try
        {
            using HttpClient http = new();
            await WaitReadyAsync(http, varvePort);

            // A test user signing in to the calling application (resource owner
            // password: a test tenant's user without MFA, for CI alone).
            using HttpResponseMessage signedIn = await http.PostAsync(new Uri("https://login.microsoftonline.com/" + tenant + "/oauth2/v2.0/token"), new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = callerId,
                ["client_secret"] = callerSecret,
                ["scope"] = callerAudience + "/access_as_user",
                ["username"] = username,
                ["password"] = password,
            }), TestContext.Current.CancellationToken);
            Assert.True(signedIn.IsSuccessStatusCode, await signedIn.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            using JsonDocument tokens = JsonDocument.Parse(await signedIn.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            string userToken = tokens.RootElement.GetProperty("access_token").GetString()!;
            string oid = Claim(userToken, "oid");

            WebApplication sample = OrdersApi.Build([
                "--urls=http://127.0.0.1:0",
                "--AzureAd:Instance=https://login.microsoftonline.com/",
                "--AzureAd:TenantId=" + tenant,
                "--AzureAd:ClientId=" + callerId,
                "--AzureAd:Audience=" + callerAudience,
                "--AzureAd:ClientCredentials:0:SourceType=ClientSecret",
                "--AzureAd:ClientCredentials:0:ClientSecret=" + callerSecret,
                "--Varve:Dataset=http://127.0.0.1:" + varvePort + "/datasets/orders/",
                "--Varve:Scope=" + varveAudience + "/access_as_user",
            ]);
            await sample.StartAsync(TestContext.Current.CancellationToken);

            try
            {
                Uri address = new(((IApplicationBuilder)sample).ServerFeatures.Get<IServerAddressesFeature>()!.Addresses.First().TrimEnd('/') + "/");
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

                using HttpResponseMessage placed = await http.PostAsync(new Uri(address, "orders/1001"), null, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.NoContent, placed.StatusCode);

                string orders = await http.GetStringAsync(new Uri(address, "orders"), TestContext.Current.CancellationToken);
                Assert.Contains("https://shop.example/orders/1001", orders, StringComparison.Ordinal);

                string commit = await http.GetStringAsync(new Uri(address, "varve/last-commit"), TestContext.Current.CancellationToken);
                Assert.Contains("agent <" + authority + "#" + oid + ">", commit, StringComparison.Ordinal);
            }
            finally
            {
                await sample.StopAsync(TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            varve.Kill(entireProcessTree: true);
        }
    }

    private static Process StartVarve(int port, string authority, string audience)
    {
        ProcessStartInfo start = new("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in (string[])[
            Path.Combine(AppContext.BaseDirectory, "Varve.Server.dll"),
            "--urls=http://127.0.0.1:" + port,
            "--Varve:Auth:Mode=Oidc",
            "--Varve:Auth:Authority=" + authority,
            "--Varve:Auth:Audiences:0=" + audience,
            "--Varve:Auth:SubjectClaim=oid",
            "--Varve:Auth:Datasets:orders:Read:0=Varve.Read",
            "--Varve:Auth:Datasets:orders:Write:0=Varve.Write",
            "--Varve:Datasets:orders:Storage=Memory",
        ])
        {
            start.ArgumentList.Add(argument);
        }

        return Process.Start(start)!;
    }

    private static async Task WaitReadyAsync(HttpClient http, int port)
    {
        for (int attempt = 0; attempt < 120; attempt++)
        {
            try
            {
                using HttpResponseMessage ready = await http.GetAsync(new Uri("http://127.0.0.1:" + port + "/health/ready"), TestContext.Current.CancellationToken);

                if (ready.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        Assert.Fail("Varve did not become ready.");
    }

    private static string Claim(string token, string name)
    {
        string payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using JsonDocument claims = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return claims.RootElement.GetProperty(name).GetString()!;
    }

    private static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string Leg(string variable)
    {
        string? value = Environment.GetEnvironmentVariable(variable);

        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        string[] required = (Environment.GetEnvironmentVariable("VARVE_AUTH_LEGS_REQUIRED") ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.False(required.Contains("entra-obo", StringComparer.Ordinal), "The entra-obo leg is required here and " + variable + " is not set.");
        Assert.Skip(variable + " is not set: the on-behalf-of exchange runs against Entra on the providers leg only.");
        return string.Empty;
    }
}
