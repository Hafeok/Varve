// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Log;
using Varve.Store.Tests.Model;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>Replica bootstrap as a copy of files (ADR 0083).</summary>
public class ReplicaTests
{
    /// <summary>
    /// For an arbitrary history, in records and segments small enough that
    /// commits span seals, a replica shipped at any closed position opens at
    /// exactly that position, equals the source read as of it — quads, terms
    /// and settings — holds a log that is a byte prefix of the source's, and
    /// continues as a dataset of its own.
    /// </summary>
    [Fact]
    public async Task a_replica_equals_the_source_at_the_shipped_position()
    {
        await Gen.Select(Generators.Scripts, Gen.Double[0, 1], Gen.Bool).SampleAsync(
            async (script, fraction, files) =>
            {
                await using Harness harness = await Harness.StartAsync(maxRecordBytes: 64, segmentBytes: 1024);
                await harness.RunAsync(script);
                Dataset source = harness.Dataset;
                Position at = new((long)Math.Floor(fraction * source.Head.Value));

                await using TemporaryDirectory directory = new();
                IStorage target = files ? await directory.OpenAsync() : new MemoryStorage();
                Assert.Equal(at, await source.ShipAsync(target, at, T.Ct));

                await using Dataset replica = await Dataset.OpenAsync(target, harness.Options, T.Ct);
                Assert.Equal(at, replica.Head);
                Assert.Equal((await source.SettingsAtAsync(at, T.Ct)).DefaultAccessScope, replica.Settings.DefaultAccessScope);

                using (DatasetView expected = await source.AsOfAsync(at, T.Ct))
                using (DatasetView actual = replica.Pin())
                {
                    Assert.Equal(T.Terms(expected), T.Terms(actual));
                }

                foreach (SegmentInfo segment in await target.Log.ListSegmentsAsync(T.Ct))
                {
                    ReadOnlyMemory<byte> copied = await target.Log.ReadRangeAsync(segment.Id, new ByteOffset(0), segment.Length, T.Ct);
                    ReadOnlyMemory<byte> original = await source.StorageForTests().Log.ReadRangeAsync(segment.Id, new ByteOffset(0), segment.Length, T.Ct);
                    Assert.True(copied.Span.SequenceEqual(original.Span));
                }

                CommitResult next = await replica.CommitAsync(new CommitRequest().Assert(T.Iri("replica"), T.Iri("p"), T.Literal("continues")), T.Ct);
                Assert.Equal(CommitOutcome.Committed, next.Outcome);
            },
            iter: 150);
    }
}
