// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Varve.Store.Log;
using Xunit;

namespace Varve.Store.Tests;

/// <summary>The commit index, derived and paged (ADR 0085).</summary>
public class CommitIndexTests
{
    private static CommitEntry Entry(long position, long ticks) =>
        new(ticks, System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(position)), new CommitLocation((int)(position / 7), position * 128), position * 3, position / 2, position * 1000);

    /// <summary>
    /// The index answers as a list of every entry does — the entry at each
    /// position, the greatest position at or before a timestamp, the settings
    /// at a position — whatever was paged out to blobs and merged, in blocks
    /// of any length against the 128 a fence covers, and through a version
    /// captured before the blobs it read were merged away and deleted.
    /// </summary>
    [Fact]
    public async Task the_index_answers_as_the_list_of_its_entries() =>
        await Gen.Select(Gen.Int[0, 3].Array[1, 700], Gen.Int[1, 300].Array[0, 12], Gen.Int[1, 5]).SampleAsync(
            async (gaps, pages, settingsEvery) =>
            {
                MemoryStorage storage = new();
                List<CommitEntry> entries = [];
                List<SettingsPoint> points = [];
                CommitIndex? latest = null;
                CommitIndex index = CommitIndex.Empty(() => latest!);
                List<CommitIndex> captured = [];
                List<CommitSegment> retired = [];
                long ticks = 1_000;
                long sequence = 0;
                int pageAt = 0;

                foreach (int gap in gaps)
                {
                    ticks += gap;
                    long p = index.Head + 1;
                    CommitEntry entry = Entry(p, ticks);
                    DatasetSettings? changed = p % (settingsEvery * 37) == 0 ? new DatasetSettings(AccessScope.AllHistory) : null;
                    index = index.Append(in entry, changed);
                    entries.Add(entry);

                    if (changed is not null)
                    {
                        points.Add(new SettingsPoint(p, changed));
                    }

                    // Now and then, the oldest entries in memory out to a blob, and a merge.
                    if (pageAt < pages.Length && index.InMemory > pages[pageAt])
                    {
                        long to = index.Head - (pages[pageAt] / 2);
                        BlobName name = CommitIndexFormat.Name(index.PagedTo, to, ++sequence);
                        await CommitIndexFormat.WriteAsync(storage.Derived, name, T.Id, index.PagedTo, to, index.Entry, T.Ct);
                        index = index.WithPaged((await CommitIndexFormat.TryLoadAsync(storage.Derived, name, T.Id, T.Ct))!);
                        captured.Add(index);

                        if (index.Segments.Length >= 2 && index.Segments[^1].Count >= index.Segments[^2].Count)
                        {
                            CommitSegment older = index.Segments[^2];
                            CommitSegment newer = index.Segments[^1];
                            BlobName merged = CommitIndexFormat.Name(older.From, newer.To, ++sequence);
                            await CommitIndexFormat.WriteAsync(storage.Derived, merged, T.Id, older.From, newer.To, q => (q <= older.To ? older : newer).Read(q), T.Ct);
                            index = index.WithMerged((await CommitIndexFormat.TryLoadAsync(storage.Derived, merged, T.Id, T.Ct))!);
                            retired.Add(older);
                            retired.Add(newer);
                        }

                        pageAt++;
                    }
                }

                latest = index;

                // Merged away and closed: the versions that read them fall back to the latest.
                foreach (CommitSegment segment in retired)
                {
                    segment.Blob.Retire(name => storage.Derived.DeleteAsync(name, T.Ct).AsTask().GetAwaiter().GetResult());
                }

                foreach (CommitIndex version in captured.Append(index))
                {
                    for (long p = 1; p <= version.Head; p++)
                    {
                        Assert.True(version.Entry(p).SameAs(entries[(int)p - 1]), "the entry at " + p);
                    }

                    for (long t = 990; t <= ticks + 2; t += 1 + (ticks / 300))
                    {
                        long expected = entries.Take((int)version.Head).Count(e => e.TimestampTicks <= t);
                        Assert.Equal(expected, version.PositionAt(t));
                    }
                }

                for (long p = 0; p <= index.Head; p++)
                {
                    DatasetSettings expected = points.LastOrDefault(s => s.Position <= p).Settings ?? DatasetSettings.Default;
                    Assert.Same(expected, index.SettingsAt(p));
                }

                Assert.True(index.Segments.Length <= 1 + (int)Math.Log2(Math.Max(1, index.PagedTo)) + 1, index.Segments.Length + " blobs for " + index.PagedTo + " entries");
            },
            iter: 200);

    /// <summary>
    /// A version held across an extension: a reader that captured the commit
    /// index and holds it resolves every position it could before, from the
    /// blobs it captured, while commits page the index out and merges replace
    /// those blobs; they are deleted only once it lets go. A reader that
    /// captured a version without holding it still resolves every position,
    /// reading what has closed from the current version.
    /// </summary>
    [Fact]
    public async Task a_version_held_across_an_extension_resolves_every_position_it_could_before()
    {
        MemoryStorage storage = new();
        ManualClock clock = ManualClock.Epoch();
        await using Dataset dataset = await Dataset.CreateAsync(
            storage, T.Id, new DatasetOptions { Clock = clock, CommitCache = 2, Maintenance = MaintenanceMode.Off }, T.Ct);

        for (int i = 0; i < 20; i++)
        {
            clock.Now = clock.Now.AddSeconds(1);
            await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s" + i), T.Iri("p"), T.Integer(i.ToString(CultureInfo.InvariantCulture))), T.Ct);
        }

        await dataset.MaintainAsync(T.Ct);
        CommitIndex held = dataset.CommitsForTests();
        CommitIndex unheld = dataset.CommitsForTests();
        Assert.True(held.Segments.Length > 0);
        Assert.True(held.TryAcquire());
        CommitEntry[] before = [.. Enumerable.Range(1, (int)held.Head).Select(p => held.Entry(p))];
        string[] blobs = [.. held.Segments.Select(s => s.Blob.Name.Value)];

        // Extended: more commits, paged out and merged, the blobs replaced.
        for (int i = 20; i < 200; i++)
        {
            clock.Now = clock.Now.AddSeconds(1);
            await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s" + i), T.Iri("p"), T.Integer(i.ToString(CultureInfo.InvariantCulture))), T.Ct);
            await dataset.MaintainAsync(T.Ct);
        }

        CommitIndex now = dataset.CommitsForTests();
        Assert.DoesNotContain(now.Segments, s => blobs.Contains(s.Blob.Name.Value));
        IReadOnlyList<BlobName> listed = await storage.Derived.ListAsync(T.Ct);
        Assert.All(blobs, name => Assert.Contains(new BlobName(name), listed));

        for (long p = 1; p <= held.Head; p++)
        {
            Assert.True(held.Entry(p).SameAs(before[p - 1]), "held, at " + p);
            Assert.True(unheld.Entry(p).SameAs(before[p - 1]), "unheld, at " + p);
            Assert.True(now.Entry(p).SameAs(before[p - 1]), "now, at " + p);
            Assert.Equal(p, held.PositionAt(before[p - 1].TimestampTicks));
        }

        // Let go: the replaced blobs are deleted by the next maintenance.
        held.Release();
        await dataset.MaintainAsync(T.Ct);
        listed = await storage.Derived.ListAsync(T.Ct);
        Assert.All(blobs, name => Assert.DoesNotContain(new BlobName(name), listed));
        Assert.True(unheld.Entry(1).SameAs(before[0]));
    }

    /// <summary>
    /// Recovery: the commit index's blobs missing, damaged, stale — written
    /// beside a log that has since diverged — or another dataset's, the
    /// dataset opens with the index the log gives: every entry, every
    /// timestamp resolved, and the reads that go through it, as a dataset
    /// whose index is whole; and the blobs it could not use are gone.
    /// </summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("damaged")]
    [InlineData("stale")]
    [InlineData("foreign")]
    public async Task an_index_missing_damaged_stale_or_foreign_is_rebuilt_from_the_log(string fault)
    {
        ManualClock clock = ManualClock.Epoch();
        DatasetOptions options = new() { Clock = clock, CommitCache = 2, Maintenance = MaintenanceMode.Off };
        MemoryStorage storage = new();
        CommitEntry[] expected;
        List<(long Position, long Ticks)> timestamps = [];

        await using (Dataset dataset = await Dataset.CreateAsync(storage, T.Id, options, T.Ct))
        {
            await CommitAsync(dataset, 0, 60);
            expected = [.. Enumerable.Range(1, 60).Select(p => dataset.CommitsForTests().Entry(p))];
        }

        IReadOnlyList<BlobName> indexBlobs = [.. (await storage.Derived.ListAsync(T.Ct)).Where(n => n.Value.StartsWith(CommitIndexFormat.Prefix, StringComparison.Ordinal))];
        Assert.NotEmpty(indexBlobs);

        switch (fault)
        {
            case "missing":
                foreach (BlobName name in indexBlobs)
                {
                    await storage.Derived.DeleteAsync(name, T.Ct);
                }

                break;

            case "damaged":
                foreach (BlobName name in indexBlobs)
                {
                    byte[] bytes = (await T.ReadBlobAsync(storage, name)).ToArray();
                    bytes[bytes.Length / 3] ^= 0x40;
                    await WriteBlobAsync(storage, name, bytes);
                }

                break;

            case "stale":
            case "foreign":
                // Another history: the same commits up to 30, then different
                // ones — under this dataset's id, or another dataset's.
                MemoryStorage other = new();
                DatasetId id = fault == "stale" ? T.Id : new DatasetId(new Guid("0badc0de-0000-4000-8000-000000000061"));
                ManualClock same = ManualClock.Epoch();

                await using (Dataset diverged = await Dataset.CreateAsync(other, id, new DatasetOptions { Clock = same, CommitCache = 2, Maintenance = MaintenanceMode.Off }, T.Ct))
                {
                    await CommitAsync(diverged, 0, 30);
                    await CommitAsync(diverged, 1000, 30);
                }

                foreach (BlobName name in indexBlobs)
                {
                    await storage.Derived.DeleteAsync(name, T.Ct);
                }

                foreach (BlobName name in await other.Derived.ListAsync(T.Ct))
                {
                    if (name.Value.StartsWith(CommitIndexFormat.Prefix, StringComparison.Ordinal))
                    {
                        await WriteBlobAsync(storage, name, (await T.ReadBlobAsync(other, name)).ToArray());
                    }
                }

                break;
        }

        string[] planted = [.. (await storage.Derived.ListAsync(T.Ct)).Where(n => n.Value.StartsWith(CommitIndexFormat.Prefix, StringComparison.Ordinal)).Select(n => n.Value)];

        await using Dataset reopened = await Dataset.OpenAsync(storage, options, T.Ct);
        CommitIndex index = reopened.CommitsForTests();
        Assert.Equal(60, index.Head);

        for (long p = 1; p <= 60; p++)
        {
            Assert.True(index.Entry(p).SameAs(expected[p - 1]), fault + ": the entry at " + p);
            Assert.Equal(new Position(p), reopened.PositionAt(new CommitTimestamp(new DateTimeOffset(expected[p - 1].TimestampTicks, TimeSpan.Zero))));
        }

        QuadDelta delta = await reopened.DiffAsync(new Position(20), new Position(50), T.Ct);
        Assert.Equal(30, delta.Asserted.Length);
        int seen = 0;

        await foreach (Commit commit in reopened.Subscribe(new Position(41), SubscriptionFilter.All, T.Ct))
        {
            Assert.Equal(42 + seen, commit.Position.Value);

            if (++seen == 19)
            {
                break;
            }
        }

        // What could not be used is gone; what is named is the index's own.
        IReadOnlyList<BlobName> after = await storage.Derived.ListAsync(T.Ct);
        string[] named = [.. index.Segments.Select(s => s.Blob.Name.Value)];
        Assert.All(after.Where(n => n.Value.StartsWith(CommitIndexFormat.Prefix, StringComparison.Ordinal)), n => Assert.Contains(n.Value, named));

        if (fault is "damaged" or "foreign")
        {
            Assert.All(planted, name => Assert.DoesNotContain(name, named));
        }
    }

    // Each commit a second after the one before, by the dataset's own clock.
    private static async Task CommitAsync(Dataset dataset, int from, int count)
    {
        ManualClock clock = (ManualClock)dataset.ClockForTests();

        for (int i = from; i < from + count; i++)
        {
            clock.Now = clock.Now.AddSeconds(1);
            await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s" + i), T.Iri("p"), T.Integer(i.ToString(CultureInfo.InvariantCulture))), T.Ct);
            await dataset.MaintainAsync(T.Ct);
        }
    }

    private static async Task WriteBlobAsync(MemoryStorage storage, BlobName name, byte[] bytes)
    {
        await using IBlobWriter writer = await storage.Derived.CreateAsync(name, T.Ct);
        await writer.WriteAsync(bytes, T.Ct);
        await writer.PublishAsync(T.Ct);
    }
}
