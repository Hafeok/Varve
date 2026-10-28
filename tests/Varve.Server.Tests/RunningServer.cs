// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Varve.Management;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Server.Tests;

/// <summary>
/// The server's own composition root, run in process on a loopback port with
/// its configuration given as command-line settings, exactly as an operator
/// gives them.
/// </summary>
internal sealed class RunningServer : IAsyncDisposable
{
    private readonly Task<int> _run;

    private RunningServer(StartedServer? started, Task<int> run, Uri address)
    {
        Started = started;
        _run = run;
        Address = address;
        Client = new HttpClient { BaseAddress = address };
    }

    /// <summary>The host and its fleet, when this process runs it.</summary>
    internal StartedServer? Started { get; }

    internal Uri Address { get; }

    internal HttpClient Client { get; }

    /// <summary>
    /// A server somebody else runs, at <paramref name="address"/>, configured
    /// as the test expects: the container CI built (ADR 0111). Stopping it is
    /// not this object's to do.
    /// </summary>
    internal static RunningServer External(Uri address) => new(null, Task.FromResult(0), address);

    /// <summary>
    /// Starts the server with <paramref name="settings"/>. The keys the
    /// management plane replaced (ADR 0163) are the test's shorthand for the
    /// declarations they convert to, as <c>varve apply --from-config</c> prints
    /// them: the server starts without them, its server admins as its
    /// bootstrap operators, and the declarations are committed to its system
    /// dataset before the test's first request.
    /// </summary>
    internal static async Task<RunningServer> StartAsync(IReadOnlyDictionary<string, string> settings)
    {
        Dictionary<string, string?> legacy = [];
        Dictionary<string, string> given = [];

        foreach ((string key, string value) in settings)
        {
            if (LegacyConfiguration.IsLegacy(key))
            {
                legacy[key] = value;
            }
            else
            {
                given[key] = value;
            }
        }

        (string trig, List<string> bootstrap, List<string> errors) = LegacyConfiguration.Convert(new ConfigurationBuilder().AddInMemoryCollection(legacy).Build().GetSection("Varve"));

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        if (given.TryGetValue("Varve:Auth:Mode", out string? mode) && mode == "Oidc" && bootstrap.Count == 0 && !given.Keys.Any(key => key.StartsWith("Varve:Manage:BootstrapOperators", StringComparison.Ordinal)))
        {
            bootstrap.Add(Operator);
        }

        for (int i = 0; i < bootstrap.Count; i++)
        {
            given["Varve:Manage:BootstrapOperators:" + i] = bootstrap[i];
        }

        RunningServer server = await StartRawAsync(given);

        if (legacy.Count > 0 && server.Started is { } started)
        {
            await DeclareAsync(started.Fleet, trig);
        }

        return server;
    }

    /// <summary>The role a test's server has as its bootstrap operator when the test names none.</summary>
    internal const string Operator = "varve-test-operator";

    /// <summary>Commits TriG declarations to the server's system dataset, and applies them before answering.</summary>
    internal static async Task DeclareAsync(Fleet fleet, string trig)
    {
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

    /// <summary>Starts the server with exactly <paramref name="settings"/>.</summary>
    internal static async Task<RunningServer> StartRawAsync(IReadOnlyDictionary<string, string> settings)
    {
        string[] args = [.. settings.Select(s => "--" + s.Key + "=" + s.Value), "--urls=http://127.0.0.1:0", "--Logging:LogLevel:Default=Warning"];
        TaskCompletionSource<StartedServer> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> run = Task.Run(() => ServerHost.RunAsync(args, ready));
        Task first = await Task.WhenAny(ready.Task, run);

        if (first == run)
        {
            throw new InvalidOperationException("The server did not start: exit " + await run);
        }

        StartedServer started = await ready.Task;
        string address = ((IApplicationBuilder)started.App).ServerFeatures.Get<IServerAddressesFeature>()!.Addresses.First();
        return new RunningServer(started, run, new Uri(address.TrimEnd('/') + "/"));
    }

    /// <summary>Stops the server as SIGTERM does, and answers its exit code.</summary>
    internal async Task<int> StopAsync()
    {
        Started?.App.Lifetime.StopApplication();
        return await _run;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        if (!_run.IsCompleted)
        {
            await StopAsync();
        }
    }
}
