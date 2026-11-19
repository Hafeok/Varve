// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Varve.Protocol.Client;
using Varve.WebUi;
using Varve.WebUi.App;
using Varve.WebUi.Components.Pages.Services;
using Varve.WebUi.Pages.Services;
using Varve.WebUi.SignIn;

// The composition root (ADR 0060): the server's address and its mode, the
// clients of Varve.Protocol.Client the screens read through, and the clock.
WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
// The app's root takes its layout from here: a component never names one of
// its own level (ADR 0206 point 4), and this composition root is no level.
builder.RootComponents.Add(typeof(App), "#app", ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(App.Layout)] = typeof(MainLayout) }));
builder.RootComponents.Add<HeadOutlet>("head::after");

Uri server = ServerAddress.From(builder.Configuration["Varve:Server"], new Uri(builder.HostEnvironment.BaseAddress));
// Anonymous only when the settings say so; a server with OIDC and no client
// for the Web UI says NotConfigured, and so does a mode missing or unknown
// (ADR 0245): then nothing signs in, and the shell says what to add.
List<string> missing = [.. builder.Configuration.GetSection("Varve:Auth:Missing").GetChildren().Select(name => name.Value).OfType<string>()];
WebUiSettings settings = WebUiSettings.From(server, builder.Configuration["Varve:Auth:Mode"], missing);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new HttpClient());
builder.Services.AddSingleton(services => new VarveHealthClient(services.GetRequiredService<HttpClient>(), server, ClientLimits.Default));

// Sign-in (ADR 0201): the Web UI's own public client, one token per plane.
if (!settings.Anonymous && settings.SignInNotConfigured is null)
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
