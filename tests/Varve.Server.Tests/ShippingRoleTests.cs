// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Varve.Management;
using Varve.Management.Model;
using Varve.Protocol.Model;
using Varve.Store;
using Varve.Store.Log;
using Varve.Store.ObjectStorage;
using Varve.Store.ObjectStorage.Model;
using Varve.Store.Tests.Objects;
using Xunit;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Server.Tests;

/// <summary>
/// The shipping role (ADR 0184 decisions 2, 9 and 11): a fleet in one
/// process over an in-memory source store, shipping a declared dataset to an
/// in-memory target store.
/// </summary>
public sealed class ShippingRoleTests
{
    private const string TargetAddress = "https://replica.invalid/copies";

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task the_writer_ships_a_declared_target_and_reports_how_far_it_got()
    {
        await using Scene scene = await Scene.StartAsync(verified: true);
        Dataset dataset = await scene.DeclareAsync();
        await CommitAsync(dataset, 0, 5);

        ShippingStatus status = await scene.WaitForAsync(s => s.State == ShippingState.Shipping && s.ShippedThrough == dataset.Head.Value);

        Assert.Equal(TargetAddress + "/orders", status.Target);
        Assert.Equal(0, status.Failures);
        Assert.Null(status.CatalogPosition);
        Assert.Contains("orders/log/MANIFEST", scene.Target.Names());
        Assert.Contains("orders/lineage", scene.Target.Names());

        // The lineage names the source and the fleet, and a follower of the copy reads the source's head.
        LineageRecord lineage = (await Backend.ReadLineageAsync(scene.Target, new ObjectName("orders/"), Ct))!;
        Assert.True(lineage.IsReplica);
        Assert.Equal(dataset.Id, lineage.Source!.Dataset);
        Assert.Equal(scene.Fleet.System.Id, lineage.Source!.Fleet);
        Assert.Equal("https://fleet.invalid/source", lineage.Source!.Store.Value);

        // A checkpoint moves the catalog, and the next round ships it and reports its position.
        await dataset.CheckpointAsync(dataset.Head, Ct);
        status = await scene.WaitForAsync(s => s.CatalogPosition == dataset.Head.Value);
        Assert.Equal(scene.Source.Get("orders/derived/catalog"), scene.Target.Get("orders/derived/catalog"));

        // Nothing moved: the shipper makes no request of either store.
        await Task.Delay(Interval * 5, Ct);
        scene.Source.ResetCounts();
        scene.Target.ResetCounts();
        await Task.Delay(Interval * 10, Ct);
        Assert.Equal(0, Requests(scene.Target));
        Assert.Equal(0, scene.Source.Count(ObjectOperation.List));
    }

    [Fact]
    public async Task an_unverified_target_is_reported_and_written_to_only_once_it_is_verified()
    {
        await using Scene scene = await Scene.StartAsync(verified: false);
        Dataset dataset = await scene.DeclareAsync();
        await CommitAsync(dataset, 0, 3);

        ShippingStatus refused = await scene.WaitForAsync(s => s.State == ShippingState.StoreUnverified);

        Assert.Contains("varve storage verify", refused.Detail, StringComparison.Ordinal);
        Assert.True(refused.Failures > 0);
        Assert.Empty(scene.Target.Names());

        // The dataset is not affected: it still commits.
        await CommitAsync(dataset, 3, 5);

        await scene.VerifyAsync();
        ShippingStatus shipped = await scene.WaitForAsync(s => s.State == ShippingState.Shipping && s.ShippedThrough == dataset.Head.Value);
        Assert.Equal(0, shipped.Failures);
    }

    /// <summary>
    /// ADR 0184 decisions 5 and 6, the architect's points A and B: what does
    /// not verify on the source is not shipped, and readiness says which. A
    /// damaged chunk is <c>source-chunk-damaged</c>; a damaged blob of a set
    /// is <c>waiting-on-damaged-set</c>, with the log shipped. Both are read
    /// again each round, and shipping carries on once the source reads whole.
    /// </summary>
    [Fact]
    public async Task readiness_names_a_damaged_chunk_and_a_damaged_set_and_shipping_carries_on_after()
    {
        await using Scene scene = await Scene.StartAsync(verified: true);
        Dataset dataset = await scene.DeclareAsync();
        await CommitAsync(dataset, 0, 2);
        await scene.WaitForAsync(s => s.State == ShippingState.Shipping && s.ShippedThrough == dataset.Head.Value);

        // The target refuses new chunks while the source is damaged, so the shipper meets the damage and not a race
        // with it. It refuses before it applies them: a create applied with its answer lost would put the whole
        // chunk there. Only chunks: a round's other writes to the target come before it reads the log.
        scene.Target.Hook = (operation, name) => operation == ObjectOperation.Create && name.Value.Contains(".chunks/", StringComparison.Ordinal) ? throw new IOException("the target is held back") : null;
        HashSet<string> had = [.. scene.Source.Names()];
        await CommitAsync(dataset, 2, 3);
        string chunk = scene.Source.Names().First(name => !had.Contains(name) && name.Contains(".chunks/", StringComparison.Ordinal));
        byte[] whole = scene.Source.Get(chunk)!;
        byte[] damaged = [.. whole];
        damaged[^1] ^= 0x01;
        scene.Source.Put(chunk, damaged);

        // The target stays held back until the shipper has reported the damage. A round that read the chunk whole,
        // before it was damaged, may still be on its way to the create: released now, it would put the whole chunk
        // there. The check of a source chunk comes before any write to the target, so the report needs none.
        ShippingStatus status = await scene.WaitForAsync(s => s.State == ShippingState.SourceChunkDamaged);
        scene.Target.Hook = null;
        Assert.Contains("does not verify", status.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(chunk, scene.Target.Names());
        scene.Source.Put(chunk, whole);
        await scene.WaitForAsync(s => s.State == ShippingState.Shipping && s.ShippedThrough == dataset.Head.Value);

        // A set, damaged before it is copied. The target refuses that one blob until the shipper has reported the
        // damage, for the same reason: a round that staged it whole must not get to create it.
        scene.Target.Hook = (operation, name) => operation == ObjectOperation.Create && name.Value.EndsWith("/terms", StringComparison.Ordinal) ? throw new IOException("the target is held back") : null;
        await dataset.CheckpointAsync(dataset.Head, Ct);
        string blob = scene.Source.Names().First(name => name.StartsWith("orders/derived/extents/", StringComparison.Ordinal) && name.EndsWith("/terms", StringComparison.Ordinal));
        whole = scene.Source.Get(blob)!;
        damaged = [.. whole];
        damaged[damaged.Length / 2] ^= 0x01;
        scene.Source.Put(blob, damaged);

        status = await scene.WaitForAsync(s => s.State == ShippingState.WaitingOnDamagedSet);
        scene.Target.Hook = null;
        Assert.Contains(blob["orders/derived/".Length..], status.Detail, StringComparison.Ordinal);
        Assert.Equal(dataset.Head.Value, status.ShippedThrough);
        Assert.DoesNotContain(blob, scene.Target.Names());
        Assert.DoesNotContain("orders/derived/catalog", scene.Target.Names());

        scene.Source.Put(blob, whole);
        await scene.WaitForAsync(s => s.State == ShippingState.Shipping && s.CatalogPosition == dataset.Head.Value);
        Assert.Equal(whole, scene.Target.Get(blob));
    }

    [Fact]
    public async Task a_commit_waits_for_no_target_while_the_target_is_stalled()
    {
        await using Scene scene = await Scene.StartAsync(verified: true);
        Dataset dataset = await scene.DeclareAsync();
        await CommitAsync(dataset, 0, 2);
        await scene.WaitForAsync(s => s.State == ShippingState.Shipping && s.ShippedThrough == dataset.Head.Value);

        // The target stops answering: every request of it blocks until released.
        using ManualResetEventSlim released = new(false);
        scene.Target.Hook = (_, _) =>
        {
            released.Wait(Ct);
            return null;
        };

        try
        {
            long before = dataset.Head.Value;
            Stopwatch watch = Stopwatch.StartNew();
            await CommitAsync(dataset, 2, 12);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "Ten commits took " + watch.Elapsed + " with the target stalled.");
            Assert.Equal(before + 10, dataset.Head.Value);
            Assert.True(scene.Status().ShippedThrough < dataset.Head.Value);
        }
        finally
        {
            scene.Target.Hook = null;
            released.Set();
        }

        await scene.WaitForAsync(s => s.State == ShippingState.Shipping && s.ShippedThrough == dataset.Head.Value);
    }

    [Fact]
    public async Task a_target_that_holds_another_dataset_stops_its_shipper_and_nothing_is_written()
    {
        await using Scene scene = await Scene.StartAsync(verified: true);
        scene.Target.Put("orders/log/MANIFEST", Encoding.UTF8.GetBytes("another dataset's manifest"));
        string[] before = [.. scene.Target.Names()];
        Dataset dataset = await scene.DeclareAsync();
        await CommitAsync(dataset, 0, 2);

        ShippingStatus stopped = await scene.WaitForAsync(s => s.State == ShippingState.TargetNotEmpty);

        Assert.Contains("ADR 0184", stopped.Detail, StringComparison.Ordinal);
        Assert.Equal(before, scene.Target.Names());
        await CommitAsync(dataset, 2, 4);
    }

    /// <summary>Readiness's <c>shipping</c> member (decision 11): the tail and the catalog's position side by side, and a failure's reason.</summary>
    [Fact]
    public void readiness_reports_each_target_with_its_tail_its_catalog_position_and_its_failure()
    {
        using MemoryStream stream = new();

        using (System.Text.Json.Utf8JsonWriter json = new(stream))
        {
            json.WriteStartObject();
            Shipping.Write(json,
            [
                new ShippingStatus("https://a.invalid/copies/orders", ShippingState.Shipping, 40, 32, 0, null),
                new ShippingStatus("s3://replicas/orders", ShippingState.StoreUnverified, null, null, 3, "Run 'varve storage verify'."),
            ], 42);
            json.WriteEndObject();
        }

        Assert.Equal(
            """{"shipping":{"https://a.invalid/copies/orders":{"state":"shipping","shippedThrough":40,"lag":2,"catalogPosition":32},"s3://replicas/orders":{"state":"store-unverified","failures":3,"detail":"Run \u0027varve storage verify\u0027."}}}""",
            Encoding.UTF8.GetString(stream.ToArray()));

        using MemoryStream none = new();

        using (System.Text.Json.Utf8JsonWriter json = new(none))
        {
            json.WriteStartObject();
            Shipping.Write(json, [], 42);
            json.WriteEndObject();
        }

        Assert.Equal("{}", Encoding.UTF8.GetString(none.ToArray()));
    }

    /// <summary>
    /// ADR 0184 decision 13 and ADR 0185 decision 8 (the maintainer's answer
    /// D3 on #166): the source fleet's system dataset is shipped to
    /// <c>fleets/{fleet id}/</c> on the target's store, and it is there
    /// before any chunk of a dataset that refers to it.
    /// </summary>
    [Fact]
    public async Task the_fleets_system_dataset_is_shipped_ahead_of_the_dataset_commits_that_refer_to_it()
    {
        await using Scene scene = await Scene.StartAsync(verified: true);
        string fleets = "fleets/" + scene.Fleet.System.Id.Value.ToString("D") + "/";

        // Every create of a chunk of the dataset on the target: the system dataset's copy must already
        // hold every chunk the source's system dataset held when the chunk was about to be written.
        List<string> behind = [];
        scene.Target.Hook = (operation, name) =>
        {
            if (operation == ObjectOperation.Create && name.Value.StartsWith("orders/log/", StringComparison.Ordinal) && name.Value.Contains(".chunks/", StringComparison.Ordinal))
            {
                foreach (string source in scene.Source.Names().Where(n => n.StartsWith("_fleet/log/", StringComparison.Ordinal) && n.Contains(".chunks/", StringComparison.Ordinal)))
                {
                    if (scene.Target.Get(fleets + source["_fleet/".Length..]) is null)
                    {
                        behind.Add(name.Value + " was created before " + source);
                    }
                }
            }

            return null;
        };

        Dataset dataset = await scene.DeclareAsync();
        await CommitAsync(dataset, 0, 3);
        await scene.WaitForAsync(s => s.State == ShippingState.Shipping && s.ShippedThrough == dataset.Head.Value);
        scene.Target.Hook = null;

        Assert.Empty(behind);
        Assert.Contains(fleets + "log/MANIFEST", scene.Target.Names());
        LineageRecord lineage = (await Backend.ReadLineageAsync(scene.Target, new ObjectName(fleets), Ct))!;
        Assert.True(lineage.IsReplica);
        Assert.Equal(scene.Fleet.System.Id, lineage.Source!.Dataset);

        // A follower of the copy reads the declarations the source fleet holds.
        string cache = Directory.CreateTempSubdirectory("varve-shipped-fleet-").FullName;

        try
        {
            await using Backend storage = await Backend.OpenAsync(new ObjectStorageOptions
            {
                Store = scene.Target,
                Prefix = new ObjectName(fleets),
                Cache = new CacheDirectory(cache),
                Access = ObjectStorageAccess.ReadOnly,
                Clock = TimeProvider.System,
                ProbeRandomness = TestRandomness.Instance,
            }, Ct);
            await using Dataset copy = await Dataset.OpenFollowerAsync(storage, new DatasetOptions { Clock = TimeProvider.System }, Ct);
            Assert.Equal(scene.Fleet.System.Head, copy.Head);
            using DatasetView view = copy.Pin();
            Assert.True(DeclarationReader.Read(view).Datasets.ContainsKey("orders"));
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    /// <summary>
    /// ADR 0185 decisions 3, 4, 7 and 8: a member of the fleet that serves
    /// the target follows the replica and never claims it; a grant present
    /// only in the source fleet's shipped system dataset grants nothing
    /// there (the maintainer's answer D3 on #166); an operator promotes it,
    /// stating the source and the position; and the old source's shipper
    /// stops.
    /// </summary>
    [Fact]
    public async Task a_member_of_the_target_fleet_follows_the_replica_and_an_operator_promotes_it()
    {
        await using Scene scene = await Scene.StartAsync(verified: true);
        Dataset source = await scene.DeclareAsync(grant: true);
        await CommitAsync(source, 0, 5);

        // The copy's last commit records position 500 of the source fleet's system dataset (ADR 0165).
        Assert.Equal(CommitOutcome.Committed, (await source.CommitAsync(
            new CommitRequestBuilder
            {
                Metadata = new CommitMetadata
                {
                    Agent = Varve.Rdf.RdfTerm.Iri("urn:test:writer"u8),
                    Attachments = [Varve.Rdf.RdfTerm.Literal("500"u8, Varve.Rdf.RdfTerm.Iri(Encoding.UTF8.GetBytes(Varve.Protocol.ManagementOrder.PositionDatatype)))],
                },
            }.Assert(Varve.Rdf.RdfTerm.Iri("https://example.org/last"u8), Varve.Rdf.RdfTerm.Iri("https://example.org/p"u8), Varve.Rdf.RdfTerm.Iri("https://example.org/o"u8)),
            Ct)).Outcome);
        await scene.WaitForAsync(s => s.State == ShippingState.Shipping && s.ShippedThrough == source.Head.Value);
        DatasetName orders = new("orders");

        // Another fleet, over the target store, declares the dataset placed on its one instance.
        await using Fleet target = await scene.StartTargetFleetAsync();
        await RunningServer.DeclareAsync(target,
            "@prefix varve: <" + ManagementVocabulary.Namespace + "> .\n"
            + "<" + ManagementVocabulary.DatasetGraphPrefix + "orders> {\n"
            + "  <urn:copy:orders> a varve:Dataset ; varve:datasetName \"orders\" ; varve:storage varve:ObjectStorage ; varve:placedOn \"t\" .\n"
            + "}\n");

        // It follows, warm, and claimed nothing: no writer record, no probe, and it is not a failure.
        await Scene.UntilAsync(() => target.Datasets.StandbyOf(orders).Warm, "the target's member is not a warm standby of the replica");
        Assert.Equal(DatasetRole.Standby, target.Datasets.RoleOf(orders));
        Assert.DoesNotContain(scene.Target.Names(), name => name == "orders/writer" || name.StartsWith("orders/probe/", StringComparison.Ordinal));
        Assert.True(target.Datasets.TryResolve(orders, out Dataset? replica));
        Assert.Equal(source.Head, replica.Head);

        // A write has no writer to go to, and the route says why: a retry cannot help.
        Assert.Equal(WriterRouteKind.Replica, (await target.RouteAsync(new WriterTarget(orders), Ct)).Kind);

        // D3: the source fleet grants alice the dataset. Its system dataset is on the target's store, and grants nothing there.
        Caller alice = new("https://login.example/v2.0#alice", "alice", new Dictionary<string, IReadOnlyList<string>>(), "roles");
        Assert.True(scene.Fleet.Table.Reaches(alice, "orders"));
        Assert.Contains("fleets/" + scene.Fleet.System.Id.Value.ToString("D") + "/log/MANIFEST", scene.Target.Names());
        Assert.False(target.Table.Reaches(alice, "orders"));
        Assert.False(target.Table.Writes(alice, "orders"));

        // Decision 8: every commit of a replica was written under the source fleet. Its system dataset, shipped
        // beside the copy, is what a recorded position is read in, under the name the dataset has there.
        ForeignFleet asReplica = (await target.ForeignFleetAsync(orders, replica.Head, Ct))!;
        Assert.Equal(scene.Fleet.System.Id, asReplica.System!.Id);
        Assert.NotSame(target.System, asReplica.System);
        Assert.Equal("orders", asReplica.Dataset.Value);
        Assert.Equal(scene.Fleet.System.Head, await asReplica.System.AdvanceAsync(Ct));
        using (DatasetView theirs = asReplica.System.Pin())
        {
            Assert.Contains(DeclarationReader.Read(theirs).Datasets["orders"].Grants, grant => grant.Access == GrantAccess.Write);
        }

        // The source's own dataset is no copy: every commit of it is its own fleet's.
        Assert.Null(await scene.Fleet.ForeignFleetAsync(orders, source.Head, Ct));

        // The operator says what they expect. A wrong position, or a wrong source, changes nothing.
        long tail = replica.Head.Value;
        Assert.Equal(PromotionRefusal.PositionMismatch, (await target.PromoteAsync(orders, new PromotionAsk("https://fleet.invalid/source", new Position(tail - 1)), Ct))!.Value.Refusal);
        Assert.Equal(PromotionRefusal.SourceMismatch, (await target.PromoteAsync(orders, new PromotionAsk("https://elsewhere.invalid/source", new Position(tail)), Ct))!.Value.Refusal);
        Assert.True((await Backend.ReadLineageAsync(scene.Target, new ObjectName("orders/"), Ct))!.IsReplica);
        Assert.DoesNotContain("orders/writer", scene.Target.Names());

        PromotionAnswer promoted = (await target.PromoteAsync(orders, new PromotionAsk("https://fleet.invalid/source", new Position(tail)), Ct))!.Value;
        Assert.Equal(PromotionRefusal.None, promoted.Refusal);
        Assert.Equal(tail, promoted.Position.Value);
        Assert.Equal(DatasetRole.Writer, target.Datasets.RoleOf(orders));
        LineageRecord lineage = (await Backend.ReadLineageAsync(scene.Target, new ObjectName("orders/"), Ct))!;
        Assert.False(lineage.IsReplica);
        Assert.Equal("t", lineage.Promoted!.By.Value);
        Assert.Equal(promoted.Epoch.Value, lineage.Promoted.Epoch.Value);

        Assert.Equal(WriterRouteKind.Local, (await target.RouteAsync(new WriterTarget(orders), Ct)).Kind);

        // Decision 8: the copy's commits recorded positions of another fleet. The target's write watermark is
        // not raised by them, or every write here would wait for a management position 500 of this fleet.
        Assert.Equal(0, target.Recorded(orders).Value);

        // After the promotion: a commit at or below the promoted position is the source fleet's, and one above it is this fleet's.
        Assert.NotNull(await target.ForeignFleetAsync(orders, new Position(tail), Ct));
        Assert.Null(await target.ForeignFleetAsync(orders, new Position(tail + 1), Ct));

        // It writes on, at once, on the same open dataset.
        await CommitAsync(replica, 5, 7);
        Assert.Equal(tail + 2, replica.Head.Value);

        // Promoting again is refused: it is no replica now.
        Assert.Equal(PromotionRefusal.NotAReplica, (await target.PromoteAsync(orders, new PromotionAsk("https://fleet.invalid/source", new Position(tail)), Ct))!.Value.Refusal);

        // The returning primary: its shipper stops, and the source still commits.
        await CommitAsync(source, 5, 6);
        ShippingStatus stopped = await scene.WaitForAsync(s => s.State == ShippingState.Promoted);
        Assert.Contains("promoted", stopped.Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ADR 0185 decisions 2 and 8, the architect's point C: a promoted copy
    /// shipped onward and promoted again. The second copy's lineage carries
    /// the chain, each earlier fleet's system dataset reaches the second
    /// copy's store, and the fleet that wrote a commit is answered per
    /// position: the first fleet up to the first promotion, the second up to
    /// the second, and no other fleet above it.
    /// </summary>
    [Fact]
    public async Task after_two_promotions_every_position_names_the_fleet_that_wrote_it_and_its_system_dataset_is_there()
    {
        await using Scene scene = await Scene.StartAsync(verified: true);
        Dataset first = await scene.DeclareAsync();
        await CommitAsync(first, 0, 4);
        await scene.WaitForAsync(s => s.State == ShippingState.Shipping && s.ShippedThrough == first.Head.Value);
        long one = first.Head.Value;
        DatasetName orders = new("orders");
        DatasetId firstFleet = scene.Fleet.System.Id;

        // The second fleet serves the copy, promotes it, and ships it on to a third store.
        MemoryObjectStore third = new();
        await scene.VerifyAsync(third);
        const string thirdAddress = "https://fleet.invalid/third";
        await using Fleet second = await scene.StartFleetOverAsync(scene.Target, TargetAddress, "t", "t", address => address == thirdAddress ? third : null);
        await RunningServer.DeclareAsync(second,
            "@prefix varve: <" + ManagementVocabulary.Namespace + "> .\n"
            + "<" + ManagementVocabulary.DatasetGraphPrefix + "orders> {\n"
            + "  <urn:copy:orders> a varve:Dataset ; varve:datasetName \"orders\" ; varve:storage varve:ObjectStorage ; varve:placedOn \"t\" ; varve:shipsTo <urn:copy:onward> .\n"
            + "  <urn:copy:onward> a varve:ShippingTarget ; varve:store \"" + thirdAddress + "\" ; varve:prefix \"orders\" ; varve:provider \"azurite\" .\n"
            + "}\n");
        await Scene.UntilAsync(() => second.Datasets.StandbyOf(orders).Warm, "the second fleet's member is not a warm standby of the replica");
        Assert.Equal(PromotionRefusal.None, (await second.PromoteAsync(orders, new PromotionAsk("https://fleet.invalid/source", new Position(one)), Ct))!.Value.Refusal);
        Assert.True(second.Datasets.TryResolve(orders, out Dataset? promoted));
        await CommitAsync(promoted, 4, 7);
        long two = promoted.Head.Value;
        DatasetId secondFleet = second.System.Id;

        // The third store gets the copy, its lineage with the chain, and both earlier fleets' system datasets.
        await Scene.UntilAsync(() => second.Shipping.StatusOf(orders) is [{ State: ShippingState.Shipping } status] && status.ShippedThrough == two, "the second fleet did not ship the promoted copy on");
        LineageRecord lineage = (await Backend.ReadLineageAsync(third, new ObjectName("orders/"), Ct))!;
        Assert.True(lineage.IsReplica);
        Assert.Equal(secondFleet, lineage.Source!.Fleet);
        LineageLink link = Assert.Single(lineage.Earlier);
        Assert.Equal(firstFleet, link.Source.Fleet);
        Assert.Equal(one, link.Promoted.Position.Value);
        Assert.Contains("fleets/" + firstFleet.Value.ToString("D") + "/log/MANIFEST", third.Names());
        Assert.Contains("fleets/" + secondFleet.Value.ToString("D") + "/log/MANIFEST", third.Names());

        // The first fleet's, on the third store, says what it said on the second: a replica of the first fleet's own.
        LineageRecord forwarded = (await Backend.ReadLineageAsync(third, new ObjectName("fleets/" + firstFleet.Value.ToString("D") + "/"), Ct))!;
        Assert.Equal(scene.Target.Get("fleets/" + firstFleet.Value.ToString("D") + "/lineage"), third.Get("fleets/" + firstFleet.Value.ToString("D") + "/lineage"));
        Assert.Equal(firstFleet, forwarded.Source!.Dataset);

        // The third fleet serves that copy and promotes it.
        await using Fleet last = await scene.StartFleetOverAsync(third, thirdAddress, "c", "c", null);
        await RunningServer.DeclareAsync(last,
            "@prefix varve: <" + ManagementVocabulary.Namespace + "> .\n"
            + "<" + ManagementVocabulary.DatasetGraphPrefix + "orders> {\n"
            + "  <urn:last:orders> a varve:Dataset ; varve:datasetName \"orders\" ; varve:storage varve:ObjectStorage ; varve:placedOn \"c\" .\n"
            + "}\n");
        await Scene.UntilAsync(() => last.Datasets.StandbyOf(orders).Warm, "the third fleet's member is not a warm standby of the replica");
        Assert.Equal(PromotionRefusal.None, (await last.PromoteAsync(orders, new PromotionAsk(TargetAddress, new Position(two)), Ct))!.Value.Refusal);
        Assert.True(last.Datasets.TryResolve(orders, out Dataset? final));
        await CommitAsync(final, 7, 9);

        // Per position: the first fleet's system dataset, then the second's, then none.
        foreach (long position in new[] { 1, one })
        {
            ForeignFleet? wrote = await last.ForeignFleetAsync(orders, new Position(position), Ct);
            Assert.Equal(firstFleet, wrote!.System!.Id);
        }

        foreach (long position in new[] { one + 1, two })
        {
            ForeignFleet? wrote = await last.ForeignFleetAsync(orders, new Position(position), Ct);
            Assert.Equal(secondFleet, wrote!.System!.Id);
        }

        Assert.Null(await last.ForeignFleetAsync(orders, new Position(two + 1), Ct));
        Assert.Null(await last.ForeignFleetAsync(orders, final.Head, Ct));
    }

    /// <summary>
    /// ADR 0185 decision 5: where no fleet serves the copy, it is promoted on
    /// the store itself. A finished promotion releases as any writer that
    /// closes, so the member placed on it afterwards starts as on any released
    /// dataset, and takes one epoch more.
    /// </summary>
    [Fact]
    public async Task a_copy_no_fleet_serves_is_promoted_on_the_store_and_the_next_writer_takes_one_epoch_more()
    {
        await using Scene scene = await Scene.StartAsync(verified: true);
        Dataset source = await scene.DeclareAsync();
        await CommitAsync(source, 0, 5);
        await scene.WaitForAsync(s => s.State == ShippingState.Shipping && s.ShippedThrough == source.Head.Value);
        long tail = source.Head.Value;
        ObjectName prefix = new("orders/");
        string cache = Directory.CreateTempSubdirectory("varve-embedded-").FullName;
        using StringWriter output = new();
        using StringWriter error = new();
        Varve.Server.Commands.Io io = new(output, error, Stream.Null);

        try
        {
            // What the operator expects is checked here too, and a wrong position changes nothing.
            Assert.False(await Varve.Server.Commands.Promote.EmbeddedAsync(scene.Target, prefix, Path.Combine(cache, "1"), "https://fleet.invalid/source", tail - 1, io, Ct));
            Assert.Contains("position-mismatch: its last position is " + tail, error.ToString(), StringComparison.Ordinal);
            Assert.True((await Backend.ReadLineageAsync(scene.Target, prefix, Ct))!.IsReplica);
            Assert.DoesNotContain("orders/writer", scene.Target.Names());

            Assert.True(await Varve.Server.Commands.Promote.EmbeddedAsync(scene.Target, prefix, Path.Combine(cache, "2"), "https://fleet.invalid/source", tail, io, Ct));
            Assert.Contains("released for the writer that starts next", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }

        // Promoted, fenced, finished, and released as any writer that closes: it opens for write as any dataset, and is no replica.
        WriterRecord record = (await Backend.ReadWriterAsync(scene.Target, prefix, Ct))!;
        Assert.Equal(ReleaseReason.Shutdown, record.Released);
        Assert.Equal(ClaimTrigger.Promotion, record.History[^1].Trigger);
        Assert.NotNull(record.History[^1].Fence);
        Assert.Equal(ReplicaState.None, await Backend.ReplicaStateAsync(scene.Target, prefix, Ct));
        Assert.Equal(tail, (await Backend.ReadLineageAsync(scene.Target, prefix, Ct))!.Promoted!.Position.Value);

        // A fleet started over the store afterwards: its placed member claims one epoch more, and writes on.
        await using Fleet target = await scene.StartTargetFleetAsync();
        await RunningServer.DeclareAsync(target,
            "@prefix varve: <" + ManagementVocabulary.Namespace + "> .\n"
            + "<" + ManagementVocabulary.DatasetGraphPrefix + "orders> {\n"
            + "  <urn:copy:orders> a varve:Dataset ; varve:datasetName \"orders\" ; varve:storage varve:ObjectStorage ; varve:placedOn \"t\" .\n"
            + "}\n");
        DatasetName orders = new("orders");
        await Scene.UntilAsync(() => target.Datasets.RoleOf(orders) == DatasetRole.Writer && target.Datasets.TryResolve(orders, out _), "the placed member did not open the promoted copy for write");
        Assert.True(target.Datasets.TryResolve(orders, out Dataset? promoted));
        Assert.Equal(tail, promoted.Head.Value);
        Assert.Equal(record.Epoch.Value + 1, (await Backend.ReadWriterAsync(scene.Target, prefix, Ct))!.Epoch.Value);
        await CommitAsync(promoted, 5, 7);
        Assert.Equal(tail + 2, promoted.Head.Value);
    }

    /// <summary>
    /// The maintainer's ruling of 2026-11-23 on #166 (ADR 0185 decisions 5
    /// and 6): a finished promotion on the store releases so that a writer set
    /// starts as after any ordinary release. The placed member takes the
    /// write, its standby stands by, and the standby takes over when the
    /// writer goes: nothing of the promotion is left for the set to know.
    /// </summary>
    [Fact]
    public async Task a_copy_promoted_on_the_store_is_taken_by_a_writer_set_as_after_any_ordinary_release()
    {
        await using Scene scene = await Scene.StartAsync(verified: true);
        Dataset source = await scene.DeclareAsync();
        await CommitAsync(source, 0, 5);
        await scene.WaitForAsync(s => s.State == ShippingState.Shipping && s.ShippedThrough == source.Head.Value);
        long tail = source.Head.Value;
        ObjectName prefix = new("orders/");
        string cache = Directory.CreateTempSubdirectory("varve-embedded-").FullName;
        using StringWriter output = new();
        using StringWriter error = new();

        try
        {
            Assert.True(await Varve.Server.Commands.Promote.EmbeddedAsync(scene.Target, prefix, cache, "https://fleet.invalid/source", tail, new Varve.Server.Commands.Io(output, error, Stream.Null), Ct), error.ToString());
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }

        WriterRecord released = (await Backend.ReadWriterAsync(scene.Target, prefix, Ct))!;
        Assert.Equal(ReleaseReason.Shutdown, released.Released);

        await using Fleet placed = await scene.StartTargetFleetAsync("t");
        await using Fleet standby = await scene.StartTargetFleetAsync("u");
        await RunningServer.DeclareAsync(placed,
            "@prefix varve: <" + ManagementVocabulary.Namespace + "> .\n"
            + "<" + ManagementVocabulary.DatasetGraphPrefix + "orders> {\n"
            + "  <urn:copy:orders> a varve:Dataset ; varve:datasetName \"orders\" ; varve:storage varve:ObjectStorage ;\n"
            + "    varve:placedOn \"t\" ; varve:standbyOn \"u\" ; varve:automaticHandover true .\n"
            + "}\n");
        DatasetName orders = new("orders");
        await Scene.UntilAsync(() => placed.Datasets.RoleOf(orders) == DatasetRole.Writer && placed.Datasets.TryResolve(orders, out _), "the placed member did not take the promoted copy");
        await Scene.UntilAsync(() => standby.Datasets.RoleOf(orders) == DatasetRole.Standby, "the standby did not stand by");

        // One epoch above the promotion's, by an ordinary start: the record's last claim is not a promotion.
        WriterRecord claimed = (await Backend.ReadWriterAsync(scene.Target, prefix, Ct))!;
        Assert.Equal(released.Epoch.Value + 1, claimed.Epoch.Value);
        Assert.Equal("t", claimed.Holder.Value);
        Assert.NotEqual(ClaimTrigger.Promotion, claimed.History[^1].Trigger);
        Assert.True(placed.Datasets.TryResolve(orders, out Dataset? promoted));
        await CommitAsync(promoted, 5, 7);
        Assert.Equal(tail + 2, promoted.Head.Value);
    }

    /// <summary>
    /// The maintainer's ruling of 2026-11-20 on #166: a dataset declared a
    /// copy of a source is never created by the fleet that serves it. Both
    /// sides are declared in one apply. Until the first shipping round the
    /// copy's member writes nothing and says what it waits for; after it, it
    /// follows.
    /// </summary>
    [Fact]
    public async Task a_dataset_declared_a_copy_is_never_created_and_waits_for_the_first_shipping_round()
    {
        // The store is not verified yet, so the first round cannot run and the wait can be seen.
        await using Scene scene = await Scene.StartAsync(verified: false, shipsWithinItsOwnStore: true);
        DatasetName orders = new("orders");
        DatasetName copy = new("copy");

        // One apply: the source, shipped to another prefix of the same store, and the copy.
        await RunningServer.DeclareAsync(scene.Fleet,
            "@prefix varve: <" + ManagementVocabulary.Namespace + "> .\n"
            + "<" + ManagementVocabulary.DatasetGraphPrefix + "orders> {\n"
            + "  <urn:both:orders> a varve:Dataset ; varve:datasetName \"orders\" ; varve:storage varve:ObjectStorage ; varve:placedOn \"a\" ; varve:shipsTo <urn:both:target> .\n"
            + "  <urn:both:target> a varve:ShippingTarget ; varve:store \"https://fleet.invalid/source\" ; varve:prefix \"copy\" ; varve:provider \"azurite\" .\n"
            + "}\n"
            + "<" + ManagementVocabulary.DatasetGraphPrefix + "copy> {\n"
            + "  <urn:both:copy> a varve:Dataset ; varve:datasetName \"copy\" ; varve:storage varve:ObjectStorage ; varve:placedOn \"a\" ; varve:copyOf <urn:both:source> .\n"
            + "  <urn:both:source> a varve:ShippingSource ; varve:store \"https://fleet.invalid/source\" ; varve:prefix \"orders\" .\n"
            + "}\n");
        Assert.True(scene.Fleet.Datasets.TryResolve(orders, out Dataset? source));
        await CommitAsync(source, 0, 4);
        await scene.WaitForAsync(s => s.State == ShippingState.StoreUnverified);

        // The copy is not there, its member created nothing under its prefix, and readiness's reason says what it waits for.
        for (int round = 0; round < 5; round++)
        {
            await scene.Fleet.AdvanceAsync(Ct);
        }

        Assert.False(scene.Fleet.Datasets.TryResolve(copy, out _));
        Assert.DoesNotContain(scene.Source.Names(), name => name.StartsWith("copy/", StringComparison.Ordinal));
        DatasetEntry waiting = scene.Fleet.Datasets.List().Single(entry => entry.Name == copy);
        Assert.Equal(DatasetState.Failed, waiting.State);
        Assert.Equal(DatasetFailure.Transient, waiting.Failure);
        Assert.Contains("waits for the first shipping round", waiting.Reason, StringComparison.Ordinal);
        Assert.Contains("Nothing is created here", waiting.Reason, StringComparison.Ordinal);

        // It is not a failure of this instance, which stays ready: the state has its own name.
        Assert.True(scene.Fleet.Datasets.AwaitsShipping(copy));
        (bool ready, IReadOnlyList<DatasetReadiness> lines) = Readiness.Evaluate(
            [new DatasetHealth("copy", waiting.State, waiting.Reason, 0, 0, false, Role: DatasetRole.Follower, AwaitsShipping: true)], 100, draining: false, null, 0);
        Assert.True(ready);
        Assert.Equal("awaiting-shipping", Assert.Single(lines).State);

        // The store is verified, the first round runs, and the member follows what the shipper wrote.
        await scene.VerifyAsync(scene.Source);
        await scene.WaitForAsync(s => s.State == ShippingState.Shipping && s.ShippedThrough == source.Head.Value);
        await Scene.UntilAsync(() => scene.Fleet.Datasets.StandbyOf(copy).Warm, "the member does not follow the copy");
        Assert.True(scene.Fleet.Datasets.TryResolve(copy, out Dataset? replica));
        Assert.Equal(source.Head, replica.Head);
        Assert.True((await Backend.ReadLineageAsync(scene.Source, new ObjectName("copy/"), Ct))!.IsReplica);
        Assert.DoesNotContain("copy/writer", scene.Source.Names());
        Assert.False(scene.Fleet.Datasets.AwaitsShipping(copy));
    }

    /// <summary>
    /// The maintainer's ruling of 2026-11-23 on #166 (ADR 0185 decision 10).
    /// An apply that declares a copy is refused where the answering instance
    /// can see the dataset is written in this fleet with no lineage naming
    /// that source. Where the declaration is applied all the same, the
    /// running writer keeps writing and readiness says at once that the
    /// declaration is wrong. A declaration of a copy that is not there yet,
    /// and one whose lineage names the source, are not refused.
    /// </summary>
    [Fact]
    public async Task a_dataset_written_here_is_not_declared_a_copy_and_a_writer_that_meets_the_declaration_keeps_writing()
    {
        await using Scene scene = await Scene.StartAsync(verified: true);
        DatasetName name = new("own");

        // An ordinary dataset first.
        await RunningServer.DeclareAsync(scene.Fleet, Declared(string.Empty));
        Assert.True(scene.Fleet.Datasets.TryResolve(name, out Dataset? own));
        await CommitAsync(own, 0, 2);
        string[] before = [.. scene.Source.Names().Where(written => written.StartsWith("own/log/", StringComparison.Ordinal))];
        string copy = Declared(" ; varve:copyOf <urn:own:source>").Replace("}\n", "  <urn:own:source> a varve:ShippingSource ; varve:store \"https://elsewhere.invalid/primary\" ; varve:prefix \"own\" .\n}\n", StringComparison.Ordinal);

        // The apply is refused: this instance sees the dataset on its store, with no lineage.
        CopyRefusal refusal = (await scene.Fleet.CheckCopiesAsync(Proposed(copy), Ct))!.Value;
        Assert.Equal(name, refusal.Dataset);
        Assert.Contains("with no lineage record: it is a dataset written in this fleet", refusal.Detail.Value, StringComparison.Ordinal);
        Assert.Contains("Remove varve:copyOf", refusal.Detail.Value, StringComparison.Ordinal);
        Assert.Null(scene.Fleet.Datasets.CopyContradictionOf(name));

        // A copy that is not there yet is not refused, nor is the same dataset declared without varve:copyOf.
        Assert.Null(await scene.Fleet.CheckCopiesAsync(Proposed(copy.Replace("own", "other", StringComparison.Ordinal)), Ct));
        Assert.Null(await scene.Fleet.CheckCopiesAsync(Proposed(Declared(string.Empty)), Ct));

        // Applied where the apply could not see it, as by an instance that could not read the store: the writer
        // keeps writing, and readiness reports the contradiction as soon as the declaration is applied.
        await RunningServer.DeclareAsync(scene.Fleet, copy);
        await Scene.UntilAsync(() => scene.Fleet.Datasets.CopyContradictionOf(name) is not null, "readiness did not report the contradiction");
        Assert.Contains("it is a dataset written in this fleet", scene.Fleet.Datasets.CopyContradictionOf(name), StringComparison.Ordinal);
        Assert.Equal(DatasetRole.Writer, scene.Fleet.Datasets.RoleOf(name));
        await CommitAsync(own, 2, 4);
        Assert.Equal(4, own.Head.Value);
        Assert.Equal(before, scene.Source.Names().Where(written => written.StartsWith("own/log/", StringComparison.Ordinal) && before.Contains(written)));
        Assert.DoesNotContain("own/lineage", scene.Source.Names());

        // The declaration put right: nothing is reported.
        Varve.Rdf.RdfTerm graph = Varve.Rdf.RdfTerm.Iri(Encoding.UTF8.GetBytes(ManagementVocabulary.DatasetGraphPrefix + "own"));
        Assert.Equal(CommitOutcome.Committed, (await scene.Fleet.System.CommitAsync(
            new CommitRequestBuilder { ExpectedPosition = scene.Fleet.System.Head, Validators = [scene.Fleet.Validator] }.Retract(
                Varve.Rdf.RdfTerm.Iri("urn:own:d"u8),
                Term("copyOf"),
                Varve.Rdf.RdfTerm.Iri("urn:own:source"u8),
                graph)
            .Retract(Varve.Rdf.RdfTerm.Iri("urn:own:source"u8), Varve.Rdf.RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8), Term("ShippingSource"), graph)
            .Retract(Varve.Rdf.RdfTerm.Iri("urn:own:source"u8), Term("store"), Varve.Rdf.RdfTerm.Literal("https://elsewhere.invalid/primary"u8), graph)
            .Retract(Varve.Rdf.RdfTerm.Iri("urn:own:source"u8), Term("prefix"), Varve.Rdf.RdfTerm.Literal("own"u8), graph),
            Ct)).Outcome);
        await Scene.UntilAsync(() => scene.Fleet.Datasets.CopyContradictionOf(name) is null, "the contradiction was still reported after varve:copyOf was removed");

        static string Declared(string more) =>
            "@prefix varve: <" + ManagementVocabulary.Namespace + "> .\n"
            + "<" + ManagementVocabulary.DatasetGraphPrefix + "own> {\n"
            + "  <urn:own:d> a varve:Dataset ; varve:datasetName \"own\" ; varve:storage varve:ObjectStorage ; varve:placedOn \"a\"" + more + " .\n"
            + "}\n";

        static Varve.Rdf.RdfTerm Term(string local) => Varve.Rdf.RdfTerm.Iri(Encoding.UTF8.GetBytes(ManagementVocabulary.Namespace + local));

        static Declarations Proposed(string trig)
        {
            Varve.Rdf.InMemoryDataset desired = ManagementTrig.TryParse(Encoding.UTF8.GetBytes(trig), null, out string? error) ?? throw new InvalidOperationException(error);
            return DeclarationReader.Read(desired);
        }
    }

    /// <summary>
    /// Decision 9: the compatibility page is a notice and never a refusal.
    /// What refuses a target is the absence of a verification record at its
    /// store. A target of a provider the page does not list as verified is
    /// shipped once its store holds one.
    /// </summary>
    [Fact]
    public async Task a_target_of_a_provider_the_page_has_not_tested_is_shipped_when_its_store_holds_a_record()
    {
        await using Scene scene = await Scene.StartAsync(verified: true);
        Dataset dataset = await scene.DeclareAsync(provider: "minio-ce");
        await CommitAsync(dataset, 0, 2);

        await scene.WaitForAsync(s => s.State == ShippingState.Shipping && s.ShippedThrough == dataset.Head.Value);

        Assert.Contains("orders/log/MANIFEST", scene.Target.Names());
    }

    [Fact]
    public void an_apply_is_told_which_providers_the_compatibility_page_compiled_into_the_build_has_not_verified()
    {
        Assert.Equal("verified", CompatibilityPage.StateOf("azurite"));
        Assert.Equal("verified", CompatibilityPage.StateOf("rustfs"));
        Assert.Equal("unsupported", CompatibilityPage.StateOf("garage"));
        Assert.Equal("no adapter", CompatibilityPage.StateOf("gcs"));
        Assert.Equal("unknown", CompatibilityPage.StateOf("not-on-the-page"));

        static Varve.Management.Model.Declarations Declared(string provider) => DeclarationReader.Read(ManagementTrig.TryParse(Encoding.UTF8.GetBytes(
            "@prefix varve: <" + ManagementVocabulary.Namespace + "> .\n"
            + "<" + ManagementVocabulary.DatasetGraphPrefix + "orders> {\n"
            + "  <urn:ship:orders> a varve:Dataset ; varve:datasetName \"orders\" ; varve:storage varve:ObjectStorage ; varve:shipsTo <urn:ship:replica> .\n"
            + "  <urn:ship:replica> a varve:ShippingTarget ; varve:store \"s3://replicas\" ; varve:prefix \"orders\" ; varve:provider \"" + provider + "\" .\n"
            + "}\n"), null, out _)!);

        Assert.Empty(CompatibilityPage.Untested(Declared("rustfs")));
        Assert.Equal([new UntestedTarget("orders", "minio-ce", "untested")], CompatibilityPage.Untested(Declared("minio-ce")));
        Assert.Equal([new UntestedTarget("orders", "nowhere", "unknown")], CompatibilityPage.Untested(Declared("nowhere")));
        Assert.Equal("shipping-provider-untested; dataset=orders; provider=gcs; state=no-adapter", Assert.Single(CompatibilityPage.Untested(Declared("gcs"))).HeaderValue);

        // What varve apply prints for one.
        Assert.Equal(
            "Notice: dataset orders ships to a store of provider 'gcs', which this build's compatibility page lists as no adapter. The project has not tested it. "
            + "Shipping starts once that store holds a verification record: run 'varve storage verify' against it (docs/operator/object-stores.md).",
            Varve.Server.Commands.Apply.Notice("shipping-provider-untested; dataset=orders; provider=gcs; state=no-adapter"));
    }

    private static int Requests(MemoryObjectStore store) =>
        store.Count(ObjectOperation.Create) + store.Count(ObjectOperation.Replace) + store.Count(ObjectOperation.Delete) + store.Count(ObjectOperation.Read) + store.Count(ObjectOperation.List);

    private static async Task CommitAsync(Dataset dataset, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            CommitRequestBuilder request = new CommitRequestBuilder().Assert(
                Varve.Rdf.RdfTerm.Iri(Encoding.UTF8.GetBytes("https://example.org/s" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                Varve.Rdf.RdfTerm.Iri("https://example.org/p"u8),
                Varve.Rdf.RdfTerm.Iri("https://example.org/o"u8));
            Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(request, Ct)).Outcome);
        }
    }

    /// <summary>One instance that writes a fleet over an in-memory store, its interval loop running, and a target store.</summary>
    private sealed class Scene : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stopping = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        private readonly string _caches = Directory.CreateTempSubdirectory("varve-shipping-role-").FullName;
        private readonly List<Task> _loops = [];

        public MemoryObjectStore Source { get; } = new();

        public MemoryObjectStore Target { get; } = new();

        public Fleet Fleet { get; private set; } = null!;

        public static async Task<Scene> StartAsync(bool verified, bool shipsWithinItsOwnStore = false)
        {
            Scene scene = new();

            if (verified)
            {
                await scene.VerifyAsync();
            }

            ServerSettings settings = new();
            settings.Auth.Mode = "Anonymous";
            settings.ObjectStorage.Provider = "Azure";
            settings.ObjectStorage.ContainerUrl = "https://fleet.invalid/source";
            settings.ObjectStorage.CachePath = Path.Combine(scene._caches, "a");
            settings.ObjectStorage.Instance = "a";
            settings.Fleet.Writer = "a";
            settings.Fleet.FollowInterval = Interval;
            settings.Replication.ShipInterval = Interval;
            ObjectStores objects = ObjectStores.Over(scene.Source, settings.ObjectStorage, TimeProvider.System);
            objects.TargetsForTests = address => address == TargetAddress ? scene.Target : shipsWithinItsOwnStore && address == "https://fleet.invalid/source" ? scene.Source : null;
            scene.Fleet = await Fleet.StartAsync(settings, objects, recordBootstrap: false, TimeProvider.System, NullLogger.Instance, new HttpContextAccessor(), Ct);
            scene._loops.Add(scene.Fleet.RunAsync(scene._stopping.Token));
            return scene;
        }

        /// <summary>The record a passing run of the suite leaves at the target's root (ADR 0183).</summary>
        public async Task VerifyAsync(MemoryObjectStore? store = null)
        {
            VerificationOutcome outcome = await ObjectStoreVerification.RunAsync(
                store ?? Target, new VerificationOptions { Rounds = new VerificationRounds(1), Randomness = TestRandomness.Instance, Clock = TimeProvider.System }, Ct);
            Assert.Null(outcome.Counterexample);
        }

        /// <summary>Declares the dataset <c>orders</c>, placed on this instance and shipped to the target, and returns it open.</summary>
        public async Task<Dataset> DeclareAsync(string provider = "azurite", bool grant = false)
        {
            await RunningServer.DeclareAsync(Fleet,
                "@prefix varve: <" + ManagementVocabulary.Namespace + "> .\n"
                + "<" + ManagementVocabulary.DatasetGraphPrefix + "orders> {\n"
                + "  <urn:ship:orders> a varve:Dataset ; varve:datasetName \"orders\" ; varve:storage varve:ObjectStorage ; varve:placedOn \"a\" ; varve:shipsTo <urn:ship:replica> .\n"
                + "  <urn:ship:replica> a varve:ShippingTarget ; varve:store \"" + TargetAddress + "\" ; varve:prefix \"orders\" ; varve:provider \"" + provider + "\" .\n"
                + (grant ? "  <urn:ship:alice> a varve:Grant ; varve:grantee <https://login.example/v2.0#alice> ; varve:permission varve:write ; varve:allGraphs true .\n" : string.Empty)
                + "}\n");
            Assert.True(Fleet.Datasets.TryResolve(new DatasetName("orders"), out Dataset? dataset));
            return dataset;
        }

        /// <summary>A second fleet, of one instance <c>t</c>, over the target store: the fleet that serves the copy.</summary>
        public Task<Fleet> StartTargetFleetAsync(string instance = "t") => StartFleetOverAsync(Target, TargetAddress, instance, "t", null);

        /// <summary>A fleet over a store of the test's, which may ship on to others.</summary>
        public async Task<Fleet> StartFleetOverAsync(MemoryObjectStore store, string address, string instance, string writer, Func<string, IObjectStore?>? targets)
        {
            ServerSettings settings = new();
            settings.Auth.Mode = "Anonymous";
            settings.ObjectStorage.Provider = "Azure";
            settings.ObjectStorage.ContainerUrl = address;
            settings.ObjectStorage.CachePath = Path.Combine(_caches, instance);
            settings.ObjectStorage.Instance = instance;
            settings.Fleet.Writer = writer;
            settings.Fleet.FollowInterval = Interval;
            settings.Replication.ShipInterval = Interval;
            ObjectStores objects = ObjectStores.Over(store, settings.ObjectStorage, TimeProvider.System);
            objects.TargetsForTests = targets;
            Fleet fleet = await Fleet.StartAsync(settings, objects, recordBootstrap: false, TimeProvider.System, NullLogger.Instance, new HttpContextAccessor(), Ct);
            _loops.Add(fleet.RunAsync(_stopping.Token));
            return fleet;
        }

        public static async Task UntilAsync(Func<bool> reached, string otherwise)
        {
            Stopwatch watch = Stopwatch.StartNew();

            while (!reached())
            {
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), otherwise);
                await Task.Delay(Interval, Ct);
            }
        }

        public ShippingStatus Status() => Assert.Single(Fleet.Shipping.StatusOf(new DatasetName("orders")));

        public async Task<ShippingStatus> WaitForAsync(Func<ShippingStatus, bool> reached)
        {
            Stopwatch watch = Stopwatch.StartNew();
            IReadOnlyList<ShippingStatus> last = [];

            while (watch.Elapsed < TimeSpan.FromSeconds(60))
            {
                last = Fleet.Shipping.StatusOf(new DatasetName("orders"));

                if (last.Count == 1 && reached(last[0]))
                {
                    return last[0];
                }

                await Task.Delay(Interval, Ct);
            }

            throw new Xunit.Sdk.XunitException("The shipper did not get there in a minute. Its status: " + string.Join("; ", last.Select(status => status.ToString())));
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();

            foreach (Task loop in _loops)
            {
                try
                {
                    await loop;
                }
                catch (OperationCanceledException)
                {
                }
            }

            await Fleet.DisposeAsync();
            _stopping.Dispose();
            Directory.Delete(_caches, recursive: true);
        }
    }
}
