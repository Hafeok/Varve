// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Varve.Inference.Model;
using Varve.Inference.Store.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Xunit;
using static Varve.Inference.Store.Tests.Support;

namespace Varve.Inference.Store.Tests;

/// <summary>
/// ADR 0195 point 6, amended 2026-11-25: what a projection published at the
/// end of its last pass is read without waiting for the pass running now; a
/// pass is named while it runs; explain answers <c>inference-behind</c>
/// rather than wait past its bound; and a cancelled pass ends within a
/// thousand rule firings, dropping its closures and saving nothing.
/// </summary>
public class PassTests
{
    private static readonly RdfTerm Type = IriOf(RdfNs + "type");
    private static readonly RdfTerm Next = Ex("next");

    // A probe's bound here: far under a probe's one second, and far over a field read on a loaded runner.
    private static readonly TimeSpan ProbeBound = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task during_a_cold_build_the_snapshot_and_the_pass_answer_at_once()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await LongBuildAsync(ct);
        InferenceProjection projection = new(dataset, Binding(InferenceProfile.Owl2Rl));
        InferenceSnapshot before = projection.Snapshot;
        using DatasetView head = dataset.Pin();
        Task build = Task.Run(async () => await projection.ResetAsync(head, ct), ct);
        InferencePass pass = await PassAsync(projection, build);
        Assert.Equal(InferencePassKind.ColdStart, pass.Kind);
        Assert.Equal(head.Position, pass.Position);

        int probes = 0;
        while (!build.IsCompleted && probes < 20)
        {
            Stopwatch probe = Stopwatch.StartNew();
            InferenceSnapshot snapshot = projection.Snapshot;
            int clashes = projection.Clashes().Count;
            InferencePass? running = projection.Pass;
            probe.Stop();
            Assert.True(probe.Elapsed < ProbeBound, $"a probe during the build took {probe.Elapsed.TotalMilliseconds:F0} ms");
            if (!build.IsCompleted)
            {
                // What the last whole pass left: the empty projection's, not the build's part way.
                Assert.Same(before, snapshot);
                Assert.Equal(0, clashes);
            }

            probes++;
            await Task.Delay(50, ct);
        }

        Assert.True(probes > 0, "the build ended before a probe could run beside it");
        await build;
        Assert.Null(projection.Pass);
        Assert.Equal(head.Position, projection.Snapshot.Position);
        Assert.True(projection.Snapshot.Held.Value > 0);
    }

    [Fact]
    public async Task explain_during_a_pass_answers_behind_within_its_wait()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await LongBuildAsync(ct);
        InferenceProjection projection = new(dataset, Binding(InferenceProfile.Owl2Rl), new InferenceOptions { ExplainWait = new ExplainWait(TimeSpan.FromMilliseconds(300)) });
        using DatasetView head = dataset.Pin();
        Task build = Task.Run(async () => await projection.ResetAsync(head, ct), ct);
        await PassAsync(projection, build);

        // At the projection's position, the live closures are the pass's: explain waits its bound for them, then says it is behind.
        Stopwatch waited = Stopwatch.StartNew();
        (InferenceReadOutcome outcome, Explanation? explanation) = await projection.ExplainAsync(projection.Position, null, Ex("n0"), Next, Ex("n2"), ct);
        waited.Stop();
        Assert.False(build.IsCompleted, "the build ended before explain could ask beside it");
        Assert.Equal(InferenceReadOutcome.Behind, outcome);
        Assert.Null(explanation);
        Assert.True(waited.Elapsed < TimeSpan.FromMilliseconds(300) + TimeSpan.FromSeconds(1), $"explain waited {waited.Elapsed.TotalMilliseconds:F0} ms");

        // Above it, behind at once, as before.
        waited.Restart();
        (outcome, _) = await projection.ExplainAsync(head.Position, null, Ex("n0"), Next, Ex("n2"), ct);
        Assert.Equal(InferenceReadOutcome.Behind, outcome);
        Assert.True(waited.Elapsed < ProbeBound + TimeSpan.FromMilliseconds(300));
        await build;
    }

    [Fact]
    public async Task a_cancelled_build_ends_at_once_keeps_its_position_and_saves_nothing()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await LongBuildAsync(ct);
        InferenceProjection projection = new(dataset, Binding(InferenceProfile.Owl2Rl));
        MemoryStorage cache = new();
        await projection.SaveAsync(cache.Derived, ct);
        BlobName saved = Assert.Single(await cache.Derived.ListAsync(ct));

        using CancellationTokenSource stop = new();
        using DatasetView head = dataset.Pin();
        Task build = Task.Run(async () => await projection.ResetAsync(head, stop.Token), ct);
        await PassAsync(projection, build);
        await Task.Delay(100, ct);
        Assert.False(build.IsCompleted, "the build ended before it could be cancelled");
        Stopwatch ended = Stopwatch.StartNew();
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build);
        ended.Stop();
        Assert.True(ended.Elapsed < TimeSpan.FromSeconds(2), $"the cancelled build ended {ended.Elapsed.TotalMilliseconds:F0} ms after the cancel");

        Assert.True(projection.Abandoned);
        Assert.Null(projection.Pass);
        Assert.Equal(new Position(0), projection.Snapshot.Position);
        await projection.SaveAsync(cache.Derived, ct);
        Assert.Equal(saved, Assert.Single(await cache.Derived.ListAsync(ct)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await CommitAsync(dataset, [(Ex("a"), Type, Ex("C"), Ex("g1"), true)], ct);
            await dataset.CatchUpAsync(projection, ct);
        });
    }

    // A data graph whose OWL 2 RL closure takes a few seconds to build: a chain under a transitive property, its pairs quadratic in its length.
    private static async Task<Dataset> LongBuildAsync(CancellationToken ct)
    {
        Dataset dataset = await CreateAsync(ct);
        await CommitAsync(dataset, [(Next, Type, IriOf(OwlNs + "TransitiveProperty"), TBox, true)], ct);
        await CommitAsync(dataset, Enumerable.Range(0, 400).Select(i => (Ex("n" + i), Next, Ex("n" + (i + 1)), (RdfTerm?)Ex("g1"), true)), ct);
        return dataset;
    }

    // The pass once it has begun; a build that ends first fails the test that needed it running.
    private static async Task<InferencePass> PassAsync(InferenceProjection projection, Task build)
    {
        while (true)
        {
            if (projection.Pass is { } pass)
            {
                return pass;
            }

            Assert.False(build.IsCompleted, "the build ended before its pass was seen");
            await Task.Delay(1);
        }
    }
}
