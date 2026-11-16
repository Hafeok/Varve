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
/// serves the history below it, and is tried again by a TBox commit.
/// </summary>
public class MemoryLimitTests
{
    private static readonly RdfTerm Type = IriOf(RdfNs + "type");
    private static readonly RdfTerm SubClassOf = IriOf(RdfsNs + "subClassOf");

    [Fact]
    public async Task a_binding_past_its_limit_stops_and_a_tbox_commit_tries_it_again()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await CreateAsync(ct);
        InferenceBinding binding = Binding(InferenceProfile.Rdfs);

        // The limit: what a small graph needs, and a little more.
        await CommitAsync(dataset, [(Ex("C1"), SubClassOf, Ex("C2"), TBox, true), (Ex("a"), Type, Ex("C1"), Ex("g1"), true)], ct);
        InferenceProjection probe = new(dataset, binding);
        await dataset.CatchUpAsync(probe, ct);
        long needed = probe.FactsHeld.Value;
        InferenceOptions options = new() { ClosureBytes = new ByteCount((needed + 20) * InferenceOptions.BytesPerFact.Value) };

        InferenceProjection projection = new(dataset, binding, options);
        await dataset.CatchUpAsync(projection, ct);
        Assert.False(projection.Stopped);
        Position small = dataset.Head;

        // Forty instances of C1 in a new graph: far past the limit.
        await CommitAsync(dataset, Enumerable.Range(0, 40).Select(i => (Ex("x" + i), Type, Ex("C1"), (RdfTerm?)Ex("g2"), true)), ct);
        await dataset.CatchUpAsync(projection, ct);
        Position stopped = dataset.Head;
        Assert.True(projection.Stopped);
        (ByteCount limit, ByteCount reached) = projection.MemoryLimit;
        Assert.Equal(options.ClosureBytes, limit);
        Assert.True(reached.Value > 0);
        Assert.Equal(InferenceReadOutcome.Unavailable, projection.TryRead(stopped, out _));

        // Below the commit that stopped it, the history is served.
        Assert.Equal(InferenceReadOutcome.Held, projection.TryRead(small, out InferredRows? rows));
        using (DatasetView view = await dataset.AsOfAsync(small, ct))
        {
            Assert.Equal(Expected(view, binding), Text(rows!, view));
        }

        // A data commit leaves it stopped; a TBox commit, once the data fits, tries it again.
        await CommitAsync(dataset, Enumerable.Range(0, 40).Select(i => (Ex("x" + i), Type, Ex("C1"), (RdfTerm?)Ex("g2"), false)), ct);
        await dataset.CatchUpAsync(projection, ct);
        Assert.True(projection.Stopped);
        Assert.Equal(InferenceReadOutcome.Unavailable, projection.TryRead(dataset.Head, out _));

        await CommitAsync(dataset, [(Ex("C2"), SubClassOf, Ex("C3"), TBox, true)], ct);
        await dataset.CatchUpAsync(projection, ct);
        Assert.False(projection.Stopped);
        Assert.Equal(InferenceReadOutcome.Held, projection.TryRead(dataset.Head, out rows));
        using (DatasetView view = await dataset.AsOfAsync(dataset.Head, ct))
        {
            Assert.Equal(Expected(view, binding), Text(rows!, view));
        }

        // Nothing was held while it was stopped: below the commit that tried it again, a read is from scratch.
        Assert.Equal(dataset.Head, projection.HeldFrom);
        Assert.Equal(InferenceReadOutcome.FromScratch, projection.TryRead(stopped, out _));
        Assert.Equal(InferenceReadOutcome.FromScratch, projection.TryRead(small, out _));
    }

    [Fact]
    public async Task a_read_from_scratch_past_the_limit_throws_rather_than_answer_part()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await CreateAsync(ct);
        InferenceBinding binding = Binding(InferenceProfile.Rdfs);
        InferenceProjection projection = new(dataset, binding, new InferenceOptions { ClosureBytes = new ByteCount(InferenceOptions.BytesPerFact.Value * 50) });
        await CommitAsync(dataset, [(Ex("C1"), SubClassOf, Ex("C2"), TBox, true)], ct);
        await Assert.ThrowsAsync<ClosureLimitExceededException>(async () => await projection.ReadFromScratchAsync(dataset.Head, ct));
    }
}
