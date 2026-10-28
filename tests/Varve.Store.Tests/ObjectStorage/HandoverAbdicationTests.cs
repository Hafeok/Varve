// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// What a writer abdicates for, and what it never does (ADR 0179 point 6):
/// - a projection fault is a function of the log, so it fails the standby at
///   the same position, and nothing a host releases the epoch for is set;
/// - a local disk fault that fails every spill keeps the writer, reports the
///   spill, and reaches the spill bound at the commit that crosses it: past
///   <see cref="Dataset.SpillBoundFactor"/> times the memtable limit, or with
///   the memory load at the GC's high-load threshold, whichever comes first;
///   the release for local-disk-error is then claimed by a standby, which
///   commits the same request.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class HandoverAbdicationTests
{
    private static DatasetOptions Options(long memtableLimit = 1_000_000) => T.Options(maxRecordBytes: 128, segmentBytes: 1024) is var options
        ? new DatasetOptions { Clock = options.Clock, MaxRecordBytes = options.MaxRecordBytes, SegmentBytes = options.SegmentBytes, CommitCache = options.CommitCache, Maintenance = MaintenanceMode.Off, MemtableLimit = new Varve.Rdf.QuadCount(memtableLimit) }
        : throw new InvalidOperationException();

    private static CommitRequestBuilder One(int n) => new CommitRequestBuilder().Assert(T.Iri("s" + n), T.Iri("p"), T.Literal("o" + n));

    [Fact]
    public async Task a_projection_fault_fails_the_standby_at_the_same_position_and_gives_no_reason_to_release()
    {
        await using ObjectHarness objects = new(waits: new InstantClock(DateTimeOffset.UnixEpoch));
        DatasetOptions writing = Options();
        Backend written = await objects.OpenAsync(instance: "writer");
        await using Dataset writer = await T.OpenOrCreate(written, writing);
        DatasetOptions standing = Options();
        await using Dataset standby = await Dataset.OpenFollowerAsync(await objects.OpenAsync(ObjectStorageAccess.ReadOnly), standing, T.Ct);
        Assert.Equal(CommitOutcome.Committed, (await writer.CommitAsync(One(1), T.Ct)).Outcome);

        // A fault of the default projection at position 2, a function of the log: on every instance alike.
        writing.DefaultProjectionFault = position => position == 2;
        standing.DefaultProjectionFault = position => position == 2;
        Assert.Equal(CommitOutcome.Committed, (await writer.CommitAsync(One(2), T.Ct)).Outcome);
        Assert.True(writer.IsFailed);
        Assert.Equal(CommitOutcome.Unavailable, (await writer.CommitAsync(One(3), T.Ct)).Outcome);

        // Nothing a host releases the epoch for: no failed append, no spill bound, no release.
        Assert.False(writer.AppendFailed);
        Assert.Equal(SpillBound.None, writer.SpillBound);
        WriterRecord record = (await Backend.ReadWriterAsync(objects.Store, objects.Prefix, T.Ct))!;
        Assert.Null(record.Released);
        Assert.Single(record.History);

        // The standby meets it at the same position, so it is not warm, and a handover would only move the fault.
        await standby.AdvanceAsync(T.Ct);
        Assert.Equal(new Position(2), standby.Head);
        Assert.True(standby.IsFailed);
        Assert.NotEqual(standby.Head, standby.ProjectionPosition);
    }

    [Fact]
    public async Task failing_spills_keep_the_writer_until_the_memtable_passes_the_bound_then_a_standby_takes_over()
    {
        const long Limit = 16;
        await using ObjectHarness objects = new(waits: new InstantClock(DateTimeOffset.UnixEpoch));
        DatasetOptions writing = Options(Limit);
        writing.SpillFault = () => true;
        writing.MemoryLoadHigh = () => false;
        Backend written = await objects.OpenAsync(instance: "writer");
        Dataset writer = await T.OpenOrCreate(written, writing);
        Backend followed = await objects.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using Dataset standby = await Dataset.OpenFollowerAsync(followed, Options(Limit), T.Ct);
        int n = 0;

        try
        {
            while (writer.SpillBound == SpillBound.None)
            {
                long before = writer.MemtableEntries.Value;
                Assert.Equal(CommitOutcome.Committed, (await writer.CommitAsync(One(++n), T.Ct)).Outcome);

                // Reached at the commit that crossed the bound, and not one before.
                Assert.Equal(writer.MemtableEntries.Value > Dataset.SpillBoundFactor * Limit && writer.SpillFailure is not null, writer.SpillBound != SpillBound.None);
                Assert.True(before <= Dataset.SpillBoundFactor * Limit || writer.SpillFailure is null);

                // Maintenance spills once the memtable is at its limit, and the disk refuses it: the writer stays.
                if (writer.MemtableEntries.Value >= Limit)
                {
                    await Assert.ThrowsAnyAsync<IOException>(async () => await writer.MaintainAsync(T.Ct));
                }

                Assert.True(n < 1000, "the bound was never reached");
            }

            Assert.Equal(SpillBound.Memtable, writer.SpillBound);
            Assert.IsType<IOException>(writer.SpillFailure);
            Assert.False(writer.AppendFailed);

            await HandOverAsync(objects, written, writer, followed, standby, n);
        }
        finally
        {
            await writer.DisposeAsync();
        }
    }

    [Fact]
    public async Task failing_spills_with_the_memory_load_at_the_threshold_release_before_the_memtable_bound()
    {
        const long Limit = 16;
        bool high = true;
        await using ObjectHarness objects = new(waits: new InstantClock(DateTimeOffset.UnixEpoch));
        DatasetOptions writing = Options(Limit);
        bool failing = false;
        writing.SpillFault = () => failing;
        writing.MemoryLoadHigh = () => high;
        Backend written = await objects.OpenAsync(instance: "writer");
        Dataset writer = await T.OpenOrCreate(written, writing);
        Backend followed = await objects.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using Dataset standby = await Dataset.OpenFollowerAsync(followed, Options(Limit), T.Ct);

        try
        {
            // The memory load alone is no reason: spills still succeed.
            for (int i = 1; i <= 20; i++)
            {
                Assert.Equal(CommitOutcome.Committed, (await writer.CommitAsync(One(i), T.Ct)).Outcome);
            }

            await writer.MaintainAsync(T.Ct);
            Assert.Null(writer.SpillFailure);
            Assert.Equal(SpillBound.None, writer.SpillBound);

            // The disk then refuses spills, the memory load still high: the next commit past the limit releases,
            // far below the memtable bound.
            failing = true;
            int n = 20;

            while (writer.SpillFailure is null)
            {
                Assert.Equal(CommitOutcome.Committed, (await writer.CommitAsync(One(++n), T.Ct)).Outcome);
                Assert.Equal(SpillBound.None, writer.SpillBound);

                try
                {
                    await writer.MaintainAsync(T.Ct);
                }
                catch (IOException)
                {
                }

                Assert.True(n < 1000, "no spill was tried");
            }

            Assert.Equal(CommitOutcome.Committed, (await writer.CommitAsync(One(++n), T.Ct)).Outcome);
            Assert.Equal(SpillBound.MemoryLoad, writer.SpillBound);
            Assert.True(writer.MemtableEntries.Value <= Dataset.SpillBoundFactor * Limit);

            // Without the high load it would not have: the arm is the GC's measure, not the count.
            high = false;
            await HandOverAsync(objects, written, writer, followed, standby, n);
        }
        finally
        {
            await writer.DisposeAsync();
        }
    }

    // The host's part (ADR 0179 points 6 and 8): the writer releases for
    // local-disk-error, a standby with its own disk claims in place, and
    // commits the same request the writer could no longer bound.
    private static async Task HandOverAsync(ObjectHarness objects, Backend written, Dataset writer, Backend followed, Dataset standby, int n)
    {
        written.ReleaseOnClose = ReleaseReason.LocalDiskError;
        await writer.DisposeAsync();
        await written.DisposeAsync();
        WriterRecord released = (await Backend.ReadWriterAsync(objects.Store, objects.Prefix, T.Ct))!;
        Assert.Equal(ReleaseReason.LocalDiskError, released.Released);

        Assert.True(await standby.TakeWriteAsync(claim => followed.ClaimInPlaceAsync(new WriterInstance("standby"), null, TestRandomness.Instance, ClaimTrigger.Abdication, released.Epoch, claim), T.Ct));
        Assert.Equal(new Position(n), standby.Head);
        CommitResult again = await standby.CommitAsync(One(n + 1), T.Ct);
        Assert.Equal(CommitOutcome.Committed, again.Outcome);
        Assert.Equal(new Position(n + 1), again.Position);

        WriterRecord record = (await Backend.ReadWriterAsync(objects.Store, objects.Prefix, T.Ct))!;
        Assert.Equal(new WriterInstance("standby"), record.Holder);
        Assert.Equal([ClaimTrigger.Restart, ClaimTrigger.Abdication], record.History.Select(claim => claim.Trigger));
        Assert.Equal(ReleaseReason.LocalDiskError, record.History[0].Released);
    }
}
