// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Net.Http;
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
/// Sign-in not configured (ADR 0245): only <c>Anonymous</c> is anonymous;
/// a server with OIDC and no client for the Web UI says <c>NotConfigured</c>,
/// and the console then reads nothing, marks both planes, and names the
/// settings to add.
/// </summary>
public class SignInNotConfiguredTests
{
    private static readonly Uri Server = new("https://varve.example/");

    [Theory]
    [InlineData("Anonymous", true, false)]
    [InlineData("Oidc", false, false)]
    [InlineData("NotConfigured", false, true)]
    [InlineData(null, false, true)]
    [InlineData("anonymous", false, true)]
    [InlineData("Bearer", false, true)]
    public void Only_anonymous_is_anonymous_and_anything_but_the_two_modes_is_not_configured(string? mode, bool anonymous, bool notConfigured)
    {
        WebUiSettings settings = WebUiSettings.From(Server, mode, ["Varve:WebUi:Auth:ClientId"]);

        Assert.Equal(anonymous, settings.Anonymous);
        Assert.Equal(notConfigured, settings.SignInNotConfigured is not null);
    }

    [Fact]
    public void The_shell_names_the_settings_to_add_and_reads_nothing()
    {
        using BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        Counting handler = new();
        context.Services.AddSingleton(WebUiSettings.From(Server, "NotConfigured", ["Varve:WebUi:Auth:ClientId", "Varve:WebUi:Auth:DataScope", "Varve:WebUi:Auth:ManageScope"]));
        context.Services.AddSingleton(new ShellFacts(new VarveManagementClient(new HttpClient(handler), Server, ClientLimits.Default, null, null)));

        IRenderedComponent<MainLayout> layout = context.Render<MainLayout>(parameters => parameters.Add(p => p.Body, (RenderFragment)(builder => builder.AddContent(0, "a screen"))));

        Assert.Equal("Sign-in is not configured", layout.Find("h1").TextContent);
        Assert.DoesNotContain("a screen", layout.Markup, StringComparison.Ordinal);
        Assert.Equal(
            "<div class=\"vw-notice vw-notice--warn\"><p class=\"vw-notice__title\">This server has no sign-in for the Web UI</p><p class=\"vw-notice__body\">The server checks tokens from an issuer, and the Web UI has no client to ask it for one. Add <code>Varve:WebUi:Auth:ClientId</code>, <code>Varve:WebUi:Auth:DataScope</code> and <code>Varve:WebUi:Auth:ManageScope</code> to the server's configuration, and restart it.</p></div>",
            layout.Find(".vw-notice").OuterHtml);
        Assert.Equal(["Data plane not configured", "Management not configured"], [.. layout.FindAll(".vw-plane").Select(plane => plane.TextContent)]);
        Assert.All(layout.FindAll(".vw-plane"), plane => Assert.Contains("vw-plane--absent", plane.ClassName, StringComparison.Ordinal));
        Assert.DoesNotContain("anonymous", layout.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public void A_mode_the_web_ui_does_not_know_is_named_as_such()
    {
        using BunitContext context = new();
        IRenderedComponent<Pages.SignInNotConfigured> page = context.Render<Pages.SignInNotConfigured>(parameters => parameters.Add(p => p.Missing, []));

        Assert.Equal(
            "The Web UI's settings name no sign-in mode it knows: Varve:Auth:Mode is Oidc or Anonymous.",
            page.Find(".vw-notice__body").TextContent);
    }

    private sealed class Counting : HttpMessageHandler
    {
        internal int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            throw new HttpRequestException("No request is expected.");
        }
    }
}
