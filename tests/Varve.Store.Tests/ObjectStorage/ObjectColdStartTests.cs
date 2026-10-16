// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CsCheck;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Varve.Store.Tests.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// A cold start in an object store (ADR 0139): an instance with an empty
/// cache reads the log only from the slot the catalog records, and serves at
/// every position — as of it, by time, and from the feed — what an instance
/// that read the whole log serves. The first read below the catalog reads
/// the log's headers once; later ones read nothing from the log.
/// </summary>
public sealed partial class ObjectColdStartTests
{
    [Fact]
    public async Task a_cold_start_reads_the_log_only_from_the_catalogs_cursor_and_serves_what_the_whole_log_serves()
    {
        int cursors = 0;

        await Gen.Select(Generators.Scripts, Gen.Int[0, 1000], Gen.Bool).SampleAsync(
            async sample =>
            {
                (Script script, int at, bool write) = sample;
                await using ObjectHarness objects = new();
                Backend storage = await objects.OpenAsync(instance: "writer");
                await using Harness harness = await Harness.StartAsync(segmentBytes: 2048, storage: storage);
                await harness.RunAsync(script);
                long head = harness.Model.Head;

                if (head == 0 || harness.Dataset.IsFailed)
                {
                    return;
                }

                await harness.Dataset.CheckpointAsync(new Position(1 + (at % head)), T.Ct);
                Served expected = await ServeAsync(harness.Dataset);

                // The cold start: a new instance, an empty cache.
                List<string> read = [];
                objects.Store.Hook = (operation, name) =>
                {
                    if (operation == ObjectOperation.Read)
                    {
                        lock (read)
                        {
                            read.Add(name.Value);
                        }
                    }

                    return null;
                };

                Backend cold = await objects.OpenAsync(write ? ObjectStorageAccess.Write : ObjectStorageAccess.ReadOnly, instance: "cold");
                await using Dataset opened = await Dataset.OpenAsync(cold, harness.Options, T.Ct);
                List<string> atOpen;

                lock (read)
                {
                    atOpen = [.. read];
                    read.Clear();
                }

                var catalog = await ExtentCatalog.TryReadAsync(cold.Derived, opened.Id, T.Ct);
                CatalogCursor cursor = Assert.IsType<CatalogCursor>(catalog?.Cursor);
                cursors++;

                foreach (string name in atOpen)
                {
                    // The cursor's segment, sealed since the catalog, is its one object.
                    if (Consolidated().Match(name) is { Success: true } whole && int.Parse(whole.Groups[1].Value, CultureInfo.InvariantCulture) < cursor.Segment)
                    {
                        Assert.Fail("The cold start read segment " + whole.Groups[1].Value + " whole; the cursor is at segment " + cursor.Segment + ".");
                    }

                    if (Chunk().Match(name) is { Success: true } chunk)
                    {
                        int segment = int.Parse(chunk.Groups[1].Value, CultureInfo.InvariantCulture);
                        int number = int.Parse(chunk.Groups[2].Value, CultureInfo.InvariantCulture);
                        Assert.True(
                            segment > cursor.Segment || (segment == cursor.Segment && number >= cursor.Slot),
                            "The cold start read chunk " + name + " below the cursor, segment " + cursor.Segment + " slot " + cursor.Slot + ".");
                    }
                }

                Assert.Equal(head, opened.Head.Value);
                await ExtentTests.EveryPositionAsync(harness, opened, "after a cold start");
                Assert.Equal(expected, await ServeAsync(opened));

                // Read once: a second pass reads nothing from the log.
                lock (read)
                {
                    read.Clear();
                }

                await ExtentTests.EveryPositionAsync(harness, opened, "after a cold start, again");
                Assert.Equal(expected, await ServeAsync(opened));

                lock (read)
                {
                    Assert.DoesNotContain(read, name => name.Contains("/log/", System.StringComparison.Ordinal) || name.StartsWith("log/", System.StringComparison.Ordinal));
                }
            },
            iter: 60,
            print: sample => sample.Item1 + " checkpoint choice " + sample.Item2 + (sample.Item3 ? " writer" : " follower"));

        Assert.True(cursors > 10, cursors + " cold starts from a cursor");
    }

    /// <summary>
    /// A sealed segment is consolidated by its seal, and its chunks deleted by
    /// the maintenance that leaves due: after any history and that
    /// maintenance, the store holds chunks of the open segment alone, and fences.
    /// </summary>
    [Fact]
    public async Task a_seal_consolidates_its_segment_so_only_the_open_segment_has_chunks()
    {
        await Generators.CommitsOnly.SampleAsync(
            async script =>
            {
                await using ObjectHarness objects = new();
                Backend storage = await objects.OpenAsync(instance: "writer");
                await using Harness harness = await Harness.StartAsync(segmentBytes: 2048, storage: storage);
                await harness.RunAsync(script);
                await harness.Dataset.MaintainAsync(T.Ct);
                Assert.False(storage.MaintenanceDue);

                IReadOnlyList<SegmentInfo> segments = await storage.Log.ListSegmentsAsync(T.Ct);

                foreach (string name in objects.Store.Names())
                {
                    if (Chunk().Match(name) is { Success: true } chunk && int.Parse(chunk.Groups[1].Value, CultureInfo.InvariantCulture) is int segment
                        && segments.Any(info => info.Id.Value == segment && info.IsSealed))
                    {
                        byte[] bytes = objects.Store.Get(name)!;
                        Assert.True(bytes.Length == Varve.Store.ObjectStorage.ChunkHeader.Size, "Chunk " + name + " of a sealed segment holds bytes after the seal consolidated it.");
                        Assert.Contains(objects.Store.Names(), other => other.EndsWith(segment.ToString("D8", CultureInfo.InvariantCulture) + ".seg", System.StringComparison.Ordinal));
                    }
                }
            },
            iter: 40);
    }

    private static async Task<Served> ServeAsync(Dataset dataset)
    {
        List<string> times = [];

        for (long q = 1; q <= dataset.Head.Value; q++)
        {
            CommitTimestamp at = dataset.TimestampAt(new Position(q));
            times.Add(q + "@" + at.Value.UtcTicks + "→" + dataset.PositionAt(at).Value + "," + dataset.PositionAt(new CommitTimestamp(at.Value.AddTicks(-1))).Value);
        }

        List<string> feed = [];
        await using IAsyncEnumerator<Commit> commits = dataset.Subscribe(new Position(0), SubscriptionFilter.All, T.Ct).GetAsyncEnumerator(T.Ct);

        for (long q = 1; q <= dataset.Head.Value && await commits.MoveNextAsync(); q++)
        {
            Commit commit = commits.Current;
            feed.Add(commit.Position.Value + " " + commit.Kind + " @" + commit.Timestamp.Value.UtcTicks + " +" + commit.Delta.Asserted.Length + " -" + commit.Delta.Retracted.Length);
        }

        return new Served(string.Join(";", times), string.Join(";", feed), dataset.Settings.DefaultAccessScope.ToString());
    }

    private sealed record Served(string Times, string Feed, string Settings);

    [GeneratedRegex(@"log/(\d{8})\.chunks/(\d{8})$")]
    private static partial Regex Chunk();

    [GeneratedRegex(@"log/(\d{8})\.seg$")]
    private static partial Regex Consolidated();
}
