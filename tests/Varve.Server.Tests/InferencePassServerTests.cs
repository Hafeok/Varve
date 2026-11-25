// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ChildServer = Varve.Server.Tests.ClosureMemoryGateTests.ChildServer;

namespace Varve.Server.Tests;

/// <summary>
/// #412; ADR 0195 point 6, amended 2026-11-25, in a real process: a closure
/// build never makes the instance unready, readiness answers within its bound
/// during a cold build and idle and names the pass, and a SIGTERM during a
/// build ends the process within the shipped probes' grace period.
/// </summary>
/// <remarks>
/// The data is a chain under a transitive property in one data graph, the
/// TBox one triple: little to upload, a closure quadratic in the chain's
/// length, and a build of seconds. The binding is declared after the data is
/// committed, so its projection starts cold at the head.
/// </remarks>
public class InferencePassServerTests
{
    private const string Varve = "https://w3id.org/varve/ns#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Owl = "http://www.w3.org/2002/07/owl#";
    private const int Chain = 650;

    // A probe's bound here: the shipped readiness probe's timeout is 1 s.
    private static readonly TimeSpan ProbeBound = TimeSpan.FromMilliseconds(500);

    // The shipped grace period: fleet.yaml's terminationGracePeriodSeconds.
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Readiness_answers_ready_within_its_bound_during_a_cold_build_and_idle_and_names_the_pass()
    {
        await using ChildServer server = await ChildServer.StartAsync(2L << 30, closureBytes: 1L << 30);
        await ColdAsync(server);

        List<(TimeSpan Took, HttpStatusCode Status, string Body)> during = [];
        bool named = false;
        while (true)
        {
            (TimeSpan took, HttpStatusCode status, string body, JsonElement? binding) = await ProbeAsync(server);
            Assert.False(server.Exited, server.Log);
            if (binding is { } b && b.GetProperty("lag").GetInt64() == 0 && !b.TryGetProperty("pass", out _))
            {
                break;
            }

            during.Add((took, status, body));
            if (binding is { } running && running.TryGetProperty("pass", out JsonElement pass))
            {
                named |= pass.GetProperty("kind").GetString() == "cold-start";
            }

            await Task.Delay(250, Ct);
        }

        Assert.True(during.Count >= 3, $"the build ended after {during.Count} probes: too short to probe beside");
        Assert.True(named, "no probe named the cold start: " + during[^1].Body);
        Assert.All(during, probe => Assert.True(probe.Status == HttpStatusCode.OK, probe.Body));
        TimeSpan slowest = during.Max(probe => probe.Took);
        Assert.True(slowest < ProbeBound, $"the slowest of {during.Count} probes during the build took {slowest.TotalMilliseconds:F0} ms");

        // Idle: the clashes are the snapshot's, never computed by a probe.
        List<TimeSpan> idle = [];
        for (int i = 0; i < 10; i++)
        {
            (TimeSpan took, HttpStatusCode status, string body, _) = await ProbeAsync(server);
            Assert.True(status == HttpStatusCode.OK, body);
            idle.Add(took);
        }

        Assert.True(idle.Max() < ProbeBound, $"the slowest idle probe took {idle.Max().TotalMilliseconds:F0} ms");
    }

    [Fact]
    public async Task A_sigterm_during_a_cold_build_ends_the_process_within_the_grace_period_and_a_restart_after_a_build_loads()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "SIGTERM is POSIX; the orchestrators shipped (fleet.yaml, Container Apps) stop a Linux container with it.");
        ChildServer first = await ChildServer.StartAsync(2L << 30, closureBytes: 1L << 30);
        string root = first.Root;
        try
        {
            await ColdAsync(first);
            await RunningAsync(first, "cold-start");
            TimeSpan stopped = await first.TerminateAsync();
            Assert.True(stopped < Grace, $"the process took {stopped.TotalSeconds:F1} s to exit after SIGTERM during a build:\n" + first.Log);
            await first.DisposeAsync();

            // Started again over the same datasets: nothing was saved part way, so the binding builds again; once
            // it has built and the process stops idle, the next start loads what it saved.
            await using (ChildServer second = await ChildServer.StartWithAsync(2L << 30, Settings, root))
            {
                await SettledAsync(second);
                TimeSpan idleStop = await second.TerminateAsync();
                Assert.True(idleStop < Grace, $"an idle stop took {idleStop.TotalSeconds:F1} s:\n" + second.Log);
            }

            await using ChildServer third = await ChildServer.StartWithAsync(2L << 30, Settings, root);
            await RunningAsync(third, "load");
            await SettledAsync(third);
            third.Release();
        }
        finally
        {
            await first.DisposeAsync();
        }
    }

    private static readonly string[] Settings =
    [
        "--Varve:Inference:ClosureBytes=" + (1L << 30).ToString(CultureInfo.InvariantCulture),
        "--Varve:Inference:ReserveBytes=" + (512L << 20).ToString(CultureInfo.InvariantCulture),
        "--Varve:Store:MemtableLimit=10000",
    ];

    // The chain committed to a dataset in files with no binding; then the binding declared over it, which starts cold at the head.
    private static async Task ColdAsync(ChildServer server)
    {
        await server.DeclarePlainAsync("d", storage: "File");
        await server.UpdateAsync("d", "INSERT DATA { GRAPH <http://ex/tbox> { <http://ex/next> <" + Rdf + "type> <" + Owl + "TransitiveProperty> } }");
        StringBuilder data = new("INSERT DATA { GRAPH <http://ex/g/0> {");
        for (int i = 0; i < Chain; i++)
        {
            data.Append(CultureInfo.InvariantCulture, $" <http://ex/n{i}> <http://ex/next> <http://ex/n{i + 1}> .");
        }

        await server.UpdateAsync("d", data.Append(" } }").ToString());
        await server.DeclareAsync("d", "OWL2RL", crossGraph: false, storage: "File");
    }

    // Until readiness names a pass of the kind.
    private static async Task RunningAsync(ChildServer server, string kind)
    {
        string last = string.Empty;
        for (int attempt = 0; attempt < 1200; attempt++)
        {
            (_, _, last, JsonElement? binding) = await ProbeAsync(server);
            if (binding is { } b && b.TryGetProperty("pass", out JsonElement pass) && pass.GetProperty("kind").GetString() == kind)
            {
                return;
            }

            Assert.False(server.Exited, server.Log);
            await Task.Delay(50, Ct);
        }

        Assert.Fail("readiness never named a " + kind + " pass: " + last + server.Log);
    }

    private static async Task SettledAsync(ChildServer server)
    {
        for (int attempt = 0; attempt < 2400; attempt++)
        {
            (_, _, _, JsonElement? binding) = await ProbeAsync(server);
            if (binding is { } b && b.GetProperty("state").GetString() == "following" && b.GetProperty("lag").GetInt64() == 0 && !b.TryGetProperty("pass", out _)
                && b.GetProperty("held").GetInt64() > 0)
            {
                return;
            }

            Assert.False(server.Exited, server.Log);
            await Task.Delay(250, Ct);
        }

        Assert.Fail("the binding did not settle: " + server.Log);
    }

    // One readiness probe: how long it took, its status and body, and the binding's entry.
    private static async Task<(TimeSpan Took, HttpStatusCode Status, string Body, JsonElement? Binding)> ProbeAsync(ChildServer server)
    {
        Stopwatch took = Stopwatch.StartNew();
        HttpResponseMessage ready = await server.Http.GetAsync(new Uri(server.Address, "health/ready"), Ct);
        string body = await ready.Content.ReadAsStringAsync(Ct);
        took.Stop();
        using JsonDocument json = JsonDocument.Parse(body);
        JsonElement? binding = null;
        if (json.RootElement.TryGetProperty("datasets", out JsonElement datasets)
            && datasets.ValueKind == JsonValueKind.Object
            && datasets.TryGetProperty("d", out JsonElement entry)
            && entry.TryGetProperty("inference", out JsonElement inference)
            && inference.TryGetProperty("urn:binding:d", out JsonElement found))
        {
            binding = found.Clone();
        }

        return (took.Elapsed, ready.StatusCode, body, binding);
    }
}
