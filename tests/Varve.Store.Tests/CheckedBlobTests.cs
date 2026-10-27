// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>
/// The checked envelope a projection outside the store keeps its bytes in
/// (ADR 0213): read back whole and verified, or not at all. Any byte zeroed,
/// flipped, or the blob cut there is a miss; so is an envelope bound to
/// another dataset, or to a commit the log does not hold at its position.
/// </summary>
public sealed class CheckedBlobTests
{
    [Fact]
    public async Task an_envelope_reads_back_whole_and_any_damage_is_a_miss()
    {
        MemoryStorage storage = new();
        await using Dataset dataset = await T.OpenOrCreate(storage, T.Options());

        for (int i = 0; i < 3; i++)
        {
            await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s" + i), T.Iri("p"), T.Iri("o")), T.Ct);
        }

        // Two whole blocks and a short one.
        byte[] bytes = new byte[(2 * CheckedBlob.BlockBytes) + 1234];
        new Random(213).NextBytes(bytes);
        BlobName name = new("validation/x/state-2");
        await dataset.WriteCheckedAsync(storage.Derived, name, new Position(2), bytes, T.Ct);

        CheckedContent read = (await dataset.TryReadCheckedAsync(storage.Derived, name, T.Ct))!;
        Assert.Equal(2, read.Position.Value);
        Assert.Equal(bytes, read.Bytes.ToArray());

        byte[] whole;

        using (IReadableBlob blob = await storage.Derived.OpenAsync(name, T.Ct))
        {
            whole = new byte[blob.Length.Value];
            blob.Read(new ByteOffset(0), whole);
        }

        // Every byte of the directory and the header, and every 97th of the
        // bytes, which a pull request checks a share of.
        List<int> places = [];

        for (int at = 0; at < whole.Length; at++)
        {
            if (at >= bytes.Length || at % 97 == 0)
            {
                places.Add(at);
            }
        }

        Sweep sweep = Depth.SweepOf("ENVELOPE_DAMAGE", 4);
        List<string> wrong = [];
        int checkedCases = 0;

        for (int i = sweep.First - 1; i < places.Count; i += sweep.Stride)
        {
            int at = places[i];
            byte[] zeroed = [.. whole];
            zeroed[at] = 0;
            byte[] flipped = [.. whole];
            flipped[at] ^= 0xFF;

            foreach ((byte[] damaged, string how) in new[] { (zeroed, "zeroed"), (flipped, "flipped"), (whole[..at], "cut") })
            {
                if (how == "zeroed" && whole[at] == 0)
                {
                    continue;
                }

                checkedCases++;
                await using (IBlobWriter writer = await storage.Derived.CreateAsync(name, T.Ct))
                {
                    await writer.WriteAsync(damaged, T.Ct);
                    await writer.PublishAsync(T.Ct);
                }

                if (await dataset.TryReadCheckedAsync(storage.Derived, name, T.Ct) is not null)
                {
                    wrong.Add("byte " + at + " " + how);
                }
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(sweep.Replay + ": " + checkedCases + " damaged envelopes");
        Assert.True(wrong.Count == 0, sweep.Replay + " replays this. Read back though damaged: " + string.Join(", ", wrong));
    }

    [Fact]
    public async Task an_envelope_bound_to_another_dataset_or_another_commit_is_a_miss()
    {
        MemoryStorage storage = new();
        BlobName name = new("validation/x/state-1");

        await using (Dataset dataset = await T.OpenOrCreate(storage, T.Options()))
        {
            await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Iri("o")), T.Ct);
            await dataset.WriteCheckedAsync(storage.Derived, name, new Position(1), new byte[] { 1, 2, 3 }, T.Ct);
            Assert.NotNull(await dataset.TryReadCheckedAsync(storage.Derived, name, T.Ct));
        }

        // Another log at the same position: another commit there.
        MemoryStorage other = new();
        await using Dataset another = await T.OpenOrCreate(other, T.Options());
        await another.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Iri("another")), T.Ct);
        Assert.Null(await another.TryReadCheckedAsync(storage.Derived, name, T.Ct));

        // Another dataset's id.
        MemoryStorage foreign = new();
        await using Dataset stranger = await Dataset.CreateAsync(foreign, new DatasetId(Guid.NewGuid()), T.Options(), T.Ct);
        await stranger.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Iri("o")), T.Ct);
        Assert.Null(await stranger.TryReadCheckedAsync(storage.Derived, name, T.Ct));

        // Nothing there, or a position the dataset has not reached.
        Assert.Null(await another.TryReadCheckedAsync(storage.Derived, new BlobName("validation/x/absent"), T.Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await another.WriteCheckedAsync(other.Derived, name, new Position(2), new byte[] { 1 }, T.Ct));
    }
}
