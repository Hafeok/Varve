// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net.Http;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Varve.Protocol.Client;
using Varve.WebUi;

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

await builder.Build().RunAsync().ConfigureAwait(false);
