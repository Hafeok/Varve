// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Varve.Store.Log;
using Varve.Store.ObjectStorage;
using Varve.Store.ObjectStorage.Model;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;
using Xunit;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// Object-storage datasets over one in-memory object store: each open gets a
/// cache directory of its own, as a process would, and everything is closed
/// and deleted with the harness.
/// </summary>
internal sealed class ObjectHarness : IAsyncDisposable
{
    private readonly List<string> _caches = [];
    private readonly List<Backend> _opened = [];
    private int _instances;

    public ObjectHarness(MemoryObjectStore? store = null, ObjectName? prefix = null)
    {
        Store = store ?? new MemoryObjectStore();
        Prefix = prefix;
    }

    public MemoryObjectStore Store { get; }

    public ObjectName? Prefix { get; }

    public ManualClock Clock { get; } = ManualClock.Epoch();

    public ObjectStorageOptions Options(ObjectStorageAccess access = ObjectStorageAccess.Write, string? instance = null, long? cacheBytes = null)
    {
        string cache = Directory.CreateTempSubdirectory("varve-objects-").FullName;
        _caches.Add(cache);
        return new ObjectStorageOptions
        {
            Store = Store,
            Prefix = Prefix,
            Cache = new CacheDirectory(cache),
            Access = access,
            Instance = new WriterInstance(instance ?? "instance-" + ++_instances),
            Clock = Clock,
            ProbeRandomness = TestRandomness.Instance,
            CacheBytes = new ByteCount(cacheBytes ?? 8L << 30),
        };
    }

    public async Task<Backend> OpenAsync(ObjectStorageAccess access = ObjectStorageAccess.Write, string? instance = null, long? cacheBytes = null)
    {
        Backend storage = await Backend.OpenAsync(Options(access, instance, cacheBytes), T.Ct);
        _opened.Add(storage);
        return storage;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (Backend storage in _opened)
        {
            await storage.DisposeAsync();
        }

        foreach (string cache in _caches)
        {
            try
            {
                Directory.Delete(cache, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}

public sealed class ObjectStorageContractTests : StorageContractTests, IAsyncLifetime
{
    // Each case asks for empty storage: a store of its own each time.
    private readonly List<ObjectHarness> _harnesses = [];

    protected override IStorage Create() => throw new NotSupportedException("Opened asynchronously.");

    protected override async ValueTask<IStorage> CreateAsync()
    {
        ObjectHarness harness = new();
        _harnesses.Add(harness);
        return await harness.OpenAsync();
    }

    protected override Durability Expected => Durability.Replicated;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (ObjectHarness harness in _harnesses)
        {
            await harness.DisposeAsync();
        }
    }
}

public sealed class MemoryObjectStoreContractTests
{
    public static TheoryData<string> Cases() => [.. ObjectStoreContractCases.All().Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Cases))]
    public Task the_in_memory_object_store_keeps_the_contract(string name) =>
        ObjectStoreContractCases.All().Single(c => c.Name == name).Run(new MemoryObjectStore(), "contract/", T.Ct);
}

public sealed class ObjectStorageTests
{
    [Fact]
    public async Task a_writer_claims_the_first_epoch_and_releases_it_on_close()
    {
        await using ObjectHarness harness = new();
        Backend storage = await harness.OpenAsync(instance: "writer-a");

        Assert.Equal(new WriterEpoch(1), storage.Epoch);
        Assert.False(storage.IsSuperseded);
        WriterRecord record = Assert.IsType<WriterRecord>(await Backend.ReadWriterAsync(harness.Store, null, T.Ct));
        Assert.Equal(new WriterInstance("writer-a"), record.Holder);
        Assert.Null(record.Released);
        EpochClaim claim = Assert.Single(record.History);
        Assert.Equal(ClaimTrigger.Restart, claim.Trigger);
        Assert.Equal(new LogSlot("00000000.chunks/00000000"), claim.Fence);

        await storage.DisposeAsync();

        record = Assert.IsType<WriterRecord>(await Backend.ReadWriterAsync(harness.Store, null, T.Ct));
        Assert.Equal(ReleaseReason.Shutdown, record.Released);
        Assert.Equal(ReleaseReason.Shutdown, Assert.Single(record.History).Released);
        Assert.Null(record.History[0].Address);
    }

    /// <summary>
    /// A claim records the claimant's internal address, a close placed
    /// elsewhere releases with the reason placement, and the next claim keeps
    /// both in the history (ADRs 0168 and 0163).
    /// </summary>
    [Fact]
    public async Task a_claim_records_its_address_and_a_placement_release_is_kept()
    {
        await using ObjectHarness harness = new();
        ObjectStorageOptions basis = harness.Options(instance: "varve-0");
        ObjectStorageOptions options = new()
        {
            Store = basis.Store,
            Cache = basis.Cache,
            Clock = basis.Clock,
            Instance = basis.Instance,
            ProbeRandomness = basis.ProbeRandomness,
            Address = new WriterAddress("http://varve-0.varve-headless:8080"),
        };
        Backend first = await Backend.OpenAsync(options, T.Ct);
        first.ReleaseOnClose = ReleaseReason.Placement;
        await first.DisposeAsync();

        Backend second = await harness.OpenAsync(instance: "varve-1");
        WriterRecord record = Assert.IsType<WriterRecord>(await Backend.ReadWriterAsync(harness.Store, null, T.Ct));
        Assert.Equal(new WriterEpoch(2), record.Epoch);
        Assert.Equal(new WriterAddress("http://varve-0.varve-headless:8080"), record.History[0].Address);
        Assert.Equal(ReleaseReason.Placement, record.History[0].Released);
        Assert.Null(record.History[1].Address);
        Assert.Equal(new WriterEpoch(2), second.Epoch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://varve")]
    [InlineData("varve-writer")]
    [InlineData("http://varve writer")]
    public void a_writer_address_is_an_absolute_http_url(string value) =>
        Assert.Throws<ArgumentException>(() => new WriterAddress(value));

    [Fact]
    public async Task a_commit_is_one_create_and_reopens_from_the_chunks()
    {
        await using ObjectHarness harness = new();
        Backend storage = await harness.OpenAsync();
        await using (Dataset dataset = await T.Open(storage))
        {
            await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("first")), T.Ct);
            harness.Store.ResetCounts();

            CommitResult result = await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("second")), T.Ct);

            Assert.Equal(CommitOutcome.Committed, result.Outcome);
            Assert.Equal(1, harness.Store.Count(ObjectOperation.Create));
            Assert.Equal(1, harness.Store.Requests);
        }

        await storage.DisposeAsync();

        Backend reopened = await harness.OpenAsync();
        await using Dataset again = await T.Open(reopened);
        Assert.Equal(new Position(2), again.Head);
        using DatasetView view = again.Pin();
        Assert.Equal(2, T.All(view).Count);
    }

    [Fact]
    public async Task a_read_only_open_changes_nothing_in_the_store()
    {
        await using ObjectHarness harness = new();
        Backend writer = await harness.OpenAsync();
        await using Dataset dataset = await T.Open(writer);
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("o")), T.Ct);
        byte[] record = harness.Store.Get("writer")!;
        IReadOnlyList<string> names = harness.Store.Names();
        harness.Store.ResetCounts();

        Backend reader = await harness.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using (Dataset read = await Dataset.OpenAsync(reader, T.Options(), T.Ct))
        {
            Assert.Equal(new Position(1), read.Head);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await read.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("x")), T.Ct));
        }

        // No write at all: a follower does not probe (ADR 0134), so its
        // identity needs only read access.
        Assert.Equal(0, harness.Store.Count(ObjectOperation.Create));
        Assert.Equal(0, harness.Store.Count(ObjectOperation.Replace));
        Assert.Equal(0, harness.Store.Count(ObjectOperation.Delete));
        Assert.Equal(record, harness.Store.Get("writer"));
        Assert.Equal(names, harness.Store.Names());
        Assert.Null(reader.Epoch);
        Assert.False(reader.IsSuperseded);
    }

    [Fact]
    public async Task a_second_writer_supersedes_the_first_which_appends_nothing_more_and_keeps_its_reads()
    {
        await using ObjectHarness harness = new();
        Backend first = await harness.OpenAsync(instance: "first");
        Dataset old = await T.Open(first);
        await old.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("one")), T.Ct);
        await old.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("two")), T.Ct);

        Backend second = await harness.OpenAsync(instance: "second");
        Assert.Equal(new WriterEpoch(2), second.Epoch);

        await Assert.ThrowsAsync<WriterSupersededException>(async () => await old.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("lost")), T.Ct));
        Assert.True(first.IsSuperseded);
        Assert.Null(first.Epoch);

        using (DatasetView view = old.Pin())
        {
            Assert.Equal(2, T.All(view).Count);
        }

        await using Dataset current = await T.Open(second);
        Assert.Equal(new Position(2), current.Head);
        await current.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("three")), T.Ct);

        await old.DisposeAsync();
        await first.DisposeAsync();

        // The superseded writer released nothing: the record is the second's.
        WriterRecord record = Assert.IsType<WriterRecord>(await Backend.ReadWriterAsync(harness.Store, null, T.Ct));
        Assert.Equal(new WriterEpoch(2), record.Epoch);
        Assert.Null(record.Released);
        Assert.Equal([ClaimTrigger.Restart, ClaimTrigger.Restart], record.History.Select(c => c.Trigger));
    }

    [Fact]
    public async Task of_two_writers_claiming_at_once_exactly_one_holds_the_epoch_and_the_other_does_not_retry()
    {
        for (int round = 0; round < 20; round++)
        {
            await using ObjectHarness harness = new();

            if (round % 2 == 1)
            {
                // Rounds that race the replace of an existing record, not its creation.
                await (await harness.OpenAsync(instance: "earlier")).DisposeAsync();
            }

            // The second claim runs inside the first's read of the writer
            // record, after the store answered it: both claim from the same
            // record, and the first writes second.
            Backend? inner = null;
            harness.Store.Hook = (operation, name) =>
            {
                if (operation != ObjectOperation.Read || name.Value != "writer" || inner is not null)
                {
                    return null;
                }

                return () =>
                {
                    harness.Store.Hook = null;
                    inner = harness.OpenAsync(instance: "b").GetAwaiter().GetResult();
                };
            };

            Backend outer = await harness.OpenAsync(instance: "a");
            Backend[] opened = [outer, inner!];

            Backend winner = Assert.Single(opened, storage => storage.Epoch is not null);
            Assert.Same(inner, winner);
            Assert.True(outer.IsSuperseded);
            WriterRecord record = Assert.IsType<WriterRecord>(await Backend.ReadWriterAsync(harness.Store, null, T.Ct));
            Assert.Equal(winner.Epoch, record.Epoch);
            Assert.Equal(round % 2 == 1 ? 2 : 1, record.Epoch.Value);
        }
    }

    [Fact]
    public async Task an_operator_takeover_names_the_epoch_and_fences_the_writer_that_held_it()
    {
        await using ObjectHarness harness = new();
        Backend stuck = await harness.OpenAsync(instance: "stuck");
        await using Dataset dataset = await T.Open(stuck);
        await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("o")), T.Ct);

        Assert.Null(await Backend.TakeOverAsync(harness.Options(instance: "operator"), new WriterEpoch(7), T.Ct));
        Assert.Equal(new WriterEpoch(1), (await Backend.ReadWriterAsync(harness.Store, null, T.Ct))!.Epoch);

        WriterRecord taken = Assert.IsType<WriterRecord>(await Backend.TakeOverAsync(harness.Options(instance: "operator"), new WriterEpoch(1), T.Ct));
        Assert.Equal(new WriterEpoch(2), taken.Epoch);
        Assert.Equal(ReleaseReason.OperatorTakeover, taken.Released);
        Assert.Equal(ClaimTrigger.Operator, taken.History[^1].Trigger);

        await Assert.ThrowsAsync<WriterSupersededException>(async () => await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s"), T.Iri("p"), T.Literal("after")), T.Ct));

        Backend next = await harness.OpenAsync(instance: "next");
        Assert.Equal(new WriterEpoch(3), next.Epoch);
        await using Dataset resumed = await T.Open(next);
        Assert.Equal(new Position(1), resumed.Head);
    }

    /// <summary>
    /// A seal consolidates its segment at once (ADR 0135); one whose
    /// consolidation failed — here every second segment's, the store
    /// unreachable for its upload — is left to maintenance, which does it.
    /// </summary>
    [Fact]
    public async Task a_sealed_segment_is_consolidated_byte_for_byte_and_only_fence_chunks_remain()
    {
        await using ObjectHarness harness = new();
        Backend storage = await harness.OpenAsync();
        List<byte[]> before;
        int failed = 0;
        harness.Store.Hook = (operation, name) =>
            operation == ObjectOperation.Create && name.Value.EndsWith(".seg", StringComparison.Ordinal) && int.Parse(name.Value[^12..^4], System.Globalization.CultureInfo.InvariantCulture) % 2 == 1 && ++failed > 0
                ? throw new IOException("The store is unreachable.")
                : null;

        DatasetOptions small = T.Options(segmentBytes: 2048, maxRecordBytes: 256);
        DatasetOptions options = new()
        {
            Clock = small.Clock,
            MaxRecordBytes = small.MaxRecordBytes,
            SegmentBytes = small.SegmentBytes,
            Maintenance = MaintenanceMode.Off,
            CommitCache = small.CommitCache,
        };

        await using (Dataset dataset = await T.OpenOrCreate(storage, options))
        {
            for (int i = 0; i < 40; i++)
            {
                await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s" + i), T.Iri("p"), T.Literal("a literal long enough to fill segments " + i)), T.Ct);
            }

            before = (await T.CopyLogAsync(storage)).Segments;
            Assert.True(before.Count > 3);
            Assert.True(failed > 0);
            Assert.True(storage.MaintenanceDue);
            harness.Store.Hook = null;
            await dataset.MaintainAsync(T.Ct);
            Assert.False(storage.MaintenanceDue);
        }

        IReadOnlyList<string> names = harness.Store.Names();

        for (int segment = 0; segment < before.Count - 1; segment++)
        {
            string number = segment.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(before[segment], harness.Store.Get("log/" + number + ".seg"));

            // Only fences are left of a consolidated segment's chunks.
            foreach (string chunk in names.Where(n => n.StartsWith("log/" + number + ".chunks/", StringComparison.Ordinal)))
            {
                Assert.True(harness.Store.Get(chunk)!.Length == 136 && (harness.Store.Get(chunk)![5] & 1) == 1, chunk + " is not a fence");
            }
        }

        await storage.DisposeAsync();
        Backend reopened = await harness.OpenAsync();
        Assert.Equal(before, (await T.CopyLogAsync(reopened)).Segments);
        await using Dataset again = await T.OpenOrCreate(reopened, T.Options(segmentBytes: 2048, maxRecordBytes: 256));
        Assert.Equal(new Position(40), again.Head);
    }

    /// <summary>
    /// A store that ignores the conditions of its conditional writes — a
    /// create that overwrites, a replace against any version, as Garage's —
    /// is refused at a writer's open by the probe under <c>probe/</c>, with a
    /// message that names the store; nothing of a dataset is written to it,
    /// and the probe's key is gone (ADR 0134). A follower, which never writes
    /// the log, does not probe and writes nothing. A store that enforces them
    /// is probed and keeps no probe key.
    /// </summary>
    [Fact]
    public async Task a_store_that_does_not_enforce_conditional_writes_is_refused_at_a_writers_open()
    {
        await using (ObjectHarness harness = new(new MemoryObjectStore { IgnoresConditions = true }))
        {
            ConditionalWritesNotEnforcedException refused = await Assert.ThrowsAsync<ConditionalWritesNotEnforcedException>(() => harness.OpenAsync());
            Assert.Contains("memory://objects", refused.Message, StringComparison.Ordinal);
            Assert.Contains("does not enforce conditional writes", refused.Message, StringComparison.Ordinal);
            Assert.Contains("ADR 0134", refused.Message, StringComparison.Ordinal);
            Assert.Empty(harness.Store.Names());

            harness.Store.ResetCounts();
            Backend follower = await harness.OpenAsync(ObjectStorageAccess.ReadOnly);
            Assert.Equal(0, harness.Store.Count(ObjectOperation.Create) + harness.Store.Count(ObjectOperation.Replace) + harness.Store.Count(ObjectOperation.Delete));
            Assert.Null(follower.Epoch);
        }

        // Only a stale replace let through is enough.
        await using (ObjectHarness staleOnly = new())
        {
            staleOnly.Store.Hook = (operation, name) =>
            {
                if (operation == ObjectOperation.Replace && name.Value.Contains("probe/", StringComparison.Ordinal))
                {
                    staleOnly.Store.IgnoresConditions = true;
                }

                return null;
            };
            await Assert.ThrowsAsync<ConditionalWritesNotEnforcedException>(() => staleOnly.OpenAsync());
        }

        await using ObjectHarness enforcing = new();
        Backend storage = await enforcing.OpenAsync();
        Assert.DoesNotContain(enforcing.Store.Names(), name => name.StartsWith("probe/", StringComparison.Ordinal));
        await using Dataset dataset = await T.OpenOrCreate(storage, T.Options());
        Assert.Equal(new Position(0), dataset.Head);
    }
}
