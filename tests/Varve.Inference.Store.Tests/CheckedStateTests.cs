// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
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
/// ADR 0195 point 5: the state in the local cache, in the dataset's checked
/// envelope (ADR 0213). A loaded state continues where it was saved and
/// serves its history; another binding's state is not read; and the
/// cache-damage gate: a saved state zeroed, flipped or cut at an offset is a
/// miss, or loads and serves the closure of every position, never a wrong
/// entailment.
/// </summary>
public class CheckedStateTests
{
    private static readonly bool Long = Environment.GetEnvironmentVariable("VARVE_LONG_PROPERTIES") is not null;

    private static readonly RdfTerm Type = IriOf(RdfNs + "type");
    private static readonly RdfTerm SubClassOf = IriOf(RdfsNs + "subClassOf");
    private static readonly RdfTerm Domain = IriOf(RdfsNs + "domain");

    [Fact]
    public async Task a_loaded_state_continues_and_serves_its_history()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await CreateAsync(ct);
        InferenceBinding binding = Binding(InferenceProfile.Owl2Rl);
        InferenceProjection first = new(dataset, binding);
        await History(dataset, ct);
        await dataset.CatchUpAsync(first, ct);
        MemoryStorage cache = new();
        await first.SaveAsync(cache.Derived, ct);
        await first.SaveAsync(cache.Derived, ct);
        Assert.Single(await cache.Derived.ListAsync(ct));

        await CommitAsync(dataset, [(Ex("b"), Type, Ex("C1"), Ex("g2"), true), (Ex("C1"), SubClassOf, Ex("C2"), TBox, false)], ct);
        InferenceProjection second = new(dataset, binding);
        Assert.True(await second.LoadAsync(cache.Derived, ct));
        Assert.Equal(first.Position, second.Position);
        Assert.Equal(first.HeldFrom, second.HeldFrom);
        await dataset.CatchUpAsync(second, ct);
        await EveryPositionIsTheClosure(dataset, binding, second, ct);

        // Another binding of the same IRI, over other graphs, does not read it.
        InferenceBinding other = new(binding.Binding, binding.TBoxGraphs, GraphScope.Of([Ex("g1")], [], DefaultGraphAccess.Excluded), binding.Profile);
        Assert.False(await new InferenceProjection(dataset, other).LoadAsync(cache.Derived, ct));
    }

    [Fact]
    public async Task a_damaged_state_is_a_miss_never_a_wrong_entailment()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using Dataset dataset = await CreateAsync(ct);
        InferenceBinding binding = Binding(InferenceProfile.Rdfs);
        InferenceProjection first = new(dataset, binding);
        await History(dataset, ct);
        await dataset.CatchUpAsync(first, ct);
        MemoryStorage cache = new();
        await first.SaveAsync(cache.Derived, ct);
        BlobName name = Assert.Single(await cache.Derived.ListAsync(ct));
        byte[] bytes;
        using (IReadableBlob blob = await cache.Derived.OpenAsync(name, ct))
        {
            bytes = new byte[blob.Length.Value];
            Assert.Equal(bytes.Length, blob.Read(new ByteOffset(0), bytes));
        }

        // Every sixteenth offset on a pull request; every offset nightly (ADR 0195 point 9).
        int step = Long ? 1 : 16;
        int loaded = 0;
        for (int offset = 0; offset < bytes.Length; offset += step)
        {
            foreach (byte[] damaged in Damage(bytes, offset))
            {
                MemoryStorage storage = new();
                await using (IBlobWriter writer = await storage.Derived.CreateAsync(name, ct))
                {
                    await writer.WriteAsync(damaged, ct);
                    await writer.PublishAsync(ct);
                }

                InferenceProjection projection = new(dataset, binding);
                if (await projection.LoadAsync(storage.Derived, ct))
                {
                    loaded++;
                    await EveryPositionIsTheClosure(dataset, binding, projection, ct);
                }
                else
                {
                    Assert.Equal(new Position(0), projection.Position);
                }
            }
        }

        // The envelope holds every byte to its check, so nothing damaged loads.
        Assert.Equal(0, loaded);
    }

    private static IEnumerable<byte[]> Damage(byte[] bytes, int offset)
    {
        byte[] zeroed = [.. bytes];
        zeroed[offset] = 0;
        if (bytes[offset] != 0)
        {
            yield return zeroed;
        }

        byte[] flipped = [.. bytes];
        flipped[offset] ^= 0x5A;
        yield return flipped;
        yield return bytes[..offset];
    }

    private static async Task History(Dataset dataset, CancellationToken ct)
    {
        await CommitAsync(dataset, [(Ex("C1"), SubClassOf, Ex("C2"), TBox, true), (Ex("p1"), Domain, Ex("C3"), TBox, true)], ct);
        await CommitAsync(dataset, [(Ex("a"), Type, Ex("C1"), Ex("g1"), true), (Ex("a"), Ex("p1"), Ex("b"), null, true)], ct);
        await CommitAsync(dataset, [(Ex("C2"), SubClassOf, Ex("C3"), TBox, true)], ct);
        await CommitAsync(dataset, [(Ex("a"), Type, Ex("C1"), Ex("g1"), false), (Ex("c"), Type, Ex("C2"), Ex("g1"), true)], ct);
    }

    private static async Task EveryPositionIsTheClosure(Dataset dataset, InferenceBinding binding, InferenceProjection projection, CancellationToken ct)
    {
        for (long p = 0; p <= projection.Position.Value; p++)
        {
            using DatasetView view = await dataset.AsOfAsync(new Position(p), ct);
            InferenceReadOutcome outcome = projection.TryRead(new Position(p), out InferredRows? rows);
            if (outcome == InferenceReadOutcome.FromScratch)
            {
                rows = await projection.ReadFromScratchAsync(new Position(p), ct);
            }

            Assert.NotEqual(InferenceReadOutcome.Behind, outcome);
            Assert.True(Expected(view, binding).SetEquals(Text(rows!, view)), $"position {p} ({outcome})");
        }
    }
}
