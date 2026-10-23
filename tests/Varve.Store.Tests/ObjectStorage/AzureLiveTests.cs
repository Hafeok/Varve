// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Varve.Store.Azure;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// What the emulator cannot answer about conditional writes (#143): the
/// status codes and error codes the service itself gives to the writes the
/// backend relies on, as ADR 0134 states them from Azurite's source.
/// </summary>
[Trait("Category", AzureAccount.Category)]
public sealed class AzureAccountConditionalWriteTests
{
    [Fact]
    public async Task the_service_answers_409_to_a_create_that_finds_a_blob_and_412_to_a_stale_replace()
    {
        using RecordingHandler recording = new();
        using AzureBlobStore store = AzureAccount.Open(recording);
        ObjectName prefix = AzureAccount.Prefix("conditional");

        try
        {
            ObjectName name = new(prefix.Value + "a");
            ObjectVersion v1 = Assert.IsType<ObjectVersion>(await store.CreateIfAbsentAsync(name, new byte[] { 1 }, T.Ct));
            Assert.Null(await store.CreateIfAbsentAsync(name, new byte[] { 2 }, T.Ct));
            ObjectVersion v2 = Assert.IsType<ObjectVersion>(await store.ReplaceIfUnchangedAsync(name, v1, new byte[] { 3 }, T.Ct));
            Assert.Null(await store.ReplaceIfUnchangedAsync(name, v1, new byte[] { 4 }, T.Ct));
            Assert.Null(await store.ReplaceIfUnchangedAsync(new ObjectName(prefix.Value + "none"), v2, new byte[] { 5 }, T.Ct));

            var puts = recording.Exchanges.Where(e => e.Method == "PUT").ToList();
            Assert.Equal(5, puts.Count);
            (int Status, string? Code) createFound = (puts[1].Status, puts[1].Code);
            (int Status, string? Code) staleReplace = (puts[3].Status, puts[3].Code);
            (int Status, string? Code) replaceOfNone = (puts[4].Status, puts[4].Code);
            TestContext.Current.TestOutputHelper?.WriteLine(
                "create of an existing blob: " + createFound.Status + " " + createFound.Code
                + "; replace against a stale version: " + staleReplace.Status + " " + staleReplace.Code
                + "; replace of no blob: " + replaceOfNone.Status + " " + replaceOfNone.Code
                + "; x-ms-version answered: " + puts[0].Version);

            // What the adapter maps (ADR 0134, decision 5), and what the ADR states the service does.
            Assert.Equal(201, puts[0].Status);
            Assert.Equal((409, "BlobAlreadyExists"), createFound);
            Assert.Equal((412, "ConditionNotMet"), staleReplace);
            Assert.True(replaceOfNone.Status is 404 or 412, "a replace of no blob was answered " + replaceOfNone.Status);
            Assert.Equal("2025-11-05", puts[0].Version);

            // The version a list reports is the one a read and a write report: the adapter quotes the listing's bare ETag.
            ObjectEntry listed = Assert.Single(await store.ListAsync(prefix, T.Ct));
            ObjectRead read = Assert.IsType<ObjectRead>(await store.ReadAsync(name, new ByteOffset(0), new ByteCount(10), T.Ct));
            Assert.Equal(v2, listed.Version);
            Assert.Equal(v2, read.Version);
            Assert.StartsWith("\"", v2.Value, StringComparison.Ordinal);
        }
        finally
        {
            await AzureAccount.DeleteUnderAsync(store, prefix);
        }
    }

    /// <summary>
    /// The commit path is at most once (ADR 0135): a write answered 503 is
    /// retried and lands once; a write answered 500 is not retried, because
    /// its outcome is unknown; a write whose answer is lost after the service
    /// applied it is not retried either, and the caller's own retry is told
    /// the blob exists and reads the bytes the first attempt wrote.
    /// </summary>
    [Fact]
    public async Task a_transient_fault_on_a_create_never_lands_it_twice()
    {
        using RecordingHandler recording = new();
        using AzureBlobStore cleaner = AzureAccount.Open();
        ObjectName prefix = AzureAccount.Prefix("at-most-once");

        try
        {
            // 503 with Retry-After before the request leaves: retried, one PUT reaches the service.
            await using (Faulty faulty = new((1, ObjectFault.Http503), recording))
            {
                faulty.Faults.Armed = true;
                ObjectName name = new(prefix.Value + "throttled");
                Assert.NotNull(await faulty.Store.CreateIfAbsentAsync(name, new byte[] { 1 }, T.Ct));
                Assert.True(faulty.Faults.Fired);
                Assert.Single(recording.Exchanges, e => e.Method == "PUT" && e.Path.EndsWith("/throttled", StringComparison.Ordinal));
            }

            // 500 before the request leaves: not retried, nothing reached the service, the caller is told the outcome is unknown.
            await using (Faulty faulty = new((1, ObjectFault.Http500), recording))
            {
                faulty.Faults.Armed = true;
                ObjectName name = new(prefix.Value + "failed");
                await Assert.ThrowsAsync<IOException>(async () => await faulty.Store.CreateIfAbsentAsync(name, new byte[] { 1 }, T.Ct));
                Assert.DoesNotContain(recording.Exchanges, e => e.Method == "PUT" && e.Path.EndsWith("/failed", StringComparison.Ordinal));
                faulty.Faults.Armed = false;
                Assert.NotNull(await faulty.Store.CreateIfAbsentAsync(name, new byte[] { 1 }, T.Ct));
            }

            // The answer lost after the service applied the write: not retried; the caller's retry finds it.
            await using (Faulty faulty = new((1, ObjectFault.HttpLostResponse), recording))
            {
                faulty.Faults.Armed = true;
                ObjectName name = new(prefix.Value + "lost");
                await Assert.ThrowsAsync<HttpRequestException>(async () => await faulty.Store.CreateIfAbsentAsync(name, new byte[] { 7 }, T.Ct));
                faulty.Faults.Armed = false;
                Assert.Null(await faulty.Store.CreateIfAbsentAsync(name, new byte[] { 8 }, T.Ct));
                ObjectRead read = Assert.IsType<ObjectRead>(await faulty.Store.ReadAsync(name, new ByteOffset(0), new ByteCount(10), T.Ct));
                Assert.Equal(new byte[] { 7 }, read.Bytes.ToArray());
                var puts = recording.Exchanges.Where(e => e.Method == "PUT" && e.Path.EndsWith("/lost", StringComparison.Ordinal)).ToList();
                Assert.Equal([201, 409], puts.Select(p => p.Status));
            }
        }
        finally
        {
            await AzureAccount.DeleteUnderAsync(cleaner, prefix);
        }
    }

    /// <summary>
    /// A store through a faulting handler over the test's recorder, so the
    /// test sees what reached the service. The recorder outlives each store,
    /// so the handler chain is left to the garbage collector: a
    /// <see cref="DelegatingHandler"/>'s disposal would dispose it.
    /// </summary>
    private sealed class Faulty : IAsyncDisposable
    {
        public Faulty((int At, ObjectFault Kind) fault, RecordingHandler recording)
        {
            Faults = new FaultController(fault);
            Store = AzureAccount.Open(new FaultingHandler(Faults, recording));
        }

        public FaultController Faults { get; }

        public AzureBlobStore Store { get; }

        public ValueTask DisposeAsync()
        {
            Store.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// Read-after-write on the service (ADR 0134, decision 1: reads and lists
/// see every write that has returned), from a second client with a
/// connection pool of its own.
/// </summary>
[Trait("Category", AzureAccount.Category)]
public sealed class AzureAccountReadAfterWriteTests
{
    private static int Rounds =>
        int.TryParse(Environment.GetEnvironmentVariable("VARVE_AZURE_VISIBILITY_ROUNDS"), NumberStyles.None, CultureInfo.InvariantCulture, out int rounds) && rounds > 0 ? rounds : 50;

    [Fact]
    public async Task a_blob_one_client_created_is_read_and_listed_at_once_by_another()
    {
        using AzureBlobStore writer = AzureAccount.Open();
        using AzureBlobStore reader = AzureAccount.Open();
        ObjectName prefix = AzureAccount.Prefix("visibility");

        try
        {
            int rounds = Rounds;

            for (int i = 0; i < rounds; i++)
            {
                ObjectName name = new(prefix.Value + "blob/" + i.ToString(CultureInfo.InvariantCulture));
                byte[] bytes = BitConverter.GetBytes(i);
                ObjectVersion version = Assert.IsType<ObjectVersion>(await writer.CreateIfAbsentAsync(name, bytes, T.Ct));
                ObjectRead read = Assert.IsType<ObjectRead>(await reader.ReadAsync(name, new ByteOffset(0), new ByteCount(16), T.Ct));
                Assert.Equal(bytes, read.Bytes.ToArray());
                Assert.Equal(version, read.Version);
                Assert.Contains(await reader.ListAsync(new ObjectName(prefix.Value + "blob/"), T.Ct), e => e.Name == name && e.Version == version);
            }

            TestContext.Current.TestOutputHelper?.WriteLine(rounds + " creates, each read and listed at once by a second client");
        }
        finally
        {
            await AzureAccount.DeleteUnderAsync(writer, prefix);
        }
    }

    /// <summary>A segment the writer sealed is read whole by a follower that opens right after, with the writer's head.</summary>
    [Fact]
    public async Task a_sealed_segment_is_read_at_once_by_a_follower()
    {
        using AzureBlobStore writerStore = AzureAccount.Open();
        using AzureBlobStore followerStore = AzureAccount.Open();
        ObjectName prefix = AzureAccount.Prefix("sealed");
        ObjectStorageOptions writerOptions = AzureAccount.Options(writerStore, prefix, "writer");
        ObjectStorageOptions followerOptions = AzureAccount.Options(followerStore, prefix, "follower", ObjectStorageAccess.ReadOnly);

        try
        {
            Backend writer = await Backend.OpenAsync(writerOptions, T.Ct);
            await using Dataset dataset = await T.OpenOrCreate(writer, T.Options(maxRecordBytes: 256, segmentBytes: 2048));
            int commits = 0;

            while (!(await writer.Log.ListSegmentsAsync(T.Ct)).Any(s => s.IsSealed))
            {
                Assert.True(commits < 200, "no segment sealed in 200 commits");
                await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s" + commits), T.Iri("p"), T.Literal("a literal long enough to roll a 2 KiB segment " + commits)), T.Ct);
                commits++;
            }

            (ReadOnlyMemory<byte> _, List<byte[]> written) = await T.CopyLogAsync(writer);

            Backend follower = await Backend.OpenAsync(followerOptions, T.Ct);
            (ReadOnlyMemory<byte> _, List<byte[]> seen) = await T.CopyLogAsync(follower);
            Assert.True(seen.Count >= 1);
            Assert.Equal(written[0], seen[0]);
            await using (Dataset following = await Dataset.OpenAsync(follower, T.Options(maxRecordBytes: 256, segmentBytes: 2048), T.Ct))
            {
                Assert.Equal(dataset.Head, following.Head);
            }

            await follower.DisposeAsync();
            await writer.DisposeAsync();
            TestContext.Current.TestOutputHelper?.WriteLine(commits + " commits to seal the first segment; the follower read it whole and at the writer's head");
        }
        finally
        {
            await AzureAccount.DeleteUnderAsync(writerStore, prefix);
            Directory.Delete(writerOptions.Cache.Value, recursive: true);
            Directory.Delete(followerOptions.Cache.Value, recursive: true);
        }
    }
}

/// <summary>A list of more than one page on the service, as the Azurite adapter test runs it.</summary>
[Trait("Category", AzureAccount.Category)]
public sealed class AzureAccountListingTests
{
    [Fact]
    public async Task a_list_pages_past_five_thousand_names_on_the_service()
    {
        using AzureBlobStore store = AzureAccount.Open();
        ObjectName prefix = AzureAccount.Prefix("paging");

        try
        {
            await Task.WhenAll(Enumerable.Range(0, 5100).Chunk(100).Select(async batch =>
            {
                foreach (int i in batch)
                {
                    await store.CreateIfAbsentAsync(new ObjectName(prefix.Value + i.ToString("D5", CultureInfo.InvariantCulture)), new byte[] { 1 }, T.Ct);
                }
            }));

            IReadOnlyList<ObjectEntry> all = await store.ListAsync(prefix, T.Ct);
            Assert.Equal(5100, all.Count);
            Assert.Equal(all.Select(e => e.Name.Value).Order(StringComparer.Ordinal), all.Select(e => e.Name.Value));
        }
        finally
        {
            await Task.WhenAll((await store.ListAsync(prefix, T.Ct)).Chunk(100).Select(async batch =>
            {
                foreach (ObjectEntry entry in batch)
                {
                    await store.DeleteAsync(entry.Name, T.Ct);
                }
            }));
        }
    }
}

/// <summary>
/// The race property of ADR 0134 (decision 7) at larger counts than the
/// contract suite's, with the service's answers to the losers counted:
/// <c>VARVE_AZURE_RACERS</c> clients (32 unless set) over
/// <c>VARVE_AZURE_RACE_ROUNDS</c> rounds (20 unless set).
/// </summary>
[Trait("Category", AzureAccount.Category)]
public sealed class AzureAccountRaceTests
{
    private static int Racers =>
        int.TryParse(Environment.GetEnvironmentVariable("VARVE_AZURE_RACERS"), NumberStyles.None, CultureInfo.InvariantCulture, out int racers) && racers > 1 ? racers : 32;

    private static int Rounds =>
        int.TryParse(Environment.GetEnvironmentVariable("VARVE_AZURE_RACE_ROUNDS"), NumberStyles.None, CultureInfo.InvariantCulture, out int rounds) && rounds > 0 ? rounds : 20;

    [Fact]
    public async Task of_clients_racing_create_if_absent_on_one_name_exactly_one_wins_and_the_rest_are_told_409()
    {
        using RecordingHandler recording = new();
        using AzureBlobStore store = AzureAccount.Open(recording);
        ObjectName prefix = AzureAccount.Prefix("race-create");
        int racers = Racers;
        int rounds = Rounds;

        try
        {
            for (int round = 0; round < rounds; round++)
            {
                ObjectName name = new(prefix.Value + round.ToString(CultureInfo.InvariantCulture));
                ObjectVersion?[] results = await Task.WhenAll(Enumerable.Range(0, racers).Select(i => Task.Run(async () => await store.CreateIfAbsentAsync(name, BitConverter.GetBytes(i), T.Ct), T.Ct)));
                int[] winners = [.. Enumerable.Range(0, racers).Where(i => results[i] is not null)];
                Assert.True(winners.Length == 1, "round " + round + ": " + winners.Length + " of " + racers + " racing creates were told they wrote");
                ObjectRead read = Assert.IsType<ObjectRead>(await store.ReadAsync(name, new ByteOffset(0), new ByteCount(16), T.Ct));
                Assert.Equal(BitConverter.GetBytes(winners[0]), read.Bytes.ToArray());
            }

            Report("create-if-absent", recording, rounds, racers);
        }
        finally
        {
            await AzureAccount.DeleteUnderAsync(store, prefix);
        }
    }

    [Fact]
    public async Task of_clients_racing_replace_if_unchanged_on_one_version_exactly_one_wins_and_the_rest_are_told_412()
    {
        using RecordingHandler recording = new();
        using AzureBlobStore store = AzureAccount.Open(recording);
        ObjectName prefix = AzureAccount.Prefix("race-replace");
        int racers = Racers;
        int rounds = Rounds;

        try
        {
            for (int round = 0; round < rounds; round++)
            {
                ObjectName name = new(prefix.Value + round.ToString(CultureInfo.InvariantCulture));
                ObjectVersion start = Assert.IsType<ObjectVersion>(await store.CreateIfAbsentAsync(name, new byte[] { 255 }, T.Ct));
                ObjectVersion?[] results = await Task.WhenAll(Enumerable.Range(0, racers).Select(i => Task.Run(async () => await store.ReplaceIfUnchangedAsync(name, start, BitConverter.GetBytes(i), T.Ct), T.Ct)));
                int[] winners = [.. Enumerable.Range(0, racers).Where(i => results[i] is not null)];
                Assert.True(winners.Length == 1, "round " + round + ": " + winners.Length + " of " + racers + " racing replaces were told they wrote");
                ObjectRead read = Assert.IsType<ObjectRead>(await store.ReadAsync(name, new ByteOffset(0), new ByteCount(16), T.Ct));
                Assert.Equal(BitConverter.GetBytes(winners[0]), read.Bytes.ToArray());
                Assert.Equal(results[winners[0]], read.Version);
            }

            Report("replace-if-unchanged", recording, rounds, racers);
        }
        finally
        {
            await AzureAccount.DeleteUnderAsync(store, prefix);
        }
    }

    private static void Report(string operation, RecordingHandler recording, int rounds, int racers)
    {
        var puts = recording.Exchanges.Where(e => e.Method == "PUT").ToList();
        string answers = string.Join(", ", puts.GroupBy(p => (p.Status, p.Code)).OrderBy(g => g.Key.Status).Select(g => g.Count() + "×" + g.Key.Status + " " + g.Key.Code));
        TestContext.Current.TestOutputHelper?.WriteLine(operation + ": " + rounds + " rounds × " + racers + " racers, one winner each; the service answered " + answers);
    }
}

/// <summary>
/// Commit latency from this machine, informational and never a gate: the
/// adapter's create and read of 1 KiB, and the dataset's small commit, each
/// <c>VARVE_AZURE_LATENCY_COMMITS</c> times (100 unless set), as p50, p95
/// and p99. The numbers depend on where this runs and are labelled so in
/// the report; they are not a performance claim.
/// </summary>
[Trait("Category", AzureAccount.Category)]
public sealed class AzureAccountLatencyTests
{
    private static int Commits =>
        int.TryParse(Environment.GetEnvironmentVariable("VARVE_AZURE_LATENCY_COMMITS"), NumberStyles.None, CultureInfo.InvariantCulture, out int commits) && commits > 0 ? commits : 100;

    [Fact]
    public async Task commit_latency_for_small_commits_is_measured_and_reported()
    {
        using AzureBlobStore store = AzureAccount.Open();
        ObjectName prefix = AzureAccount.Prefix("latency");
        ObjectStorageOptions options = AzureAccount.Options(store, new ObjectName(prefix.Value + "dataset/"), "latency");
        int count = Commits;
        byte[] kibibyte = new byte[1024];
        Random.Shared.NextBytes(kibibyte);

        try
        {
            List<double> puts = [];
            List<double> gets = [];
            List<double> commits = [];

            for (int i = 0; i < count; i++)
            {
                ObjectName name = new(prefix.Value + "put/" + i.ToString(CultureInfo.InvariantCulture));
                long started = Stopwatch.GetTimestamp();
                Assert.NotNull(await store.CreateIfAbsentAsync(name, kibibyte, T.Ct));
                puts.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }

            for (int i = 0; i < count; i++)
            {
                ObjectName name = new(prefix.Value + "put/" + i.ToString(CultureInfo.InvariantCulture));
                long started = Stopwatch.GetTimestamp();
                Assert.NotNull(await store.ReadAsync(name, new ByteOffset(0), new ByteCount(long.MaxValue), T.Ct));
                gets.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }

            Backend storage = await Backend.OpenAsync(options, T.Ct);
            await using (Dataset dataset = await T.OpenOrCreate(storage, T.Options()))
            {
                for (int i = 0; i < count; i++)
                {
                    long started = Stopwatch.GetTimestamp();
                    CommitResult result = await dataset.CommitAsync(new CommitRequest().Assert(T.Iri("s" + i.ToString(CultureInfo.InvariantCulture)), T.Iri("p"), T.Literal("o")), T.Ct);
                    commits.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    Assert.Equal(CommitOutcome.Committed, result.Outcome);
                }
            }

            await storage.DisposeAsync();

            TestContext.Current.TestOutputHelper?.WriteLine("endpoint " + AzureAccount.Container().Host + ", " + count + " of each, milliseconds:");
            TestContext.Current.TestOutputHelper?.WriteLine("  create-if-absent 1 KiB  " + Percentiles(puts));
            TestContext.Current.TestOutputHelper?.WriteLine("  read 1 KiB              " + Percentiles(gets));
            TestContext.Current.TestOutputHelper?.WriteLine("  commit of one quad      " + Percentiles(commits));
        }
        finally
        {
            await AzureAccount.DeleteUnderAsync(store, prefix);
            Directory.Delete(options.Cache.Value, recursive: true);
        }
    }

    private static string Percentiles(List<double> samples)
    {
        double[] sorted = [.. samples.Order()];
        return "p50 " + At(sorted, 50) + "  p95 " + At(sorted, 95) + "  p99 " + At(sorted, 99) + "  max " + sorted[^1].ToString("F0", CultureInfo.InvariantCulture);
    }

    private static string At(double[] sorted, int percentile) =>
        sorted[Math.Max(0, (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1)].ToString("F0", CultureInfo.InvariantCulture);
}
