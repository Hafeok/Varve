// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Varve.Protocol.Client;
using Varve.WebUi.App;
using Xunit;

namespace Varve.WebUi.Tests;

/// <summary>
/// The shell's own facts (review F01, F22): the instance that answered and
/// its role in the system dataset, from <c>/manage/system</c>, and the
/// datasets the navigation's selector offers.
/// </summary>
public class ShellFactsTests
{
    private static readonly Uri Server = new("https://varve.example/");

    [Theory]
    [InlineData("""{"role":"writer","epoch":7,"position":412,"tail":412,"lag":0,"bootstrap":"recorded"}""", "system writer")]
    [InlineData("""{"role":"follower","epoch":7,"position":410,"tail":412,"lag":2,"bootstrap":"recorded","writer":"https://varve-1.example/"}""", "system follower")]
    [InlineData("""{"instance":"varve-1","role":"writer","epoch":7,"position":412,"tail":412,"lag":0,"bootstrap":"recorded"}""", "varve-1 · system writer")]
    public async Task The_instance_is_its_role_and_its_name_once_the_server_serves_it(string system, string shown)
    {
        ShellFacts facts = new(Client(system, """{"datasets":[{"name":"notes"},{"name":"catalog"}]}"""));

        await facts.ReadAsync(CancellationToken.None);

        Assert.Equal(shown, facts.Instance);
        Assert.Equal(["catalog", "notes"], facts.Datasets);
    }

    [Fact]
    public async Task What_cannot_be_read_is_not_shown()
    {
        ShellFacts facts = new(new VarveManagementClient(new HttpClient(new Unreachable()), Server, ClientLimits.Default, null, null));
        int changed = 0;
        facts.Changed += () => changed++;

        await facts.ReadAsync(CancellationToken.None);

        Assert.Null(facts.Instance);
        Assert.Empty(facts.Datasets);
        Assert.Equal(1, changed);
    }

    // Review F22: the selector defaults to the dataset last viewed, and on a
    // dataset's screen, choosing another opens the same screen of it.
    [Fact]
    public void The_selector_defaults_to_the_dataset_last_viewed()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new WebUiSettings(Server, Anonymous: true));
        context.Services.AddSingleton(new ShellFacts(Client("""{"role":"writer"}""", """{"datasets":[{"name":"notes"},{"name":"catalog"}]}""")));
        NavigationManager navigation = context.Render<Probe>().Instance.Navigation;
        IRenderedComponent<MainLayout> layout = context.Render<MainLayout>(parameters => parameters.Add(p => p.Body, (RenderFragment)(_ => { })));

        layout.WaitForAssertion(() => Assert.Equal("system writer", layout.Find(".vw-shell__instance").TextContent));
        Assert.Equal("catalog", layout.Find("#nav-dataset option[selected]").TextContent);

        navigation.NavigateTo("datasets/notes/grants");
        navigation.NavigateTo("health");

        layout.WaitForAssertion(() => Assert.Equal("notes", layout.Find("#nav-dataset option[selected]").TextContent));
        Assert.Equal("datasets/notes/commits", layout.Find("nav a[href$='/commits']").GetAttribute("href"));

        navigation.NavigateTo("datasets/notes/grants");
        layout.Find("#nav-dataset").Change("catalog");

        Assert.EndsWith("/datasets/catalog/grants", navigation.Uri, StringComparison.Ordinal);
    }

    private static VarveManagementClient Client(string system, string datasets) =>
        new(new HttpClient(new Answers(system, datasets)), Server, ClientLimits.Default, null, null);

    private sealed class Answers(string system, string datasets) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/manage/system" => Json(system),
                "/manage/datasets" => Json(datasets),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    // Hands the test the navigation the layout follows.
    private sealed class Probe : ComponentBase
    {
        [Inject]
        public NavigationManager Navigation { get; set; } = null!;
    }

    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused.");
    }
}
