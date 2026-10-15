// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Log;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The fence (ADR 0136) over generated schedules: a second writer claims the
/// epoch at any request of the first, before or after the store applies it,
/// and the first writer's every create after that is either a slot of its
/// own that already exists or the fence slot, which exists too.
/// </summary>
public sealed class ObjectEpochPropertyTests
{
    private const int Iterations = 200;

    [Fact]
    public async Task a_superseded_writer_attempts_only_its_own_slots_or_the_fence_and_appends_nothing_after_it()
    {
        Gen<(int Batches, int Trigger, bool AfterApply, int SegmentBytes)> schedules =
            Gen.Select(Gen.Int[2, 12], Gen.Int[1, 90], Gen.Bool, Gen.OneOfConst(1024, 2048, 64 << 20));

        await schedules.SampleAsync(
            async schedule =>
            {
                await using ObjectHarness harness = new();
                Backend first = await harness.OpenAsync(instance: "first");
                Dataset writer = await T.OpenOrCreate(first, T.Options(maxRecordBytes: 256, segmentBytes: schedule.SegmentBytes));

                int requests = 0;
                bool taking = false;
                Backend? second = null;
                HashSet<string>? existedAtFence = null;
                List<string> attemptsAfter = [];

                harness.Store.Hook = (operation, name) =>
                {
                    if (taking)
                    {
                        return null;
                    }

                    if (second is not null)
                    {
                        if (operation == ObjectOperation.Create)
                        {
                            attemptsAfter.Add(name.Value);
                        }

                        return null;
                    }

                    if (++requests != schedule.Trigger)
                    {
                        return null;
                    }

                    void TakeOver()
                    {
                        taking = true;
                        second = harness.OpenAsync(instance: "second").GetAwaiter().GetResult();
                        existedAtFence = [.. harness.Store.Names()];
                        taking = false;
                    }

                    if (!schedule.AfterApply)
                    {
                        TakeOver();
                        if (operation == ObjectOperation.Create)
                        {
                            attemptsAfter.Add(name.Value);
                        }

                        return null;
                    }

                    return TakeOver;
                };

                int acknowledged = 0;
                bool refused = false;

                for (int batch = 0; batch < schedule.Batches && !refused; batch++)
                {
                    try
                    {
                        CommitResult result = await writer.CommitAsync(new CommitRequest().Assert(T.Iri("s" + batch.ToString(CultureInfo.InvariantCulture) + new string('x', 60)), T.Iri("p"), T.Iri("o")), T.Ct);

                        if (result.Outcome == CommitOutcome.Committed)
                        {
                            acknowledged++;
                        }
                        else
                        {
                            refused = true;
                        }
                    }
                    catch (WriterSupersededException)
                    {
                        refused = true;
                    }
                }

                harness.Store.Hook = null;

                if (second is null)
                {
                    // The trigger fell after the writer's last request.
                    await writer.DisposeAsync();
                    return;
                }

                Assert.False(second.IsSuperseded);
                string fence = "log/" + second.Writer!.History[^1].Fence!.Value.Value;

                // The fence is about log slots. A consolidation the old writer
                // ran before it learned of the takeover is checked below: a
                // segment it sealed is final, so the object must hold exactly
                // the bytes the new writer holds for it.
                foreach (string attempt in attemptsAfter.Where(a => a.Contains(".chunks/", StringComparison.Ordinal)))
                {
                    Assert.True(
                        attempt == fence || existedAtFence!.Contains(attempt),
                        "after the takeover the superseded writer created " + attempt + ", which is neither the fence " + fence + " nor a slot that existed");
                }

                // Every acknowledged commit is in the log the new writer holds,
                // and nothing after the fence is the old writer's.
                await writer.DisposeAsync();
                await using Dataset current = await T.OpenOrCreate(second, T.Options(maxRecordBytes: 256, segmentBytes: schedule.SegmentBytes));
                Assert.Equal(acknowledged, current.Head.Value);
                CommitResult next = await current.CommitAsync(new CommitRequest().Assert(T.Iri("after"), T.Iri("p"), T.Iri("o")), T.Ct);
                Assert.Equal(CommitOutcome.Committed, next.Outcome);

                foreach (string consolidated in attemptsAfter.Where(a => a.EndsWith(".seg", StringComparison.Ordinal)).Distinct())
                {
                    int number = int.Parse(consolidated["log/".Length..^".seg".Length], System.Globalization.CultureInfo.InvariantCulture);
                    ReadOnlyMemory<byte> held = await second.Log.ReadRangeAsync(new SegmentId(number), new ByteOffset(0), new ByteCount(long.MaxValue), T.Ct);
                    Assert.True(held.Span.SequenceEqual(harness.Store.Get(consolidated)), consolidated + ", written by the superseded writer, differs from the segment the new writer holds");
                }
            },
            iter: Iterations,
            print: s => s.ToString());
    }
}
