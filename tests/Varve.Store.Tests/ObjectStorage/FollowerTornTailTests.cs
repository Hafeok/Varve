// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Varve.Store.Tests.Formats;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// #384, ADR 0164 as amended 2026-11-20: a follower opens at the last whole
/// record and never appends. The writer opens a segment in two creates, and
/// closes one in two, and a follower that opens between them once ran the
/// writer's repair of the newest segment, which its storage refuses.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class FollowerTornTailTests
{
    private static DatasetOptions Small() => T.Options(segmentBytes: 2048);

    /// <summary>The issue's reproduction: the newest segment holds its opening chunk and nothing after it.</summary>
    [Fact]
    public async Task a_follower_opens_a_log_whose_newest_segment_holds_only_its_opening_chunk()
    {
        await using ObjectHarness harness = new(waits: new InstantClock(DateTimeOffset.UnixEpoch));
        Dataset writer = await T.OpenOrCreate(await harness.OpenAsync(), Small());

        // The writer stops between the two creates that open segment 1.
        harness.Store.Hook = (operation, name) => operation == ObjectOperation.Create && name.Value == "log/00000001.chunks/00000001" ? throw new IOException("stopped") : null;
        long committed = await CommitUntilItFailsAsync(writer);
        harness.Store.Hook = null;
        Assert.Contains("log/00000001.chunks/00000000", harness.Store.Names());
        Assert.DoesNotContain("log/00000001.chunks/00000001", harness.Store.Names());

        await AssertAFollowerOpensAtAsync(harness, committed);
    }

    /// <summary>The second state: the newest segment has its trailer chunk and no seal chunk.</summary>
    [Fact]
    public async Task a_follower_opens_a_log_whose_newest_segment_has_its_trailer_and_no_seal_chunk()
    {
        // Which chunk seals segment 0, from a run nothing stops: its last one.
        string seal;

        await using (ObjectHarness whole = new(waits: new InstantClock(DateTimeOffset.UnixEpoch)))
        {
            HashSet<string> created = [];
            whole.Store.Hook = (operation, name) =>
            {
                if (operation == ObjectOperation.Create && name.Value.StartsWith("log/00000000.chunks/", StringComparison.Ordinal))
                {
                    created.Add(name.Value);
                }

                return null;
            };
            await using Dataset complete = await T.OpenOrCreate(await whole.OpenAsync(), Small());
            await CommitAsync(complete, 0, 30);
            seal = created.Max(StringComparer.Ordinal)!;
        }

        await using ObjectHarness harness = new(waits: new InstantClock(DateTimeOffset.UnixEpoch));
        Dataset writer = await T.OpenOrCreate(await harness.OpenAsync(), Small());
        harness.Store.Hook = (operation, name) => operation == ObjectOperation.Create && name.Value == seal ? throw new IOException("stopped") : null;
        long committed = await CommitUntilItFailsAsync(writer);
        harness.Store.Hook = null;
        Assert.DoesNotContain(seal, harness.Store.Names());
        Assert.DoesNotContain(harness.Store.Names(), name => name.StartsWith("log/00000001", StringComparison.Ordinal));

        await AssertAFollowerOpensAtAsync(harness, committed);
    }

    // A follower opens at the last whole record, reads what was committed, and writes nothing to the log.
    // A writer that opens afterwards repairs the segment, and the follower reads on.
    private static async Task AssertAFollowerOpensAtAsync(ObjectHarness harness, long committed)
    {
        string[] log = [.. harness.Store.Names().Where(name => name.StartsWith("log/", StringComparison.Ordinal))];
        List<string> written = [];
        harness.Store.Hook = (operation, name) =>
        {
            if (operation is ObjectOperation.Create or ObjectOperation.Replace or ObjectOperation.Delete && name.Value.StartsWith("log/", StringComparison.Ordinal))
            {
                written.Add(operation + " " + name.Value);
            }

            return null;
        };

        await using Backend storage = await harness.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using Dataset follower = await Dataset.OpenFollowerAsync(storage, Small(), T.Ct);

        Assert.Equal(committed, follower.Head.Value);
        Assert.Equal(committed + 1, (await FormatFixtures.ViewsAsync(follower)).Count);
        Assert.Empty(written);
        Assert.Equal(log, harness.Store.Names().Where(name => name.StartsWith("log/", StringComparison.Ordinal)));
        harness.Store.Hook = null;

        // An advance with nothing new changes nothing either.
        Assert.Equal(committed, (await follower.AdvanceAsync(T.Ct)).Value);

        // A writer opens again and repairs the segment, as it always has. The follower reads on.
        await using Dataset again = await T.OpenOrCreate(await harness.OpenAsync(instance: "second"), Small());
        Assert.Equal(committed, again.Head.Value);
        await CommitAsync(again, 100, 104);
        Assert.Equal(again.Head, await follower.AdvanceAsync(T.Ct));
        Assert.Equal(await FormatFixtures.ViewsAsync(again), await FormatFixtures.ViewsAsync(follower));
    }

    private static async Task<long> CommitUntilItFailsAsync(Dataset writer)
    {
        long committed = 0;

        for (int i = 0; i < 60; i++)
        {
            try
            {
                CommitResult result = await writer.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s" + i), T.Iri("p"), T.Literal(new string('x', 200) + i)), T.Ct);

                if (result.Outcome != CommitOutcome.Committed)
                {
                    return committed;
                }

                committed = writer.Head.Value;
            }
            catch (IOException)
            {
                return committed;
            }
        }

        throw new Xunit.Sdk.XunitException("Sixty commits were made and the writer never met the stopped create.");
    }

    private static async Task CommitAsync(Dataset dataset, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            await dataset.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s" + i), T.Iri("p"), T.Literal(new string('x', 200) + i)), T.Ct);
        }
    }
}
