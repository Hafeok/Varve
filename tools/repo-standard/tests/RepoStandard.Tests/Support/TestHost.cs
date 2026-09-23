// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RepoStandard.Cli;
using RepoStandard.GitHub;

namespace RepoStandard.Tests.Support;

/// <summary>A wait that records what it was asked to wait, and returns at once.</summary>
internal sealed class RecordingDelay : IDelay
{
    public List<TimeSpan> Waits { get; } = [];

    public TimeProvider? Advance { get; set; }

    public Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        Waits.Add(duration);
        (Advance as ManualTime)?.Add(duration);
        return Task.CompletedTask;
    }
}

/// <summary>A clock that moves only when told.</summary>
internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Add(TimeSpan duration) => _now += duration;
}

/// <summary>A fetch that refuses: tests never reach the network.</summary>
internal sealed class NoNetwork : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException($"test tried to fetch {request.RequestUri}");
}

/// <summary>Serves fixed bodies by URL, for <c>extends</c> over https.</summary>
internal sealed class StaticFetch(Dictionary<string, string> bodies) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(bodies.TryGetValue(request.RequestUri!.ToString(), out string? body)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>Runs the CLI in-process against a handler, and keeps what it wrote.</summary>
internal sealed class TestHost
{
    public const string Token = "ghp_TESTTOKENtesttokenTESTTOKEN0123456789";

    public StringWriter Output { get; } = new() { NewLine = "\n" };

    public StringWriter Error { get; } = new() { NewLine = "\n" };

    public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal) { ["GITHUB_TOKEN"] = Token };

    public RecordingDelay Delay { get; } = new();

    public ManualTime Time { get; } = new();

    public HttpMessageHandler Fetch { get; set; } = new NoNetwork();

    public Task<int> RunAsync(HttpMessageHandler gitHub, params string[] args)
    {
        Delay.Advance = Time;
        Host host = new(Output, Error, name => Environment.GetValueOrDefault(name), gitHub, Fetch, Delay, Time);
        return App.RunAsync(args, host, CancellationToken.None);
    }
}
