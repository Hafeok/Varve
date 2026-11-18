// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

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
/// ADR 0195 point 6: a binding whose closures would pass its limit stops,
/// serves no partial closure at or above the commit that stopped it, still
/// serves the history below it, and is never tried again: not by a data
/// commit, a TBox commit or a reset (ADR 0195 point 6, as amended
/// 2026-11-18); only a new projection, its owner's, builds it again.
/// </summary>
public class MemoryLimitTests
{
    private static readonly RdfTerm Type = IriOf(RdfNs + "type");
    private static readonly RdfTerm SubClassOf = IriOf(RdfsNs + "subClassOf");

    [Fact]
    public async Task a_binding_past_its_limit_stops_and_is_never_tried_again()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await CreateAsync(ct);
        InferenceBinding binding = Binding(InferenceProfile.Rdfs);

        // The limit: what a small graph needs, by the budget's count, and a little more.
        await CommitAsync(dataset, [(Ex("C1"), SubClassOf, Ex("C2"), TBox, true), (Ex("a"), Type, Ex("C1"), Ex("g1"), true)], ct);
        InferenceProjection probe = new(dataset, binding);
        await dataset.CatchUpAsync(probe, ct);
        long needed = probe.ClosurePeak.Value;
        InferenceOptions options = new() { ClosureBytes = new ByteCount(needed + (needed / 4)) };

        InferenceProjection projection = new(dataset, binding, options);
        await dataset.CatchUpAsync(projection, ct);
        Assert.False(projection.Stopped);
        Position small = dataset.Head;

        // Forty instances of C1 in a new graph: far past the limit.
        await CommitAsync(dataset, Enumerable.Range(0, 40).Select(i => (Ex("x" + i), Type, Ex("C1"), (RdfTerm?)Ex("g2"), true)), ct);
        await dataset.CatchUpAsync(projection, ct);
        Position stopped = dataset.Head;
        Assert.True(projection.Stopped);
        Assert.Null(projection.Failure);
        (ByteCount limit, ByteCount reached) = projection.MemoryLimit;
        Assert.Equal(options.ClosureBytes, limit);
        Assert.True(reached.Value > limit.Value, "what it wanted, past the limit");
        Assert.Equal(InferenceReadOutcome.Unavailable, projection.TryRead(stopped, out _));

        // Below the commit that stopped it, the history is served.
        Assert.Equal(InferenceReadOutcome.Held, projection.TryRead(small, out InferredRows? rows));
        using (DatasetView view = await dataset.AsOfAsync(small, ct))
        {
            Assert.Equal(Expected(view, binding), Text(rows!, view));
        }

        // Neither a data commit that would make it fit, nor a TBox commit, nor a reset tries it again.
        await CommitAsync(dataset, Enumerable.Range(0, 40).Select(i => (Ex("x" + i), Type, Ex("C1"), (RdfTerm?)Ex("g2"), false)), ct);
        await dataset.CatchUpAsync(projection, ct);
        Assert.True(projection.Stopped);
        await CommitAsync(dataset, [(Ex("C2"), SubClassOf, Ex("C3"), TBox, true)], ct);
        await dataset.CatchUpAsync(projection, ct);
        Assert.True(projection.Stopped);
        Assert.Equal(InferenceReadOutcome.Unavailable, projection.TryRead(dataset.Head, out _));
        using (DatasetView head = dataset.Pin())
        {
            await projection.ResetAsync(head, ct);
        }

        Assert.True(projection.Stopped);
        Assert.Equal(InferenceReadOutcome.Unavailable, projection.TryRead(dataset.Head, out _));

        // Its owner's new projection, given room for what the binding holds now, builds it.
        InferenceProjection again = new(dataset, binding, new InferenceOptions { ClosureBytes = new ByteCount(needed * 4) });
        await dataset.CatchUpAsync(again, ct);
        Assert.False(again.Stopped);
        Assert.Equal(InferenceReadOutcome.Held, again.TryRead(dataset.Head, out rows));
        using (DatasetView view = await dataset.AsOfAsync(dataset.Head, ct))
        {
            Assert.Equal(Expected(view, binding), Text(rows!, view));
        }
    }

    [Fact]
    public async Task a_read_from_scratch_past_the_limit_throws_rather_than_answer_part()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await CreateAsync(ct);
        InferenceBinding binding = Binding(InferenceProfile.Rdfs);
        InferenceProjection projection = new(dataset, binding, new InferenceOptions { ClosureBytes = new ByteCount(4096) });
        await CommitAsync(dataset, [(Ex("C1"), SubClassOf, Ex("C2"), TBox, true)], ct);
        await Assert.ThrowsAsync<ClosureLimitExceededException>(async () => await projection.ReadFromScratchAsync(dataset.Head, ct));
    }
}
