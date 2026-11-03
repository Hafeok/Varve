// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
/// The object store's formats, from the fixtures the releases that introduced
/// them wrote (ADR 0216): the log's chunks with chunk header 1, the writer
/// record before and after <c>address</c> and <c>placement</c>, the reader
/// record before and after <c>maxDerived</c>, and derived formats 4 and 5 in
/// objects. Current code reads each, the records field by field, and writes
/// the chunks and the records as the latest fixture of their version holds
/// them.
/// </summary>
[DesignDecision(typeof(TheFormatInventory.ReadForeverFixturesReadAsWritten), Scope = ExceptionScope.Boundary)]
public sealed class ObjectFormatTests
{
    public static TheoryData<string> Releases => ["objects-preview.8", "objects-preview.10", "objects-preview.11", "objects-preview.15"];

    [Theory]
    [MemberData(nameof(Releases))]
    [DesignDecision(typeof(TheFormatInventory.DerivedFixturesReachTheSameResults), Scope = ExceptionScope.Boundary)]
    public async Task the_objects_read_at_every_position_as_the_release_wrote_them(string name)
    {
        string fixture = FormatFixtures.Fixture(name);
        MemoryObjectStore store = Load(fixture);
        using TemporaryCache cache = new();

        await using ObjectStorageBackend storage = await ObjectStorageBackend.OpenAsync(new ObjectStorageOptions
        {
            Store = store,
            Cache = new CacheDirectory(cache.Path),
            Access = ObjectStorageAccess.ReadOnly,
            Reader = new ReaderId("format-test"),
            Clock = ManualClock.Epoch(),
        }, T.Ct);
        await using Dataset dataset = await Dataset.OpenAsync(storage, T.Options(), T.Ct);
        Assert.Equal(FormatFixtures.States(fixture), await FormatFixtures.ViewsAsync(dataset));
    }

    [Theory]
    [MemberData(nameof(Releases))]
    public void the_writer_record_reads_field_by_field(string name)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(FormatFixtures.Fixture(name), "objects", "writer"));
        WriterRecord record = WriterRecordFormat.Read(bytes);
        using JsonDocument json = JsonDocument.Parse(bytes);
        JsonElement root = json.RootElement;

        Assert.Equal(root.GetProperty("epoch").GetInt64(), record.Epoch.Value);
        Assert.Equal(root.GetProperty("holder").GetString(), record.Holder.Value);
        Assert.Equal(root.GetProperty("released").GetString(), Name(record.Released));
        JsonElement[] history = [.. root.GetProperty("history").EnumerateArray()];
        Assert.Equal(history.Length, record.History.Count);

        for (int i = 0; i < history.Length; i++)
        {
            EpochClaim claim = record.History[i];
            Assert.Equal(history[i].GetProperty("epoch").GetInt64(), claim.Epoch.Value);
            Assert.Equal(history[i].GetProperty("holder").GetString(), claim.Holder.Value);
            Assert.Equal(history[i].GetProperty("trigger").GetString(), claim.Trigger.ToString().ToLowerInvariant());
            Assert.Equal(DateTimeOffset.Parse(history[i].GetProperty("at").GetString()!, System.Globalization.CultureInfo.InvariantCulture), claim.ClaimedAt.Value);
            Assert.Equal(history[i].GetProperty("fence").GetString(), claim.Fence?.Value);
            Assert.Equal(history[i].TryGetProperty("address", out JsonElement address) ? address.GetString() : null, claim.Address?.Value);
            Assert.Equal(history[i].GetProperty("released").GetString(), Name(claim.Released));
        }
    }

    [Theory]
    [InlineData("objects-preview.11", 4)]
    [InlineData("objects-preview.15", 5)]
    public void the_reader_record_reads_field_by_field(string name, int maxDerived)
    {
        string path = Path.Combine(FormatFixtures.Fixture(name), "objects", "readers", "fixture-reader");
        byte[] bytes = File.ReadAllBytes(path);
        ReaderRecord record = ReaderRecordFormat.Read(new ReaderId("fixture-reader"), bytes);
        using JsonDocument json = JsonDocument.Parse(bytes);
        JsonElement root = json.RootElement;

        Assert.Equal(root.GetProperty("reader").GetString(), record.Reader.Value);
        Assert.Equal(root.GetProperty("catalogOldest").GetInt64(), record.Oldest.Value);
        Assert.Equal(root.GetProperty("catalogNewest").GetInt64(), record.Newest.Value);
        Assert.Equal(maxDerived, record.MaxDerived.Value);
        Assert.Equal(DateTimeOffset.Parse(root.GetProperty("at").GetString()!, System.Globalization.CultureInfo.InvariantCulture), record.AnnouncedAt?.Value);
        Assert.Equal(ReaderRecordState.Announced, record.State);
    }

    [Fact]
    [DesignDecision(typeof(TheFormatInventory.AFormatChangeWithoutANewVersionFails), Scope = ExceptionScope.Boundary)]
    public async Task current_code_writes_the_chunks_and_the_records_as_the_latest_fixtures_hold_them()
    {
        MemoryObjectStore store = new();
        ManualClock clock = ManualClock.Epoch();
        using TemporaryCache writerCache = new();
        using TemporaryCache readerCache = new();
        Dictionary<string, byte[]> written;

        await using (ObjectStorageBackend storage = await ObjectStorageBackend.OpenAsync(new ObjectStorageOptions
        {
            Store = store,
            Cache = new CacheDirectory(writerCache.Path),
            Instance = new WriterInstance("fixture-writer"),
            Address = new WriterAddress("https://writer.fixture.example/"),
            Clock = clock,
            ProbeRandomness = new FixedRandomness(),
        }, T.Ct))
        {
            storage.ReleaseOnClose = ReleaseReason.Placement;
            await FormatFixtures.WriteWorkloadAsync(storage, clock);
        }

        await using (ObjectStorageBackend follower = await ObjectStorageBackend.OpenAsync(new ObjectStorageOptions
        {
            Store = store,
            Cache = new CacheDirectory(readerCache.Path),
            Access = ObjectStorageAccess.ReadOnly,
            Reader = new ReaderId("fixture-reader"),
            Clock = clock,
        }, T.Ct))
        await using (Dataset view = await Dataset.OpenAsync(follower, T.Options(), T.Ct))
        {
            written = store.Names().ToDictionary(n => n, n => store.Get(n)!, StringComparer.Ordinal);
        }

        // The log's chunks and manifest, and the writer record with address
        // and placement: chunk header 1 and writer record format 1 as
        // v0.1.0-preview.10 wrote them.
        string preview10 = Path.Combine(FormatFixtures.Fixture("objects-preview.10"), "objects");

        foreach (string file in Directory.GetFiles(Path.Combine(preview10, "log"), "*", SearchOption.AllDirectories).Append(Path.Combine(preview10, "writer")))
        {
            string objectName = Path.GetRelativePath(preview10, file).Replace('\\', '/');
            Assert.True(written.TryGetValue(objectName, out byte[]? bytes), objectName + " was not written.");
            Assert.True(File.ReadAllBytes(file).AsSpan().SequenceEqual(bytes), objectName + " differs from v0.1.0-preview.10's.");
        }

        // The reader record with maxDerived, as v0.1.0-preview.15 wrote it,
        // but for the derived format this build reads: the value is the
        // build's, the format the same (docs/formats/README.md).
        string preview15 = File.ReadAllText(Path.Combine(FormatFixtures.Fixture("objects-preview.15"), "objects", "readers", "fixture-reader"));
        Assert.Equal(
            preview15.Replace("\"maxDerived\": 5", "\"maxDerived\": " + DerivedVersion.ThisBuildReads, StringComparison.Ordinal),
            System.Text.Encoding.UTF8.GetString(written["readers/fixture-reader"]));
    }

    private static string? Name(ReleaseReason? reason) => reason switch
    {
        null => null,
        ReleaseReason.OperatorTakeover => "operator-takeover",
        { } known => known.ToString().ToLowerInvariant(),
    };

    // A store holding a fixture's objects, each named as its path under objects/.
    private static MemoryObjectStore Load(string fixture)
    {
        MemoryObjectStore store = new();
        string objects = Path.Combine(fixture, "objects");

        foreach (string file in Directory.GetFiles(objects, "*", SearchOption.AllDirectories))
        {
            store.Put(Path.GetRelativePath(objects, file).Replace('\\', '/'), File.ReadAllBytes(file));
        }

        return store;
    }

    private sealed class FixedRandomness : IProbeRandomness
    {
        public void Fill(Span<byte> destination) => destination.Fill(0x5a);
    }

    private sealed class TemporaryCache : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("varve-format-cache-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
