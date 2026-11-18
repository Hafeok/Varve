// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using System.Threading.Tasks;
using Varve.Store.Log;
using Varve.Store.ObjectStorage;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Store.Tests.Objects;

/// <summary>
/// The lineage record (ADR 0185): its bytes, how a record this build does
/// not read is taken, and the rule it exists for: a replica is never opened
/// for write and never claimed, and nothing is written under its prefix by
/// anything that tries.
/// </summary>
[Trait(ObjectTraits.Name, ObjectTraits.Objects)]
public sealed class LineageRecordTests
{
    private static readonly DatasetId SourceDataset = new(Guid.Parse("11111111-2222-3333-4444-555555555555"));

    private static readonly DatasetId Fleet = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

    private static LineageRecord Replica() => new()
    {
        Understood = true,
        State = LineageState.Replica,
        Source = new LineageSource(new StoreAddress("https://source.blob.core.windows.net/varve"), new ObjectName("orders/"), SourceDataset, Fleet),
    };

    private static LineageRecord Promoted() => Replica() with
    {
        State = LineageState.Promoted,
        Promoted = new LineagePromotion
        {
            Epoch = new WriterEpoch(7),
            Position = new Position(42),
            Hash = new CommitHeaderHash(new string('a', 64)),
            At = new ClaimTime(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero)),
            By = new WriterInstance("varve-2"),
        },
    };

    [Fact]
    public void a_replicas_record_is_what_the_format_says_and_reads_back()
    {
        byte[] bytes = LineageRecordFormat.Write(Replica());

        Assert.Equal(
            """
            {
              "format": 1,
              "state": "replica",
              "source": {
                "store": "https://source.blob.core.windows.net/varve",
                "prefix": "orders/",
                "dataset": "11111111-2222-3333-4444-555555555555",
                "fleet": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
              }
            }
            """.ReplaceLineEndings("\n"),
            Encoding.UTF8.GetString(bytes));

        LineageRecord read = LineageRecordFormat.Read(bytes);
        Assert.True(read.Understood);
        Assert.True(read.IsReplica);
        Assert.Equal(LineageState.Replica, read.State);
        Assert.Equal(Replica().Source, read.Source);
        Assert.Null(read.Promoted);
    }

    [Fact]
    public void a_promoted_record_keeps_what_was_promoted_and_is_no_replica()
    {
        byte[] bytes = LineageRecordFormat.Write(Promoted());

        Assert.Contains(
            "\"promoted\": {\n    \"epoch\": 7,\n    \"position\": 42,\n    \"hash\": \"" + new string('a', 64) + "\",\n    \"at\": \"2026-11-18T09:00:00Z\",\n    \"by\": \"varve-2\"\n  }",
            Encoding.UTF8.GetString(bytes),
            StringComparison.Ordinal);

        LineageRecord read = LineageRecordFormat.Read(bytes);
        Assert.False(read.IsReplica);
        Assert.Equal(Promoted().Promoted, read.Promoted);
        Assert.Equal(Promoted().Source, read.Source);
    }

    /// <summary>
    /// Read conservatively (ADR 0217): a format this build does not read,
    /// bytes that are no record, and a promoted record that names no
    /// promotion are not understood, and a record that is not understood is a
    /// replica.
    /// </summary>
    [Theory]
    [InlineData("{\"format\": 2, \"state\": \"promoted\"}")]
    [InlineData("{\"state\": \"replica\"}")]
    [InlineData("{\"format\": 1}")]
    [InlineData("{\"format\": 1, \"state\": \"promoted\", \"source\": {\"store\": \"s3://b\", \"dataset\": \"11111111-2222-3333-4444-555555555555\"}}")]
    [InlineData("{\"format\": 1, \"state\": \"replica\", \"source\": {\"store\": \"s3://b\", \"dataset\": \"not an id\"}}")]
    [InlineData("promoted")]
    [InlineData("")]
    public void a_record_this_build_does_not_read_is_a_replica(string bytes)
    {
        LineageRecord read = LineageRecordFormat.Read(Encoding.UTF8.GetBytes(bytes));

        Assert.False(read.Understood);
        Assert.True(read.IsReplica);
        Assert.Throws<ArgumentException>(() => LineageRecordFormat.Write(read));
    }

    /// <summary>A state a later version wrote is held as written, never acted on, and is a replica.</summary>
    [Fact]
    public void an_unknown_state_is_held_as_written_and_is_a_replica()
    {
        byte[] later = Encoding.UTF8.GetBytes(
            "{\"format\": 1, \"state\": \"demoted\", \"source\": {\"store\": \"s3://b\", \"dataset\": \"11111111-2222-3333-4444-555555555555\"}, \"later\": 1}");

        LineageRecord read = LineageRecordFormat.Read(later);

        Assert.True(read.Understood);
        Assert.Equal(LineageState.Unknown, read.State);
        Assert.True(read.IsReplica);
        Assert.Null(read.Source!.Prefix);
        Assert.Null(read.Source.Fleet);
        Assert.Contains("\"state\": \"demoted\"", Encoding.UTF8.GetString(LineageRecordFormat.Write(read)), StringComparison.Ordinal);
    }

    /// <summary>
    /// The rule: an open for write of a replica is refused before anything
    /// is written, the probe included; a follower opens it; and a follower's
    /// claim in place is refused the same way.
    /// </summary>
    [Fact]
    public async Task a_replica_is_never_opened_for_write_or_claimed_and_nothing_is_written_to_it()
    {
        await using ObjectHarness harness = new();
        await using (Dataset dataset = await T.OpenOrCreate(await harness.OpenAsync(), T.Options()))
        {
            await dataset.CommitAsync(new CommitRequestBuilder().Assert(T.Iri("s"), T.Iri("p"), T.Iri("o")), T.Ct);
        }

        harness.Store.Put("lineage", LineageRecordFormat.Write(Replica()));
        string[] before = [.. harness.Store.Names()];
        byte[] writer = harness.Store.Get("writer")!;
        harness.Store.ResetCounts();

        DatasetIsReplicaException refused = await Assert.ThrowsAsync<DatasetIsReplicaException>(() => harness.OpenAsync());

        Assert.Contains("is a replica of the one at https://source.blob.core.windows.net/varve under 'orders/'", refused.Message, StringComparison.Ordinal);
        Assert.Contains("varve promote", refused.Message, StringComparison.Ordinal);
        Assert.Equal(LineageState.Replica, refused.Lineage!.State);
        Assert.Equal(before, harness.Store.Names());
        Assert.Equal(writer, harness.Store.Get("writer"));
        Assert.Equal(0, harness.Store.Count(ObjectOperation.Create) + harness.Store.Count(ObjectOperation.Replace) + harness.Store.Count(ObjectOperation.Delete));

        // It opens read-only, and follows.
        Backend follower = await harness.OpenAsync(ObjectStorageAccess.ReadOnly);
        await using (Dataset reading = await Dataset.OpenFollowerAsync(follower, T.Options(), T.Ct))
        {
            Assert.Equal(new Position(1), reading.Head);
        }

        // And a follower does not claim it in place either.
        Backend standby = await harness.OpenAsync(ObjectStorageAccess.ReadOnly);
        harness.Store.ResetCounts();
        await Assert.ThrowsAsync<DatasetIsReplicaException>(async () =>
            await standby.ClaimInPlaceAsync(new WriterInstance("standby"), null, TestRandomness.Instance, ClaimTrigger.Abdication, standby.Writer!.Epoch, T.Ct));
        Assert.Equal(0, harness.Store.Count(ObjectOperation.Create) + harness.Store.Count(ObjectOperation.Replace));
        Assert.Equal(writer, harness.Store.Get("writer"));
    }

    [Fact]
    public async Task a_record_of_an_unknown_format_refuses_the_write_open_and_says_to_upgrade()
    {
        await using ObjectHarness harness = new();
        harness.Store.Put("lineage", Encoding.UTF8.GetBytes("{\"format\": 9}"));

        DatasetIsReplicaException refused = await Assert.ThrowsAsync<DatasetIsReplicaException>(() => harness.OpenAsync());

        Assert.Contains("a format this version of Varve does not read", refused.Message, StringComparison.Ordinal);
        Assert.Equal(["lineage"], harness.Store.Names());
    }

    [Fact]
    public async Task a_dataset_with_no_lineage_or_a_promoted_one_opens_for_write_and_the_record_reads_from_the_store()
    {
        await using ObjectHarness plain = new();
        Assert.Null(await Backend.ReadLineageAsync(plain.Store, null, T.Ct));
        Assert.NotNull((await plain.OpenAsync()).Epoch);

        await using ObjectHarness promoted = new();
        promoted.Store.Put("lineage", LineageRecordFormat.Write(Promoted()));
        Assert.NotNull((await promoted.OpenAsync()).Epoch);

        LineageRecord read = (await Backend.ReadLineageAsync(promoted.Store, null, T.Ct))!;
        Assert.Equal(LineageState.Promoted, read.State);
        Assert.Equal(new Position(42), read.Promoted!.Position);
    }
}
