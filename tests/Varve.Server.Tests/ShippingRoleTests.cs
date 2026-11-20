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
    /// Decision 9: a target's provider is one the compatibility page of the
    /// build lists as verified. A declaration that reached the system
    /// dataset by another road is still refused by the shipper, with nothing
    /// written.
    /// </summary>
    [Fact]
    public async Task a_target_of_an_unverified_provider_is_never_written_to()
    {
        await using Scene scene = await Scene.StartAsync(verified: true);
        string[] before = [.. scene.Target.Names()];
        Dataset dataset = await scene.DeclareAsync(provider: "minio-ce");
        await CommitAsync(dataset, 0, 2);

        ShippingStatus refused = await scene.WaitForAsync(s => s.State == ShippingState.StoreUnverified);

        Assert.Contains("'minio-ce' is untested", refused.Detail, StringComparison.Ordinal);
        Assert.Equal(before, scene.Target.Names());
    }

    [Fact]
    public void an_apply_is_checked_against_the_compatibility_page_compiled_into_the_build()
    {
        Assert.Equal("verified", CompatibilityPage.StateOf("azurite"));
        Assert.Equal("verified", CompatibilityPage.StateOf("rustfs"));
        Assert.Equal("unsupported", CompatibilityPage.StateOf("garage"));
        Assert.Equal("no adapter", CompatibilityPage.StateOf("gcs"));
        Assert.Equal("unknown", CompatibilityPage.StateOf("not-on-the-page"));

        static Varve.Management.Model.Declarations Declared(string target) => DeclarationReader.Read(ManagementTrig.TryParse(Encoding.UTF8.GetBytes(
            "@prefix varve: <" + ManagementVocabulary.Namespace + "> .\n"
            + "<" + ManagementVocabulary.DatasetGraphPrefix + "orders> {\n"
            + "  <urn:ship:orders> a varve:Dataset ; varve:datasetName \"orders\" ; varve:storage varve:ObjectStorage ; varve:shipsTo <urn:ship:replica> .\n"
            + "  <urn:ship:replica> a varve:ShippingTarget ; " + target + " .\n"
            + "}\n"), null, out _)!);

        Assert.Null(CompatibilityPage.Check(Declared("varve:store \"s3://replicas\" ; varve:prefix \"orders\" ; varve:provider \"rustfs\"")));
        Assert.Equal(new UnverifiedTarget("orders", "minio-ce", "untested"), CompatibilityPage.Check(Declared("varve:store \"s3://replicas\" ; varve:prefix \"orders\" ; varve:provider \"minio-ce\"")));
        Assert.Equal(new UnverifiedTarget("orders", "nowhere", "unknown"), CompatibilityPage.Check(Declared("varve:store \"s3://replicas\" ; varve:prefix \"orders\" ; varve:provider \"nowhere\"")));

        // A directory target names no provider and is exempt.
        Assert.Null(CompatibilityPage.Check(Declared("varve:directory \"/var/lib/varve/replica\"")));
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
        private Task _loop = Task.CompletedTask;

        public MemoryObjectStore Source { get; } = new();

        public MemoryObjectStore Target { get; } = new();

        public Fleet Fleet { get; private set; } = null!;

        public static async Task<Scene> StartAsync(bool verified)
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
            objects.TargetsForTests = address => address == TargetAddress ? scene.Target : null;
            scene.Fleet = await Fleet.StartAsync(settings, objects, recordBootstrap: false, TimeProvider.System, NullLogger.Instance, new HttpContextAccessor(), Ct);
            scene._loop = scene.Fleet.RunAsync(scene._stopping.Token);
            return scene;
        }

        /// <summary>The record a passing run of the suite leaves at the target's root (ADR 0183).</summary>
        public async Task VerifyAsync()
        {
            VerificationOutcome outcome = await ObjectStoreVerification.RunAsync(
                Target, new VerificationOptions { Rounds = new VerificationRounds(1), Randomness = TestRandomness.Instance, Clock = TimeProvider.System }, Ct);
            Assert.Null(outcome.Counterexample);
        }

        /// <summary>Declares the dataset <c>orders</c>, placed on this instance and shipped to the target, and returns it open.</summary>
        public async Task<Dataset> DeclareAsync(string provider = "azurite")
        {
            await RunningServer.DeclareAsync(Fleet,
                "@prefix varve: <" + ManagementVocabulary.Namespace + "> .\n"
                + "<" + ManagementVocabulary.DatasetGraphPrefix + "orders> {\n"
                + "  <urn:ship:orders> a varve:Dataset ; varve:datasetName \"orders\" ; varve:storage varve:ObjectStorage ; varve:placedOn \"a\" ; varve:shipsTo <urn:ship:replica> .\n"
                + "  <urn:ship:replica> a varve:ShippingTarget ; varve:store \"" + TargetAddress + "\" ; varve:prefix \"orders\" ; varve:provider \"" + provider + "\" .\n"
                + "}\n");
            Assert.True(Fleet.Datasets.TryResolve(new DatasetName("orders"), out Dataset? dataset));
            return dataset;
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

            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
            }

            await Fleet.DisposeAsync();
            _stopping.Dispose();
            Directory.Delete(_caches, recursive: true);
        }
    }
}
