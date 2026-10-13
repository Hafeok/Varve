// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Varve.Protocol.Client;
using Varve.Protocol.Client.Hosting;
using Varve.Protocol.Client.Model;

namespace Varve.OnBehalfOf.Sample;

/// <summary>
/// An application's own API that calls Varve as its signed-in user (ADR 0127).
/// </summary>
/// <remarks>
/// <para>
/// <c>AzureAd</c> configures the application's registration (its tenant, its
/// client id, its credential, its own audience); <c>Varve:Dataset</c> is the
/// dataset's address and <c>Varve:Scope</c> the scope the exchange asks for,
/// <c>api://varve/access_as_user</c>. The registrations are
/// <c>deploy/azure/entra/</c>'s.
/// </para>
/// <para>
/// One <see cref="SparqlHttpClient"/> serves every user: its token provider is
/// called for each request and answers the token MSAL holds for the user of
/// the request in flight, exchanged on first use and cached per user. Every
/// commit it makes names the user as its agent; the application is the span's
/// <c>varve.azp</c>. A write the application made on its own account would
/// take a client-credentials token instead, and name the application.
/// </para>
/// </remarks>
public static class OrdersApi
{
    /// <summary>The application, built and not yet running.</summary>
    public static WebApplication Build(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        ConfigurationManager configuration = builder.Configuration;

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"))
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();
        builder.Services.AddAuthorization();
        builder.Services.AddHttpContextAccessor();

        // The client, and the dataset's setup applied as the host starts
        // (ADRs 0130, 0131). At start there is no user, so the setup's commits
        // take the application's own token and name the application; every
        // request afterwards takes its user's.
        string scope = configuration["Varve:Scope"] ?? "api://varve/access_as_user";
        Uri dataset = new(configuration["Varve:Dataset"] ?? throw new InvalidOperationException("Varve:Dataset is the dataset's address."));
        builder.Services.AddVarve(options =>
        {
            options.Server = new Uri(dataset, "../../");
            options.Setup = DatasetSetupFile.Read(Path.Combine(AppContext.BaseDirectory, "varve", "orders.yaml"));
            options.Tokens = services => UserOrApplication(services, scope);
            options.Clock = TimeProvider.System;
        });

        WebApplication app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        // The user places an order: one commit, whose agent is the user.
        app.MapPost("/orders/{id}", async (string id, SparqlHttpClient varve, CancellationToken cancellationToken) =>
        {
            string update = "INSERT DATA { <https://shop.example/orders/" + Uri.EscapeDataString(id) + "> <https://shop.example/ns#status> \"placed\" }";
            using HttpResponseMessage response = await varve.UpdateAsync(update, null, cancellationToken).ConfigureAwait(false);
            return Results.StatusCode((int)response.StatusCode);
        }).RequireAuthorization();

        // The orders the user may read, as SPARQL results JSON.
        app.MapGet("/orders", async (SparqlHttpClient varve, CancellationToken cancellationToken) =>
        {
            using HttpResponseMessage response = await varve.QueryAsync(
                "SELECT ?order WHERE { ?order <https://shop.example/ns#status> \"placed\" }",
                "application/sparql-results+json",
                null,
                cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Results.Content(body, "application/sparql-results+json", null, (int)response.StatusCode);
        }).RequireAuthorization();

        // The dataset's last commit, as the delta format writes it: whose it is
        // shows on its agent line.
        app.MapGet("/varve/last-commit", async (SparqlHttpClient varve, CancellationToken cancellationToken) =>
        {
            using HttpResponseMessage head = await varve.QueryAsync("ASK {}", "application/sparql-results+json", null, cancellationToken).ConfigureAwait(false);
            using HttpResponseMessage commit = await varve.CommitAsync(SparqlHttpClient.PositionOf(head) ?? 0, cancellationToken).ConfigureAwait(false);
            return Results.Text(await commit.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), "text/plain", null, (int)commit.StatusCode);
        }).RequireAuthorization();

        return app;
    }

    // The token provider (ADR 0127): the token MSAL holds for the user of the
    // request in flight, exchanged on behalf of that user on first use; with
    // no request in flight, the application's own, by client credentials.
    // Which one a write takes is the attribution rule: the user's write names
    // the user, the application's own names the application.
    private static BearerTokenProvider UserOrApplication(IServiceProvider services, string scope) => async cancellationToken =>
    {
        ITokenAcquisition acquisition = services.GetRequiredService<ITokenAcquisition>();

        if (services.GetRequiredService<IHttpContextAccessor>().HttpContext is { } context)
        {
            AuthenticationResult user = await acquisition.GetAuthenticationResultForUserAsync([scope], user: context.User).ConfigureAwait(false);
            return new BearerToken(user.AccessToken, user.ExpiresOn);
        }

        AuthenticationResult application = await acquisition.GetAuthenticationResultForAppAsync(scope[..scope.LastIndexOf('/')] + "/.default").ConfigureAwait(false);
        return new BearerToken(application.AccessToken, application.ExpiresOn);
    };
}
