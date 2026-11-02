// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;
using Varve.Store.ObjectStorage;
using Varve.Store.ObjectStorage.Model;
using Varve.Store.Tests.Objects;
using Xunit;

using ObjectStorageBackend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Formats;

/// <summary>
/// A writer record a later version wrote, read conservatively (ADR 0217): on
/// v0.1.0-preview.10's record, with its format or its values changed as a
/// later release could.
/// </summary>
public sealed class CoordinationRecordTests
{
    [Fact]
    [DesignDecision(typeof(CoordinationRecordsReadConservatively.AnUnknownWriterRecordFormatIsNeverWritten), Scope = ExceptionScope.Boundary)]
    public async Task an_unknown_format_is_never_written_on_and_a_follower_reads_on()
    {
        string fixture = FormatFixtures.Fixture("objects-preview.10");
        MemoryObjectStore store = Load(fixture, record => record.Replace("\"format\": 1", "\"format\": 2", StringComparison.Ordinal));
        byte[] before = store.Get("writer")!;
        string[] names = [.. store.Names()];

        WriterRecordFormatUnknownException write = await Assert.ThrowsAsync<WriterRecordFormatUnknownException>(async () =>
            await ObjectStorageBackend.OpenAsync(Options(store, ObjectStorageAccess.Write, Cache()), T.Ct));
        Assert.Equal(2, write.Format);
        Assert.Contains("neither claims the writer epoch nor takes it over", write.Message, StringComparison.Ordinal);

        await Assert.ThrowsAsync<WriterRecordFormatUnknownException>(async () =>
            await ObjectStorageBackend.TakeOverAsync(Options(store, ObjectStorageAccess.Write, Cache()), new WriterEpoch(1), T.Ct));

        // Nothing was written: the record and every object are as they were.
        Assert.Equal(before, store.Get("writer"));
        Assert.Equal(names, store.Names().Where(n => !n.StartsWith("probe/", StringComparison.Ordinal)));

        // A follower reads on, knowing no writer.
        await using ObjectStorageBackend follower = await ObjectStorageBackend.OpenAsync(Options(store, ObjectStorageAccess.ReadOnly, Cache()), T.Ct);
        await using Dataset dataset = await Dataset.OpenAsync(follower, T.Options(), T.Ct);
        Assert.Equal(FormatFixtures.States(fixture), await FormatFixtures.ViewsAsync(dataset));
    }

    [Fact]
    [DesignDecision(typeof(CoordinationRecordsReadConservatively.AnUnknownValueIsHeldNeverActedOn), Scope = ExceptionScope.Boundary)]
    public async Task an_unknown_trigger_and_reason_are_held_kept_and_never_acted_on()
    {
        string fixture = FormatFixtures.Fixture("objects-preview.10");
        MemoryObjectStore store = Load(fixture, record => record
            .Replace("\"trigger\": \"restart\"", "\"trigger\": \"rebalance\"", StringComparison.Ordinal)
            .Replace("\"released\": \"placement\"", "\"released\": \"migration\"", StringComparison.Ordinal));

        WriterRecord read = WriterRecordFormat.Read(store.Get("writer")!);
        Assert.Equal(ReleaseReason.Unknown, read.Released);
        Assert.Equal(ClaimTrigger.Unknown, read.History[0].Trigger);
        Assert.Equal(ReleaseReason.Unknown, read.History[0].Released);

        // A released epoch is claimed, and the claim keeps what it does not
        // know as it was written; no placement is inferred from it.
        await using (ObjectStorageBackend writer = await ObjectStorageBackend.OpenAsync(Options(store, ObjectStorageAccess.Write, Cache()), T.Ct))
        {
            Assert.False(writer.IsSuperseded);
        }

        string after = Encoding.UTF8.GetString(store.Get("writer")!);
        Assert.Contains("\"trigger\": \"rebalance\"", after, StringComparison.Ordinal);
        Assert.Contains("\"released\": \"migration\"", after, StringComparison.Ordinal);
        WriterRecord claimed = WriterRecordFormat.Read(store.Get("writer")!);
        Assert.Equal(new WriterEpoch(2), claimed.Epoch);
        Assert.Equal([ReleaseReason.Unknown, ReleaseReason.Shutdown], claimed.History.Select(c => c.Released));
    }

    private static ObjectStorageOptions Options(MemoryObjectStore store, ObjectStorageAccess access, string cache) => new()
    {
        Store = store,
        Cache = new CacheDirectory(cache),
        Access = access,
        Instance = access == ObjectStorageAccess.Write ? new WriterInstance("later-writer") : null,
        Reader = access == ObjectStorageAccess.ReadOnly ? new ReaderId("conservative-reader") : null,
        ProbeRandomness = TestRandomness.Instance,
        Clock = ManualClock.Epoch(),
    };

    private static string Cache() => Directory.CreateTempSubdirectory("varve-conservative-").FullName;

    private static MemoryObjectStore Load(string fixture, Func<string, string> writer)
    {
        MemoryObjectStore store = new();
        string objects = Path.Combine(fixture, "objects");

        foreach (string file in Directory.GetFiles(objects, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetRelativePath(objects, file).Replace('\\', '/');
            store.Put(name, name == "writer" ? Encoding.UTF8.GetBytes(writer(File.ReadAllText(file))) : File.ReadAllBytes(file));
        }

        return store;
    }
}
