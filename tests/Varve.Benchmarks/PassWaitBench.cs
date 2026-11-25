// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The gate of #412 (ADR 0195 point 6, amended 2026-11-25): at a REC size, how
// long what readiness reads, explain and a cancelled build wait on a binding's
// pass. Each phase is held to its bound, and the run exits 1 when one is
// passed: the nightly runs it at REC M, and a pull request's tests hold the
// same reads to the same bounds over a build of seconds.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Inference.Model;
using Varve.Inference.Store;
using Varve.Inference.Store.Model;
using Varve.Rdf;
using Varve.Rec.Tests;
using Varve.Store;
using Varve.Store.Log;
using Varve.Turtle;

namespace Varve.Benchmarks;

internal static class PassWaitBench
{
    private const ulong Seed = 20261015;
    private static readonly RdfTerm TBoxGraph = RdfTerm.Iri("https://example.org/rec/tbox"u8);

    /// <summary>What readiness reads of a binding, in process: a probe's bound before HTTP.</summary>
    private static readonly TimeSpan ProbeBound = TimeSpan.FromMilliseconds(100);

    /// <summary>Explain's wait for the live closures, the default, and what it may take beyond it.</summary>
    private static readonly TimeSpan ExplainBound = TimeSpan.FromSeconds(2.5);

    /// <summary>From a cancel to the cancelled build's return: <c>Varve:Inference:StopWait</c>'s default.</summary>
    private static readonly TimeSpan CancelBound = TimeSpan.FromSeconds(10);

    internal static async Task<int> RunAsync(string sizeName, string profileName, bool crossGraph)
    {
        CancellationToken none = CancellationToken.None;
        RecSize size = Enum.Parse<RecSize>(sizeName);
        InferenceProfile profile = profileName == "owl2rl" ? InferenceProfile.Owl2Rl : InferenceProfile.Rdfs;
        Console.WriteLine($"// pass waits, REC {size}, {profileName}, {(crossGraph ? "cross-graph" : "per-graph")}, .NET {Environment.Version}, {Environment.ProcessorCount} cores");
        List<string> failed = [];

        await using Varve.Store.Dataset dataset = await Varve.Store.Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, none);
        Stopwatch load = Stopwatch.StartNew();
        await LoadTBoxAsync(dataset, none);
        int commits = 0;
        foreach (RecCommit commit in RecPortfolio.Generate(size, Seed))
        {
            await dataset.CommitAsync(Request(commit, retract: true), none);
            commits++;
        }

        Console.WriteLine($"server GC {System.Runtime.GCSettings.IsServerGC}; heap limit {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes} B");
        Console.WriteLine($"loaded {commits} commits in {load.Elapsed.TotalSeconds:F1} s; head {dataset.Head.Value}");
        InferenceBinding binding = new(RdfTerm.Iri("https://example.org/rec/binding"u8), [TBoxGraph], GraphScope.Of([], ["https://example.org/rec/graph/"], DefaultGraphAccess.Excluded), profile) { CrossGraph = crossGraph };
        InferenceOptions options = new() { ClosureBytes = new ByteCount(long.MaxValue / 4) };

        // 1. A cold start, probed as readiness probes it, and explain asked at the projection's position beside it.
        InferenceProjection projection = new(dataset, binding, options);
        using (DatasetView head = dataset.Pin())
        {
            Stopwatch build = Stopwatch.StartNew();
            Task reset = Task.Run(async () => await projection.ResetAsync(head, none));
            List<double> probes = [];
            Task<(double Seconds, InferenceReadOutcome Outcome)>? explain = null;
            while (!reset.IsCompleted)
            {
                probes.Add(Probe(projection));
                explain ??= projection.Pass is null ? null : Explain(projection, projection.Position);
                await Task.WhenAny(reset, Task.Delay(500, none));
            }

            await reset;
            Console.WriteLine($"cold start: {GC.CollectionCount(2)} full collections so far, pause {GC.GetTotalPauseDuration().TotalSeconds:F1} s");
            Console.WriteLine($"cold start: built in {build.Elapsed.TotalSeconds:F1} s ({projection.ClosureHeld.Value} B held, {projection.Snapshot.Clashes.Count} clashes)");
            Console.WriteLine($"  probes during the build: {probes.Count}; slowest {Max(probes):F2} ms");
            Hold(failed, "a probe during the cold build", Max(probes), ProbeBound);
            if (explain is not null)
            {
                (double seconds, InferenceReadOutcome outcome) = await explain;
                Console.WriteLine($"  explain at the projection's position during the build: {outcome} after {seconds:F2} s");
                Hold(failed, "explain during the cold build", seconds * 1000, ExplainBound);
            }
        }

        // 2. Idle.
        List<double> idle = [.. Enumerable.Range(0, 200).Select(_ => Probe(projection))];
        Console.WriteLine($"idle: 200 probes; slowest {Max(idle):F3} ms");
        Hold(failed, "an idle probe", Max(idle), ProbeBound);

        // 3. Commits: a pass each, a probe and an explain beside each.
        List<double> passes = [], waits = [], explains = [];
        foreach (RecCommit commit in RecPortfolio.Generate(size, Seed + 1).Skip(500).Take(size == RecSize.S ? 200 : 30))
        {
            await dataset.CommitAsync(Request(commit, retract: false), none);
            Position at = dataset.Head;
            Stopwatch pass = Stopwatch.StartNew();
            Task apply = Task.Run(async () => await dataset.CatchUpAsync(projection, none));
            await Task.Delay(1, none);
            waits.Add(Probe(projection));
            Task<(double Seconds, InferenceReadOutcome Outcome)> asked = AsServerExplains(projection, at);
            await apply;
            passes.Add(pass.Elapsed.TotalMilliseconds);
            explains.Add((await asked).Seconds * 1000);
        }

        passes.Sort();
        Console.WriteLine($"commits: {passes.Count} passes; ms p50 {passes[passes.Count / 2]:F2}, max {passes[^1]:F2}; a probe beside each, slowest {Max(waits):F2} ms; explain beside each, slowest {Max(explains):F0} ms");
        Hold(failed, "a probe beside a commit", Max(waits), ProbeBound);
        Hold(failed, "explain beside a commit", Max(explains), ExplainBound);

        // 4. Shutdown: a cold start cancelled 10 s in, as a stop cancels its feed.
        {
            InferenceProjection cancelled = new(dataset, binding, options);
            using DatasetView head = dataset.Pin();
            using CancellationTokenSource stop = new();
            Task reset = Task.Run(async () => await cancelled.ResetAsync(head, stop.Token));
            await Task.WhenAny(reset, Task.Delay(10_000, none));
            if (reset.IsCompleted)
            {
                Console.WriteLine("shutdown: the build ended within 10 s; nothing to cancel");
            }
            else
            {
                Stopwatch ended = Stopwatch.StartNew();
                await stop.CancelAsync();
                try
                {
                    await reset;
                }
                catch (OperationCanceledException)
                {
                }

                Console.WriteLine($"shutdown: the cancelled build returned {ended.Elapsed.TotalSeconds:F2} s after the cancel; abandoned {cancelled.Abandoned}");
                Hold(failed, "a cancelled build's return", ended.Elapsed.TotalMilliseconds, CancelBound);
            }
        }

        foreach (string failure in failed)
        {
            Console.WriteLine("FAIL: " + failure);
        }

        return failed.Count == 0 ? 0 : 1;
    }

    // What readiness reads of a binding, in milliseconds.
    private static double Probe(InferenceProjection projection)
    {
        Stopwatch probe = Stopwatch.StartNew();
        InferenceSnapshot snapshot = projection.Snapshot;
        _ = snapshot.Clashes.Count + snapshot.PremiseSetGraphs.Count + snapshot.Held.Value + snapshot.Peak.Value;
        _ = projection.Pass;
        _ = projection.Position;
        _ = projection.HeldFrom;
        _ = projection.MemoryLimit;
        return probe.Elapsed.TotalMilliseconds;
    }

    private static Task<(double Seconds, InferenceReadOutcome Outcome)> Explain(InferenceProjection projection, Position at) => Task.Run(async () =>
    {
        Stopwatch sw = Stopwatch.StartNew();
        (InferenceReadOutcome outcome, _) = await projection.ExplainAsync(at, null, RdfTerm.Iri("https://example.org/rec/x"u8), RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8), RdfTerm.Iri("https://w3id.org/rec#Space"u8), CancellationToken.None);
        return (sw.Elapsed.TotalSeconds, outcome);
    });

    // As the server explains at a position: one bounded wait for the projection to reach it, then explain, once more after a wait when behind.
    private static Task<(double Seconds, InferenceReadOutcome Outcome)> AsServerExplains(InferenceProjection projection, Position at) => Task.Run(async () =>
    {
        Stopwatch sw = Stopwatch.StartNew();
        int gcs = GC.CollectionCount(2);
        using (CancellationTokenSource bound = new(TimeSpan.FromSeconds(2)))
        {
            try
            {
                await projection.WaitForAsync(at, bound.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        double waited = sw.Elapsed.TotalMilliseconds;
        (InferenceReadOutcome outcome, _) = await projection.ExplainAsync(at, null, RdfTerm.Iri("https://example.org/rec/x"u8), RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8), RdfTerm.Iri("https://w3id.org/rec#Space"u8), CancellationToken.None);
        if (sw.Elapsed.TotalMilliseconds > 500)
        {
            Console.WriteLine($"  an explain at {at.Value}: {outcome}, {waited:F0} ms for the position, {sw.Elapsed.TotalMilliseconds - waited:F0} ms in explain, {GC.CollectionCount(2) - gcs} full collections meanwhile");
        }

        return (sw.Elapsed.TotalSeconds, outcome);
    });

    private static double Max(List<double> values) => values.Count == 0 ? 0 : values.Max();

    private static void Hold(List<string> failed, string what, double milliseconds, TimeSpan bound)
    {
        if (milliseconds > bound.TotalMilliseconds)
        {
            failed.Add($"{what} took {milliseconds:F0} ms, over its bound of {bound.TotalMilliseconds:F0} ms");
        }
    }

    private static CommitRequestBuilder Request(RecCommit commit, bool retract)
    {
        CommitRequestBuilder request = new();
        if (retract)
        {
            foreach (RecQuad q in commit.Retracted)
            {
                request.Retract(RequestTerm.FromTerm(Term(q.Subject)), RequestTerm.FromTerm(Term(q.Predicate)), RequestTerm.FromTerm(Term(q.Object)), RequestTerm.FromTerm(Term(q.Graph)));
            }
        }

        foreach (RecQuad q in commit.Asserted)
        {
            request.Assert(RequestTerm.FromTerm(Term(q.Subject)), RequestTerm.FromTerm(Term(q.Predicate)), RequestTerm.FromTerm(Term(q.Object)), RequestTerm.FromTerm(Term(q.Graph)));
        }

        return request;
    }

    private static async Task LoadTBoxAsync(Varve.Store.Dataset dataset, CancellationToken ct)
    {
        string repository = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
        string root = Path.Combine(repository, "testdata", "rec", "v4.1.0");
        CommitRequestBuilder request = new();
        foreach (string file in new[] { Path.Combine(root, "rec.ttl"), Path.Combine(root, "Brick+patches.ttl") })
        {
            ParseResult result = TurtleParser.Parse(
                File.ReadAllBytes(file),
                (in QuadView quad) => request.Assert(RequestTerm.FromTerm(quad.Subject.Materialise()), RequestTerm.FromTerm(quad.Predicate.Materialise()), RequestTerm.FromTerm(quad.Object.Materialise()), RequestTerm.FromTerm(TBoxGraph)),
                new TurtleOptions { Syntax = RdfSyntax.Turtle });
            if (!result.Succeeded)
            {
                throw new InvalidDataException(file + ": " + result.FirstError.ToString());
            }
        }

        await dataset.CommitAsync(request, ct);
    }

    private static RdfTerm Term(string text) => text[0] == '<'
        ? RdfTerm.Iri(Encoding.UTF8.GetBytes(text[1..^1]))
        : RdfTerm.Literal(Encoding.UTF8.GetBytes(text[1..^1]));
}
