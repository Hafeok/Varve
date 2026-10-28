// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Varve.Management;
using Varve.Server;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.WebUi.Browser.Tests;

/// <summary>
/// The server, in this process, serving a copy of the published Web UI under
/// <c>/ui/</c> (ADR 0202) with the <c>appsettings.json</c> the test gives it.
/// </summary>
internal sealed class WebUiServer : IAsyncDisposable
{
    private readonly Task<int> _run;
    private readonly string _bundle;

    private WebUiServer(StartedServer started, Task<int> run, Uri address, string bundle)
    {
        Started = started;
        _run = run;
        Address = address;
        _bundle = bundle;
    }

    internal StartedServer Started { get; }

    internal Uri Address { get; }

    internal Uri Ui => new(Address, "ui/");

    /// <summary>
    /// What the browser tests need, or a skip: the published bundle in
    /// <c>VARVE_WEBUI</c>, the browser in <c>VARVE_CHROME</c>. With
    /// <c>VARVE_BROWSER_TESTS_REQUIRED</c> set, a missing one fails instead.
    /// </summary>
    internal static (string Bundle, string Chrome) Required()
    {
        string? bundle = Environment.GetEnvironmentVariable("VARVE_WEBUI");
        string? chrome = Environment.GetEnvironmentVariable("VARVE_CHROME");

        if (string.IsNullOrEmpty(bundle) || !File.Exists(Path.Combine(bundle, "index.html")) || string.IsNullOrEmpty(chrome) || !File.Exists(chrome))
        {
            string why = "VARVE_WEBUI (a published wwwroot) and VARVE_CHROME (a Chrome) are needed.";

            if (Environment.GetEnvironmentVariable("VARVE_BROWSER_TESTS_REQUIRED") == "1")
            {
                Assert.Fail(why);
            }

            Assert.Skip(why);
        }

        return (bundle, chrome);
    }

    internal static async Task<WebUiServer> StartAsync(string bundle, IReadOnlyDictionary<string, string> settings, JsonObject webUiAuth)
    {
        // A copy, so its appsettings.json can be the test's own; the
        // compressed copies of the original are left out, so none is stale.
        string copy = Directory.CreateTempSubdirectory("varve-webui-").FullName;

        foreach (string file in Directory.EnumerateFiles(bundle, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(bundle, file);

            if (relative.StartsWith("appsettings.json", StringComparison.Ordinal))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(copy, relative))!);
            File.Copy(file, Path.Combine(copy, relative));
        }

        File.WriteAllText(Path.Combine(copy, "appsettings.json"), new JsonObject { ["Varve"] = new JsonObject { ["Server"] = "", ["Auth"] = webUiAuth } }.ToJsonString());

        Dictionary<string, string> all = new(settings) { ["Varve:WebUi:Path"] = copy };
        string[] args = [.. all.Select(s => "--" + s.Key + "=" + s.Value), "--urls=http://127.0.0.1:0", "--Logging:LogLevel:Default=Warning"];
        TaskCompletionSource<StartedServer> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> run = Task.Run(() => ServerHost.RunAsync(args, ready));
        Task first = await Task.WhenAny(ready.Task, run);

        if (first == run)
        {
            throw new InvalidOperationException("The server did not start: exit " + await run);
        }

        StartedServer started = await ready.Task;
        string address = ((IApplicationBuilder)started.App).ServerFeatures.Get<IServerAddressesFeature>()!.Addresses.First();
        return new WebUiServer(started, run, new Uri(address.TrimEnd('/') + "/"), copy);
    }

    /// <summary>Commits TriG declarations to the system dataset and applies them, as an apply would.</summary>
    internal async Task DeclareAsync(string trig)
    {
        Fleet fleet = Started.Fleet;
        InMemoryDataset desired = ManagementTrig.TryParse(Encoding.UTF8.GetBytes(trig), null, out string? error) ?? throw new InvalidOperationException(error);
        CommitRequestBuilder request = new() { ExpectedPosition = fleet.System.Head, Validators = [fleet.Validator] };

        using (IQuadCursor cursor = desired.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any))
        {
            while (cursor.MoveNext())
            {
                Quad quad = cursor.Current;
                desired.TryExternalise(quad.Subject, out RdfTerm? s);
                desired.TryExternalise(quad.Predicate, out RdfTerm? p);
                desired.TryExternalise(quad.Object, out RdfTerm? o);
                desired.TryExternalise(quad.Graph, out RdfTerm? g);
                request = request.Assert(s!, p!, o!, g!);
            }
        }

        CommitResult result = await fleet.System.CommitAsync(request);

        if (result.Outcome is not (CommitOutcome.Committed or CommitOutcome.NoChange))
        {
            throw new InvalidOperationException("The declarations were refused: " + result.Outcome + ".");
        }

        await fleet.AdvanceAsync(default);
    }

    public async ValueTask DisposeAsync()
    {
        Started.App.Lifetime.StopApplication();
        await _run;
        Directory.Delete(_bundle, recursive: true);
    }
}
