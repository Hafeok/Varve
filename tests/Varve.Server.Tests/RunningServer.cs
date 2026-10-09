// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Varve.Server.Tests;

/// <summary>
/// The server's own composition root, run in process on a loopback port with
/// its configuration given as command-line settings, exactly as an operator
/// gives them.
/// </summary>
internal sealed class RunningServer : IAsyncDisposable
{
    private readonly WebApplication? _app;
    private readonly Task<int> _run;

    private RunningServer(WebApplication? app, Task<int> run, Uri address)
    {
        _app = app;
        _run = run;
        Address = address;
        Client = new HttpClient { BaseAddress = address };
    }

    internal Uri Address { get; }

    internal HttpClient Client { get; }

    /// <summary>
    /// A server somebody else runs, at <paramref name="address"/>, configured
    /// as the test expects: the container CI built (ADR 0111). Stopping it is
    /// not this object's to do.
    /// </summary>
    internal static RunningServer External(Uri address) => new(null, Task.FromResult(0), address);

    internal static async Task<RunningServer> StartAsync(IReadOnlyDictionary<string, string> settings)
    {
        string[] args = [.. settings.Select(s => "--" + s.Key + "=" + s.Value), "--urls=http://127.0.0.1:0", "--Logging:LogLevel:Default=Warning"];
        TaskCompletionSource<WebApplication> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> run = Task.Run(() => ServerHost.RunAsync(args, ready));
        Task first = await Task.WhenAny(ready.Task, run);

        if (first == run)
        {
            throw new InvalidOperationException("The server did not start: exit " + await run);
        }

        WebApplication app = await ready.Task;
        string address = ((IApplicationBuilder)app).ServerFeatures.Get<IServerAddressesFeature>()!.Addresses.First();
        return new RunningServer(app, run, new Uri(address.TrimEnd('/') + "/"));
    }

    /// <summary>Stops the server as SIGTERM does, and answers its exit code.</summary>
    internal async Task<int> StopAsync()
    {
        _app?.Lifetime.StopApplication();
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
