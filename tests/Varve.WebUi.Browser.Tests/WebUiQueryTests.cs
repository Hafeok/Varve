// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace Varve.WebUi.Browser.Tests;

/// <summary>
/// The Query screen against the real server (#355; ADRs 0254 and 0250): the
/// editor colours and refuses what the parser refuses before sending; a
/// SELECT, an ASK and a CONSTRUCT each answer with the position they were
/// read at; a long answer is read to the ceiling, closed, and shown 500 at a
/// time; a download reads at the view's position and holds every row; the
/// tab's history keeps what ran. With no CSP violation and no page error.
/// </summary>
public sealed class WebUiQueryTests
{
    private const string Title = "<http://purl.org/dc/terms/title>";
    private const string Order = "<https://notes.example/order>";

    [Fact]
    public async Task The_query_screen_reads_select_ask_and_construct_and_stops_a_long_answer_at_the_ceiling()
    {
        (string bundle, string chrome) = WebUiServer.Required();
        await using WebUiServer server = await WebUiBrowserTests.AnonymousAsync(bundle);
        await SeedAsync(server, 6_000);
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = chrome });
        IBrowserContext context = await browser.NewContextAsync(new() { AcceptDownloads = true });
        IPage page = await context.NewPageAsync();
        List<string> violations = WebUiBrowserTests.Watch(page);
        List<string> findings = [];

        await page.GotoAsync(server.Ui + "datasets/notes/query");
        await page.WaitForSelectorAsync(".vw-editor__code .vw-tok--keyword", new() { Timeout = 60_000 });

        // The parser's error, before anything is sent, and Run refused.
        await page.FillAsync("#query-text", "SELECT * WHERE {");
        await page.WaitForSelectorAsync("#query-text-err");
        Assert.StartsWith("1:", await page.InnerTextAsync("#query-text-err"));
        Assert.True(await page.IsDisabledAsync("button.vw-button--primary"));
        Assert.Equal("The query has a syntax error.", await page.InnerTextAsync("button.vw-button--primary + .vw-muted"));

        // A SELECT: its one row, the regime that answered and the position read.
        await RunAsync(page, "SELECT ?title WHERE { <https://notes.example/42> " + Title + " ?title }");
        await page.WaitForSelectorAsync(".vw-results__status:has-text('All 1 row returned to you.')");
        Assert.Contains("Weekly sync", await page.InnerTextAsync(".vw-results tbody"));
        Assert.Matches("^1 row · answered under ", await page.InnerTextAsync(".vw-results caption"));
        Assert.Equal(1, await page.Locator(".vw-viewhead .vw-position:has-text('read at position')").CountAsync());
        findings.AddRange(await WebUiBrowserTests.AuditAsync(page, "query: a select"));

        // An ASK: a word, not a state.
        await RunAsync(page, "ASK { ?s " + Title + " ?o }");
        await page.WaitForSelectorAsync(".vw-answer .vw-metric");
        Assert.Equal("true", await page.InnerTextAsync(".vw-answer .vw-metric"));

        // A CONSTRUCT: the statements, each term a chip.
        await RunAsync(page, "CONSTRUCT { ?s " + Title + " ?o } WHERE { ?s " + Title + " ?o }");
        await page.WaitForSelectorAsync("p[role=status]:has-text('All 1 statement returned to you.')");
        Assert.Contains("Weekly sync", await page.InnerTextAsync("table"));

        // A long SELECT: read to 5,000 rows and closed, shown 500 at a time.
        string ordered = "SELECT ?n WHERE { ?s " + Order + " ?n } ORDER BY ?n";
        await RunAsync(page, ordered);
        await page.WaitForSelectorAsync(".vw-results__status:has-text('Stopped at 5,000 rows')", new() { Timeout = 60_000 });
        Assert.Equal("Showing 500 of 5,000 rows. Stopped at 5,000 rows: download the result to read the rest.", await page.InnerTextAsync(".vw-results__status"));
        Assert.Equal(500, await page.Locator(".vw-results tbody tr").CountAsync());
        await page.ClickAsync("button:has-text('Show 500 more')");
        await page.WaitForSelectorAsync(".vw-results__status:has-text('Showing 1,000 of 5,000')");
        Assert.Equal(1_000, await page.Locator(".vw-results tbody tr").CountAsync());
        findings.AddRange(await WebUiBrowserTests.AuditAsync(page, "query: at the ceiling"));

        // A read the explorer closes at its ceiling is one the client ended,
        // and the server says so on its span, by request id (ADR 0112, amended
        // 2026-11-20). Six thousand rows squared do not fit in any buffer, so
        // the server is still writing when the page closes the answer.
        using (Spans spans = new())
        {
            IResponse crossed = await page.RunAndWaitForResponseAsync(
                () => RunAsync(page, "SELECT ?n ?m WHERE { ?s " + Order + " ?n . ?t " + Order + " ?m }"),
                response => response.Url.Contains("/sparql", StringComparison.Ordinal));
            string id = (await crossed.AllHeadersAsync())["varve-request-id"];
            await page.WaitForSelectorAsync(".vw-results__status:has-text('Stopped at 5,000 rows')", new() { Timeout = 60_000 });
            System.Diagnostics.Activity? ended = null;

            for (int wait = 0; wait < 200 && ended is null; wait++)
            {
                ended = spans.All.FirstOrDefault(span => Equals(span.GetTagItem("varve.request_id"), id));
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            Assert.NotNull(ended);
            Assert.Equal(true, ended.GetTagItem("varve.request.aborted"));
        }

        await RunAsync(page, ordered);
        await page.WaitForSelectorAsync(".vw-results__status:has-text('Stopped at 5,000 rows')", new() { Timeout = 60_000 });

        // The download reads at the view's position, and holds every row.
        await page.ClickAsync(".vw-menu > button:has-text('Download')");
        IDownload download = await page.RunAndWaitForDownloadAsync(() => page.ClickAsync(".vw-menu__item:has-text('CSV')"));
        string saved = await File.ReadAllTextAsync((await download.PathAsync())!, TestContext.Current.CancellationToken);
        string[] lines = saved.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("n", lines[0]);
        Assert.Equal(6_001, lines.Length);
        Assert.Matches("^notes-[0-9]+\\.csv$", download.SuggestedFilename);

        // Escape closes the open menu and gives its button the focus.
        await page.ClickAsync(".vw-menu > button:has-text('Download')");
        await page.WaitForSelectorAsync("#query-download");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForSelectorAsync("#query-download", new() { State = WaitForSelectorState.Detached });
        Assert.Equal("Download", await page.EvaluateAsync<string>("document.activeElement.textContent"));

        // The tab's history keeps what ran, newest first.
        await page.ClickAsync(".vw-menu > button:has-text('History')");
        string history = await page.InnerTextAsync("#query-history");
        Assert.StartsWith("SELECT ?n WHERE", history.Trim());
        using (JsonDocument stored = JsonDocument.Parse(await page.EvaluateAsync<string>("sessionStorage.getItem('varve.query-history')")))
        {
            JsonElement newest = stored.RootElement[0];
            Assert.Equal(ordered, newest.GetProperty("query").GetString());
            Assert.Equal("notes", newest.GetProperty("dataset").GetString());
            Assert.Equal("head", newest.GetProperty("asOf").GetString());
            Assert.Equal(5, stored.RootElement.GetArrayLength());
        }

        Assert.True(findings.Count == 0 && violations.Count == 0, string.Join(Environment.NewLine, findings.Concat(violations)));
        await context.CloseAsync();
    }

    private static async Task RunAsync(IPage page, string query)
    {
        await page.FillAsync("#query-text", query);
        await page.WaitForFunctionAsync("() => !document.querySelector('button.vw-button--primary').disabled");
        await page.ClickAsync("button.vw-button--primary");
    }

    // Statements enough to pass the ceiling: n numbered resources, each with its order.
    private static async Task SeedAsync(WebUiServer server, int count)
    {
        StringBuilder update = new("INSERT DATA {\n");

        for (int i = 0; i < count; i++)
        {
            update.Append("<https://notes.example/n").Append(i).Append("> ").Append(Order).Append(' ').Append(i).Append(" .\n");
        }

        update.Append('}');
        using HttpClient http = new() { BaseAddress = server.Address };
        using HttpResponseMessage written = await http.PostAsync("datasets/notes/sparql", new StringContent(update.ToString(), new MediaTypeHeaderValue("application/sparql-update")), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);
    }

    // Every span the server's Varve.Protocol source finished while this listened:
    // the server runs in this process.
    private sealed class Spans : IDisposable
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<System.Diagnostics.Activity> _finished = new();
        private readonly System.Diagnostics.ActivityListener _listener;

        internal Spans()
        {
            _listener = new System.Diagnostics.ActivityListener
            {
                ShouldListenTo = source => source.Name == "Varve.Protocol",
                Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = _finished.Enqueue,
            };
            System.Diagnostics.ActivitySource.AddActivityListener(_listener);
        }

        internal IReadOnlyCollection<System.Diagnostics.Activity> All => _finished;

        public void Dispose() => _listener.Dispose();
    }
}
