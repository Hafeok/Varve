// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
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
/// The sample's exchange against Entra (ADR 0127), run by hand: the
/// <c>entra-obo</c> workflow, dispatched by the maintainer in the environment
/// of that name, which only <c>main</c> may use. No credential is stored:
/// <list type="bullet">
/// <item>the calling application's credential is GitHub's OIDC token for that
/// environment, written to <c>VARVE_ENTRA_OBO_ASSERTION_FILE</c>, which its
/// registration trusts as a federated credential;</item>
/// <item>the user's token for the calling application comes from an
/// interactive device-code sign-in in the workflow, written to
/// <c>VARVE_ENTRA_OBO_USER_TOKEN_FILE</c>.</item>
/// </list>
/// The sample applies its setup with the application's own token, then places
/// an order and reads it back with the user's; the order's commit names the
/// user's <c>oid</c>, and the setup's names another agent, the application.
/// Without the files the test is skipped, unless <c>VARVE_AUTH_LEGS_REQUIRED</c>
/// names <c>entra-obo</c>.
/// </summary>
public sealed class EntraTests
{
    [Fact]
    public async Task The_sample_places_an_order_in_varve_as_the_signed_in_user()
    {
        string tenant = Leg("VARVE_ENTRA_TENANT");
        string varveAudience = Leg("VARVE_ENTRA_AUDIENCE");
        string callerId = Leg("VARVE_ENTRA_OBO_CALLER_ID");
        string callerAudience = Leg("VARVE_ENTRA_OBO_CALLER_AUDIENCE");
        string assertion = Leg("VARVE_ENTRA_OBO_ASSERTION_FILE");
        string userToken = (await File.ReadAllTextAsync(Leg("VARVE_ENTRA_OBO_USER_TOKEN_FILE"), TestContext.Current.CancellationToken)).Trim();
        string authority = "https://login.microsoftonline.com/" + tenant + "/v2.0";
        string oid = Claim(userToken, "oid");

        int varvePort = FreePort();
        using Process varve = StartVarve(varvePort, authority, varveAudience);

        try
        {
            using HttpClient http = new();
            await WaitReadyAsync(http, varvePort);

            WebApplication sample = OrdersApi.Build([
                "--urls=http://127.0.0.1:0",
                "--AzureAd:Instance=https://login.microsoftonline.com/",
                "--AzureAd:TenantId=" + tenant,
                "--AzureAd:ClientId=" + callerId,
                "--AzureAd:Audience=" + callerAudience,
                "--AzureAd:ClientCredentials:0:SourceType=SignedAssertionFilePath",
                "--AzureAd:ClientCredentials:0:SignedAssertionFileDiskPath=" + assertion,
                "--Varve:Dataset=http://127.0.0.1:" + varvePort + "/datasets/orders/",
                "--Varve:Scope=" + varveAudience + "/access_as_user",
            ]);

            // Starting applies the setup, with the application's own token.
            await sample.StartAsync(TestContext.Current.CancellationToken);

            try
            {
                Uri address = new(((IApplicationBuilder)sample).ServerFeatures.Get<IServerAddressesFeature>()!.Addresses.First().TrimEnd('/') + "/");
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

                using HttpResponseMessage placed = await http.PostAsync(new Uri(address, "orders/1001"), null, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.NoContent, placed.StatusCode);

                string orders = await http.GetStringAsync(new Uri(address, "orders"), TestContext.Current.CancellationToken);
                Assert.Contains("https://shop.example/orders/1001", orders, StringComparison.Ordinal);

                // The attribution rule, in the history: the order is the user's,
                // the setup before it the application's.
                string commits = await http.GetStringAsync(new Uri(address, "varve/commits"), TestContext.Current.CancellationToken);
                string user = "agent <" + authority + "#" + oid + ">";
                Assert.Contains(user, commits, StringComparison.Ordinal);
                Assert.Contains(commits.Split('\n'), line => line.StartsWith("agent <" + authority + "#", StringComparison.Ordinal) && line != user);
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
        Assert.Skip(variable + " is not set: the on-behalf-of exchange runs against Entra in the entra-obo workflow only, dispatched by hand.");
        return string.Empty;
    }
}
