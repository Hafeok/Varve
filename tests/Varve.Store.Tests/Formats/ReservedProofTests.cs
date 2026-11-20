// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Varve.Store.Tests.Objects;
using Xunit;

using ObjectStorageBackend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Formats;

/// <summary>
/// <c>proof/</c> is reserved in storage format 1 (#340, ADR 0216 as amended
/// 2026-11-18, <c>docs/spec/storage-format.md</c> §2.1): a dataset a release
/// wrote, with a <c>proof/</c> beside its <c>log/</c> and <c>derived/</c>
/// holding objects no build of format 1 knows, is read, written, maintained,
/// followed, shipped and restored as if <c>proof/</c> were not there, and
/// afterwards <c>proof/</c> holds exactly the fixture's objects, byte for byte,
/// and nothing else.
/// </summary>
[DesignDecision(typeof(TheFormatInventory.ProofIsReserved), Scope = ExceptionScope.Boundary)]
[DesignDecision(typeof(TheFormatInventory.ReadForeverFixturesReadAsWritten), Scope = ExceptionScope.Boundary)]
public sealed class ReservedProofTests
{
    private const string Proof = "proof";

    // Merges on, at the smallest fan-out, so that maintenance retires and
    // deletes extent sets while proof/ is there; maintenance by hand.
    private static DatasetOptions Options() => T.Options() is var options
        ? new DatasetOptions
        {
            Clock = options.Clock,
            MaxRecordBytes = options.MaxRecordBytes,
            SegmentBytes = options.SegmentBytes,
            CommitCache = options.CommitCache,
            Maintenance = MaintenanceMode.Off,
            ExtentMerges = new ExtentMergePolicy { FanOut = 2, MaxSetBytes = new ByteCount(1L << 20) },
        }
        : throw new InvalidOperationException();

    [Fact]
    public async Task on_files_proof_is_ignored_by_every_reader_and_kept_byte_for_byte()
    {
        string release = FormatFixtures.Fixture("store-preview.1");
        string reserved = Path.Combine(FormatFixtures.Fixture("proof-reserved"), Proof);
        SortedDictionary<string, byte[]> proof = FormatFixtures.Files(reserved);
        List<SortedSet<string>> expected = FormatFixtures.States(release);

        await using TemporaryDirectory directory = new(flushes: false);
        FormatFixtures.Copy(Path.Combine(release, "log"), Path.Combine(directory.Path, "log"));
        FormatFixtures.Copy(Path.Combine(release, "derived.v2"), Path.Combine(directory.Path, "derived"));
        FormatFixtures.Copy(reserved, Path.Combine(directory.Path, Proof));

        // Open (the file backend's only open is for write), read every
        // position, write, checkpoint, maintain, and ship to a target that
        // holds only a proof/ of its own.
        Position head;
        await using TemporaryDirectory shipped = new(flushes: false);
        FormatFixtures.Copy(reserved, Path.Combine(shipped.Path, Proof));

        await using (FileStorage storage = await directory.OpenForCallerAsync())
        await using (Dataset dataset = await Dataset.OpenAsync(storage, Options(), T.Ct))
        {
            Assert.Equal(expected, await FormatFixtures.ViewsAsync(dataset));
            Unchanged(proof, Path.Combine(directory.Path, Proof));

            await WriteAndMaintainAsync(dataset);
            head = dataset.Head;

            await using FileStorage target = await shipped.OpenForCallerAsync();
            Assert.Equal(head, await dataset.ShipAsync(target, head, T.Ct));
        }

        Unchanged(proof, Path.Combine(directory.Path, Proof));
        Unchanged(proof, Path.Combine(shipped.Path, Proof));
        Assert.False(Directory.Exists(Path.Combine(shipped.Path, "log", Proof)), "ShipAsync wrote under the target's log/ a name it does not own.");

        // The shipped copy opens at the head, its proof/ left alone.
        await using (FileStorage target = await shipped.OpenForCallerAsync())
        await using (Dataset replica = await Dataset.OpenAsync(target, Options(), T.Ct))
        {
            Assert.Equal(head, replica.Head);
            Assert.Equal(expected, (await FormatFixtures.ViewsAsync(replica)).Take(expected.Count));
        }

        Unchanged(proof, Path.Combine(shipped.Path, Proof));

        // Restore: a copy of the directory, opened, as the operator's guide has it.
        await using TemporaryDirectory restored = new(flushes: false);
        FormatFixtures.Copy(directory.Path, restored.Path);

        await using (FileStorage storage = await restored.OpenForCallerAsync())
        await using (Dataset dataset = await Dataset.OpenAsync(storage, Options(), T.Ct))
        {
            Assert.Equal(head, dataset.Head);
            Assert.Equal(expected, (await FormatFixtures.ViewsAsync(dataset)).Take(expected.Count));
            await WriteAndMaintainAsync(dataset);
        }

        Unchanged(proof, Path.Combine(restored.Path, Proof));
    }

    [Fact]
    public async Task in_an_object_store_proof_is_ignored_by_every_reader_and_kept_byte_for_byte()
    {
        string release = FormatFixtures.Fixture("objects-preview.15");
        string reserved = Path.Combine(FormatFixtures.Fixture("proof-reserved"), Proof);
        SortedDictionary<string, byte[]> proof = FormatFixtures.Files(reserved);
        List<SortedSet<string>> expected = FormatFixtures.States(release);

        MemoryObjectStore store = new();
        Load(store, Path.Combine(release, "objects"), string.Empty);
        Load(store, reserved, Proof + "/");

        await using ObjectHarness objects = new(store);
        Position head;

        // Open for read, read every position.
        await using (Dataset reader = await Dataset.OpenAsync(await objects.OpenAsync(ObjectStorageAccess.ReadOnly, reader: new ReaderId("proof-reader")), Options(), T.Ct))
        {
            Assert.Equal(expected, await FormatFixtures.ViewsAsync(reader));
        }

        Unchanged(proof, store);

        // Open for write and a follower: write, checkpoint, maintain the
        // projection and the log, and follow to the writer's head.
        ObjectStorageBackend written = await objects.OpenAsync();

        await using (Dataset writer = await Dataset.OpenAsync(written, Options(), T.Ct))
        await using (Dataset follower = await Dataset.OpenFollowerAsync(await objects.OpenAsync(ObjectStorageAccess.ReadOnly, reader: new ReaderId("proof-follower")), Options(), T.Ct))
        {
            Assert.Equal(expected, await FormatFixtures.ViewsAsync(writer));
            await WriteAndMaintainAsync(writer);
            await written.MaintainStorageAsync(T.Ct);
            head = writer.Head;

            await follower.AdvanceAsync(T.Ct);
            Assert.Equal(head, follower.Head);
            Assert.Equal(expected, (await FormatFixtures.ViewsAsync(follower)).Take(expected.Count));
            Unchanged(proof, store);

            // Backed up by ShipAsync into a directory, as the operator's guide
            // has it for an object store, and restored by opening it.
            await using TemporaryDirectory backup = new(flushes: false);

            await using (FileStorage target = await backup.OpenForCallerAsync())
            {
                Assert.Equal(head, await writer.ShipAsync(target, head, T.Ct));
            }

            await using FileStorage restoredStorage = await backup.OpenForCallerAsync();
            await using Dataset restored = await Dataset.OpenAsync(restoredStorage, Options(), T.Ct);
            Assert.Equal(head, restored.Head);
        }

        Unchanged(proof, store);
    }

    // Three commits, a checkpoint after each, and maintenance to a fixpoint:
    // runs flushed and merged, sets merged and the retired ones deleted.
    private static async Task WriteAndMaintainAsync(Dataset dataset)
    {
        for (int i = 0; i < 3; i++)
        {
            CommitResult result = await dataset.CommitAsync(
                new CommitRequestBuilder().Assert(T.Iri("reserved-" + dataset.Head.Value + "-" + i), T.Iri("p"), T.Literal("proof/ is not ours")), T.Ct);
            Assert.Equal(CommitOutcome.Committed, result.Outcome);
            await dataset.CheckpointAsync(dataset.Head, T.Ct);
            await dataset.MaintainAsync(T.Ct);
        }
    }

    private static void Load(MemoryObjectStore store, string directory, string prefix)
    {
        foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            store.Put(prefix + Path.GetRelativePath(directory, file).Replace('\\', '/'), File.ReadAllBytes(file));
        }
    }

    // proof/ holds the fixture's objects, byte for byte, and nothing else.
    private static void Unchanged(SortedDictionary<string, byte[]> proof, string directory)
    {
        SortedDictionary<string, byte[]> now = FormatFixtures.Files(directory);
        Assert.Equal(proof.Keys, now.Keys);

        foreach ((string name, byte[] bytes) in proof)
        {
            Assert.True(bytes.AsSpan().SequenceEqual(now[name]), "proof/" + name + " changed.");
        }
    }

    private static void Unchanged(SortedDictionary<string, byte[]> proof, MemoryObjectStore store)
    {
        string[] names = [.. store.Names().Where(n => n.StartsWith(Proof + "/", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
        Assert.Equal(proof.Keys.Select(k => Proof + "/" + k), names);

        foreach ((string name, byte[] bytes) in proof)
        {
            Assert.True(bytes.AsSpan().SequenceEqual(store.Get(Proof + "/" + name)), "proof/" + name + " changed.");
        }
    }
}
