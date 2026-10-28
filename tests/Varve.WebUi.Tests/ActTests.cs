// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Varve.Protocol.Client;
using Varve.WebUi.Pages;
using Xunit;

namespace Varve.WebUi.Tests;

/// <summary>
/// The acts on a dataset (D4) through <c>ConfirmDialog</c>: close asks for the
/// name typed exactly and names the drift; a conflict or a stale precondition
/// is shown in the dialog with "Reload at head", which reads the view again.
/// The server on <c>main</c> answers neither for a close, so a fake one does,
/// and records the <c>If-Match</c> a close sends.
/// </summary>
public class ActTests
{
    private static readonly Uri Server = new("https://varve.example/");

    [Fact]
    public void Close_needs_the_name_typed_exactly_and_names_the_drift()
    {
        using BunitContext context = NewContext(new FakeServer());
        IRenderedComponent<Dataset> page = context.Render<Dataset>(parameters => parameters.Add(p => p.Name, "notes"));
        page.WaitForElement("button.vw-button--danger").Click();

        Assert.Contains("The declaration will drift from Git", page.Find("dialog").TextContent, StringComparison.Ordinal);
        AngleSharp.Dom.IElement confirm = page.Find("dialog button.vw-button--danger-solid");
        Assert.True(confirm.HasAttribute("disabled"));

        page.Find("dialog input").Input("Notes");
        Assert.True(page.Find("dialog button.vw-button--danger-solid").HasAttribute("disabled"));

        page.Find("dialog input").Input("notes");
        Assert.False(page.Find("dialog button.vw-button--danger-solid").HasAttribute("disabled"));
    }

    [Fact]
    public void Close_sends_the_version_of_the_declaration_it_read_as_if_match()
    {
        FakeServer server = new();
        using BunitContext context = NewContext(server);
        IRenderedComponent<Dataset> page = context.Render<Dataset>(parameters => parameters.Add(p => p.Name, "notes"));
        page.WaitForElement("button.vw-button--danger").Click();
        page.Find("dialog input").Input("notes");
        page.Find("dialog button.vw-button--danger-solid").Click();

        page.WaitForAssertion(() => Assert.Equal("\"412\"", server.IfMatch));
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed, "precondition-failed")]
    [InlineData(HttpStatusCode.Conflict, "conflict")]
    public void A_stale_close_shows_its_problem_in_the_dialog_with_reload_at_head(HttpStatusCode status, string type)
    {
        FakeServer server = new() { StateAnswer = (status, type) };
        using BunitContext context = NewContext(server);
        IRenderedComponent<Dataset> page = context.Render<Dataset>(parameters => parameters.Add(p => p.Name, "notes"));
        page.WaitForElement("button.vw-button--danger").Click();
        page.Find("dialog input").Input("notes");
        page.Find("dialog button.vw-button--danger-solid").Click();

        page.WaitForAssertion(() => Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), page.Find("dialog .vw-problem__status").TextContent, StringComparison.Ordinal));
        Assert.Contains("https://w3id.org/varve/problems/" + type, page.Find("dialog .vw-problem__type").TextContent, StringComparison.Ordinal);
        Assert.Equal("Close", page.Find("dialog .vw-dialog__foot button").TextContent);
        int reads = server.StatusReads;

        page.FindAll("dialog .vw-problem button").Single(button => button.TextContent == "Reload at head").Click();

        page.WaitForAssertion(() => Assert.True(server.StatusReads > reads));
    }

    [Fact]
    public void A_refusal_that_is_not_stale_shows_its_problem_with_no_reload()
    {
        FakeServer server = new() { StateAnswer = (HttpStatusCode.Forbidden, "forbidden") };
        using BunitContext context = NewContext(server);
        IRenderedComponent<Dataset> page = context.Render<Dataset>(parameters => parameters.Add(p => p.Name, "notes"));
        page.WaitForElement("button.vw-button--danger").Click();
        page.Find("dialog input").Input("notes");
        page.Find("dialog button.vw-button--danger-solid").Click();

        page.WaitForAssertion(() => Assert.Contains("403", page.Find("dialog .vw-problem__status").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(page.FindAll("dialog button"), button => button.TextContent == "Reload at head");
    }

    private static BunitContext NewContext(FakeServer server)
    {
        BunitContext context = new();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        HttpClient http = new(server);
        context.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        context.Services.AddSingleton(new WebUiSettings(Server, Anonymous: true));
        context.Services.AddSingleton(new VarveManagementClient(http, Server, ClientLimits.Default, null, null));
        context.Services.AddSingleton(new VarveHealthClient(http, Server, ClientLimits.Default));
        context.Services.AddSingleton(new DatasetClients(http, Server, null, TimeProvider.System));
        return context;
    }

    // Enough of a server for the Dataset page: one open dataset, written by
    // this instance, with no commits; and a state route that answers as told.
    private sealed class FakeServer : HttpMessageHandler
    {
        private int _statusReads;

        internal (HttpStatusCode Status, string Type)? StateAnswer { get; init; }

        internal int StatusReads => Volatile.Read(ref _statusReads);

        internal string? IfMatch { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Put && path == "/manage/datasets/notes/state")
            {
                IfMatch = request.Headers.IfMatch.ToString();
                return Task.FromResult(StateAnswer is { } answer
                    ? Json(answer.Status, "{\"type\":\"https://w3id.org/varve/problems/" + answer.Type + "\",\"title\":\"Refused.\",\"status\":" + (int)answer.Status + ",\"instance\":\"vm:1\"}", "application/problem+json")
                    : new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            return Task.FromResult(path switch
            {
                "/manage/datasets/notes/state" => Json(HttpStatusCode.OK, "{\"state\":\"open\",\"role\":\"writer\"}"),
                "/manage/datasets/notes/status" => Count(Json(HttpStatusCode.OK, "{\"name\":\"notes\",\"head\":0,\"durability\":\"None\",\"failed\":false,\"state\":\"open\",\"projection\":{\"position\":0,\"lag\":0,\"failed\":false},\"settings\":{},\"checkpoints\":[]}")),
                "/manage/datasets/notes" => Tagged(Json(HttpStatusCode.OK, "@prefix varve: <https://w3id.org/varve/ns#> .", "application/trig"), "\"412\""),
                "/manage/datasets" => Json(HttpStatusCode.OK, "{\"datasets\":[{\"name\":\"notes\",\"state\":\"open\",\"storage\":\"Memory\",\"role\":\"writer\",\"head\":0}]}"),
                "/manage/sparql" => Json(HttpStatusCode.OK, "{\"head\":{\"vars\":[\"name\",\"instance\"]},\"results\":{\"bindings\":[]}}", "application/sparql-results+json"),
                "/health/ready" => Json(HttpStatusCode.OK, "{\"status\":\"ready\",\"datasets\":{}}"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        }

        private HttpResponseMessage Count(HttpResponseMessage response)
        {
            Interlocked.Increment(ref _statusReads);
            return response;
        }

        private static HttpResponseMessage Tagged(HttpResponseMessage response, string tag)
        {
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(tag);
            return response;
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body, string type = "application/json") =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, type) };
    }
}
