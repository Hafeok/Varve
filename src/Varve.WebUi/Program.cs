// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Varve.Protocol.Client;
using Varve.WebUi;
using Varve.WebUi.SignIn;

// The composition root (ADR 0060): the server's address and its mode, the
// clients of Varve.Protocol.Client the screens read through, and the clock.
WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

Uri server = ServerAddress.From(builder.Configuration["Varve:Server"], new Uri(builder.HostEnvironment.BaseAddress));
bool anonymous = !string.Equals(builder.Configuration["Varve:Auth:Mode"], "Oidc", StringComparison.Ordinal);
builder.Services.AddSingleton(new WebUiSettings(server, anonymous));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new HttpClient());
builder.Services.AddSingleton(services => new VarveHealthClient(services.GetRequiredService<HttpClient>(), server, ClientLimits.Default));

// Sign-in (ADR 0201): the Web UI's own public client, one token per plane.
if (!anonymous)
{
    OidcOptions oidc = OidcOptions.From(
        builder.Configuration["Varve:Auth:Authority"],
        builder.Configuration["Varve:Auth:ClientId"],
        builder.Configuration["Varve:Auth:DataScope"],
        builder.Configuration["Varve:Auth:ManageScope"],
        builder.Configuration["Varve:Auth:ManageGrant"],
        builder.Configuration["Varve:Auth:ManageAudience"],
        builder.Configuration["Varve:Auth:DataAudience"],
        builder.Configuration["Varve:Auth:RoleClaimType"]);
    Uri application = new(builder.HostEnvironment.BaseAddress);
    builder.Services.AddSingleton(services => new SignInSession(
        oidc,
        services.GetRequiredService<HttpClient>(),
        new SessionSignInStore(services.GetRequiredService<IJSRuntime>()),
        new NavigationBrowser(services.GetRequiredService<NavigationManager>()),
        services.GetRequiredService<TimeProvider>(),
        OidcRandom.Default,
        application));
}

// The data plane, one client per dataset, with its token when signed in.
builder.Services.AddSingleton(services => new DatasetClients(
    services.GetRequiredService<HttpClient>(),
    server,
    services.GetService<SignInSession>()?.Data,
    services.GetRequiredService<TimeProvider>()));

// The management plane's reads, with its own token when signed in (ADR 0203).
builder.Services.AddSingleton(services => new VarveManagementClient(
    services.GetRequiredService<HttpClient>(),
    server,
    ClientLimits.Default,
    services.GetService<SignInSession>()?.Management,
    services.GetRequiredService<TimeProvider>()));

// What the shell shows that is not the screen's: the instance, the datasets.
builder.Services.AddSingleton(services => new ShellFacts(services.GetRequiredService<VarveManagementClient>()));

await builder.Build().RunAsync().ConfigureAwait(false);
