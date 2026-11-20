// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Microsoft.Extensions.Logging;
using Varve.Management.Model;
using Varve.Protocol.Model;
using Varve.Store;
using Varve.Store.Log;
using Varve.Store.ObjectStorage;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Server;

/// <summary>What a shipper of one target is doing (ADR 0184 decision 11), as readiness reports it.</summary>
internal static class ShippingState
{
    /// <summary>The target holds what the source held when the last round began.</summary>
    internal const string Shipping = "shipping";

    /// <summary>The last round failed, and the shipper waits out its back-off.</summary>
    internal const string Retrying = "retrying";

    /// <summary>The target's store has no verification record this build accepts (ADR 0183). Retried.</summary>
    internal const string StoreUnverified = "store-unverified";

    /// <summary>This instance cannot reach the declared store: it has no endpoint for it. Retried.</summary>
    internal const string TargetUnreachable = "target-unconfigured";

    /// <summary>The target holds bytes that are not the source's. The shipper has stopped for it.</summary>
    internal const string Diverged = "shipping-diverged";

    /// <summary>The target's prefix holds something that is not a replica of this source. The shipper has stopped for it.</summary>
    internal const string TargetNotEmpty = "shipping-target-not-empty";

    /// <summary>The target was promoted (ADR 0185). The shipper has stopped for it.</summary>
    internal const string Promoted = "replica-promoted";

    /// <summary>A directory target, which this build does not ship to yet.</summary>
    internal const string DirectoryNotShipped = "directory-not-shipped";
}

/// <summary>One target's shipping, for readiness (ADR 0184 decision 11).</summary>
/// <param name="Target">The target: its store and prefix, or its directory.</param>
/// <param name="State">One of <see cref="ShippingState"/>.</param>
/// <param name="ShippedThrough">The source position the target's log holds at least, or none where it is not known.</param>
/// <param name="CatalogPosition">The position of the catalog the target holds, or none while it has no catalog.</param>
/// <param name="Failures">How many rounds in a row have failed.</param>
/// <param name="Detail">What the last failure said, or none.</param>
internal sealed record ShippingStatus(string Target, string State, long? ShippedThrough, long? CatalogPosition, int Failures, string? Detail);

/// <summary>
/// The shipping role (ADR 0184 decision 2): one shipper per declared target
/// of each dataset this instance ships. By default that is the dataset's
/// writer, and with <c>varve:shippedOn</c> the instance it names.
/// </summary>
/// <remarks>
/// <para>
/// **Off the sequencer.** A shipper reads the source's objects through the
/// store and never through the dataset, so a slow or failing target delays no
/// commit. A failure is the target's alone (decision 11): the shipper retries
/// with a back-off of its own, never releases the dataset, and never makes
/// the instance unready.
/// </para>
/// <para>
/// **No probe when nothing moved.** On the writer a round runs only when the
/// dataset's head or its catalog has moved since the last complete round.
/// The interval sets how soon that is noticed, never whether a round's
/// writes are correct.
/// </para>
/// </remarks>
[Contract(typeof(LogShipping.TheShipperRunsOffTheSequencer), Role = "the server's shippers, one per declared target")]
internal sealed partial class Shipping : IAsyncDisposable
{
    private readonly Func<Declarations> _declarations;
    private readonly OpenDatasets _datasets;
    private readonly ObjectStores? _objects;
    private readonly ReplicationSettings _settings;
    private readonly Func<Dataset> _system;
    private readonly Dictionary<string, FleetCopy> _fleets = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Dataset, string Target), Entry> _entries = [];
    private bool _disposed;

    internal Shipping(Func<Declarations> declarations, OpenDatasets datasets, ObjectStores? objects, ReplicationSettings settings, Func<Dataset> system, TimeProvider clock, ILogger logger)
    {
        _declarations = declarations;
        _datasets = datasets;
        _objects = objects;
        _settings = settings;
        _system = system;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Each target's shipping of <paramref name="dataset"/> on this instance, in the order of their names; empty where this instance ships none.</summary>
    internal IReadOnlyList<ShippingStatus> StatusOf(DatasetName dataset)
    {
        List<ShippingStatus> statuses = [];

        lock (_lock)
        {
            foreach (((string name, string _), Entry entry) in _entries)
            {
                if (name == dataset.Value)
                {
                    statuses.Add(entry.Status);
                }
            }
        }

        statuses.Sort(static (a, b) => string.CompareOrdinal(a.Target, b.Target));
        return statuses;
    }

    /// <summary>
    /// Brings the shippers of <paramref name="dataset"/> in line with its
    /// declaration and with what this instance holds: a target declared gets
    /// a shipper where this instance ships it, and a target gone, a dataset
    /// closed, reopened or handed over, stops its own.
    /// </summary>
    internal void Reconcile(DatasetName dataset)
    {
        Dataset? held = _datasets.TryResolve(dataset, out Dataset? open) ? open : null;
        bool writes = _datasets.RoleOf(dataset) == DatasetRole.Writer;
        Dictionary<string, ShippingTargetDeclaration> wanted = new(StringComparer.Ordinal);

        if (_objects is not null && held is not null && _declarations().Datasets.TryGetValue(dataset.Value, out DatasetDeclaration? declared))
        {
            foreach (ShippingTargetDeclaration target in declared.ShippingTargets)
            {
                if (target.ShippedOn is null ? writes : string.Equals(target.ShippedOn, _objects.Instance.Value, StringComparison.Ordinal))
                {
                    wanted[NameOf(target)] = target;
                }
            }
        }

        List<Entry> stopped = [];

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            foreach (((string name, string key), Entry entry) in _entries)
            {
                if (name == dataset.Value && (!wanted.TryGetValue(key, out ShippingTargetDeclaration? declaredNow) || declaredNow != entry.Target || !ReferenceEquals(entry.Dataset, held) || entry.Writes != writes))
                {
                    stopped.Add(entry);
                }
            }

            foreach (Entry entry in stopped)
            {
                _entries.Remove((dataset.Value, entry.Key));
            }

            foreach ((string key, ShippingTargetDeclaration target) in wanted)
            {
                if (!_entries.ContainsKey((dataset.Value, key)))
                {
                    Entry entry = new(this, dataset.Value, key, target, held!, writes);
                    _entries[(dataset.Value, key)] = entry;
                    entry.Start();
                }
            }
        }

        foreach (Entry entry in stopped)
        {
            _ = entry.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<Entry> entries;

        lock (_lock)
        {
            _disposed = true;
            entries = [.. _entries.Values];
            _entries.Clear();
        }

        foreach (Entry entry in entries)
        {
            await entry.StopAsync().ConfigureAwait(false);
        }

        lock (_lock)
        {
            foreach (FleetCopy copy in _fleets.Values)
            {
                copy.Dispose();
            }

            _fleets.Clear();
        }
    }

    /// <summary>
    /// Readiness's <c>shipping</c> member of a dataset (ADR 0184 decision 11):
    /// each target by name, with its state, the source position its log holds
    /// at least and the lag behind <paramref name="head"/>, the position of
    /// its catalog beside them, and what the last failure said. Nothing is
    /// written for a dataset this instance ships nowhere.
    /// </summary>
    internal static void Write(System.Text.Json.Utf8JsonWriter json, IReadOnlyList<ShippingStatus> targets, long? head)
    {
        if (targets.Count == 0)
        {
            return;
        }

        json.WriteStartObject("shipping");

        foreach (ShippingStatus target in targets)
        {
            json.WriteStartObject(target.Target);
            json.WriteString("state", target.State);

            if (target.ShippedThrough is { } through)
            {
                json.WriteNumber("shippedThrough", through);

                if (head is { } at)
                {
                    json.WriteNumber("lag", Math.Max(0, at - through));
                }
            }

            if (target.CatalogPosition is { } catalog)
            {
                json.WriteNumber("catalogPosition", catalog);
            }

            if (target.Failures > 0)
            {
                json.WriteNumber("failures", target.Failures);
            }

            if (target.Detail is { } detail)
            {
                json.WriteString("detail", detail);
            }

            json.WriteEndObject();
        }

        json.WriteEndObject();
    }

    /// <summary>A target as readiness and the logs name it: its store and prefix, or its directory. An address carries no credential (ADR 0037).</summary>
    internal static string NameOf(ShippingTargetDeclaration target) =>
        target.Directory ?? target.Store!.TrimEnd('/') + "/" + target.Prefix;

    // The last part of the shipper's reader id (ADR 0184 decision 7): a short digest of the target's store and prefix.
    private static string DigestOf(string target) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(target)).AsSpan(0, 6));

    // The copy of this fleet's system dataset on one target store, shared by every dataset shipped there.
    private FleetCopy FleetCopyOf(string store)
    {
        lock (_lock)
        {
            return _fleets.TryGetValue(store, out FleetCopy? copy) ? copy : _fleets[store] = new FleetCopy(this, store);
        }
    }

    /// <summary>
    /// The source fleet's system dataset on one target store (ADR 0185
    /// decision 8, the maintainer's answer D3 on #166): shipped to
    /// <c>fleets/{fleet id}/</c> there, ahead of every dataset commit that
    /// refers to it (ADR 0184 decision 13).
    /// </summary>
    /// <remarks>
    /// A dataset's shipper calls <see cref="ShipAsync"/> after it has read
    /// log bytes from the source and before it creates them on the target.
    /// Every commit in those bytes recorded a management position its writer
    /// had applied, so that position was in the source's store before the
    /// bytes were read, and a round that starts now ships it. Where this
    /// instance writes the dataset, its own applied position bounds what its
    /// commits recorded, so a round is skipped while that has not moved.
    /// </remarks>
    private sealed class FleetCopy(Shipping owner, string store) : IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private LogShipper? _shipper;
        private long _shipped = -1;

        public void Dispose() => _gate.Dispose();

        // appliedBounds: the commits about to be shipped were written by this instance, so its applied
        // management position bounds what they recorded.
        internal async ValueTask ShipAsync(bool appliedBounds, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                Dataset system = owner._system();
                long applied = system.Head.Value;

                if (_shipper is not null && appliedBounds && applied == _shipped)
                {
                    return;
                }

                try
                {
                    _shipper ??= await LogShipper.StartAsync(Options(), cancellationToken).ConfigureAwait(false);

                    if (_shipper is not null)
                    {
                        await _shipper.ShipAsync(cancellationToken).ConfigureAwait(false);
                        _shipped = applied;
                    }
                }
                catch
                {
                    _shipper = null;
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private ShippingOptions Options()
        {
            ObjectStores objects = owner._objects!;
            Dataset system = owner._system();
            string fleet = system.Id.Value.ToString("D");
            string digest = DigestOf(store + "/fleets/" + fleet);
            string staging = objects.StagingOf(Fleet.SystemName, digest);
            Directory.CreateDirectory(staging);

            return new ShippingOptions
            {
                Source = objects.Store,
                SourcePrefix = ObjectStores.PrefixOf(Fleet.SystemName),
                Target = objects.OpenTarget(store, owner._settings),
                TargetPrefix = new ObjectName("fleets/" + fleet + "/"),
                Lineage = new LineageSource(new StoreAddress(objects.Address), ObjectStores.PrefixOf(Fleet.SystemName), system.Id, system.Id),
                Reader = new ReaderId((objects.ConfiguredReader ?? objects.Instance.Value) + "-ship-" + digest),
                Holder = objects.Instance,
                Staging = new CacheDirectory(staging),
                Clock = owner._clock,
            };
        }
    }

    private sealed class Entry : IAsyncDisposable
    {
        private readonly Shipping _owner;
        private readonly string _dataset;
        private readonly CancellationTokenSource _stop = new();
        private readonly ClaimHoldOff _holdOff;
        private Task _run = Task.CompletedTask;
        private ShippingStatus _status;

        internal Entry(Shipping owner, string dataset, string key, ShippingTargetDeclaration target, Dataset held, bool writes)
        {
            _owner = owner;
            _dataset = dataset;
            Target = target;
            Key = key;
            Dataset = held;
            Writes = writes;

            // Shaped as the claim hold-off is, and an instance of its own (ADR 0184 decision 11).
            _holdOff = new ClaimHoldOff(owner._settings.ShipInterval, owner._clock, SystemProbeRandomness.Instance);
            _status = new ShippingStatus(key, ShippingState.Retrying, null, null, 0, null);
        }

        internal string Key { get; }

        internal ShippingTargetDeclaration Target { get; }

        internal Dataset Dataset { get; }

        internal bool Writes { get; }

        internal ShippingStatus Status => Volatile.Read(ref _status);

        internal void Start() => _run = Task.Run(() => RunAsync(_stop.Token));

        public ValueTask DisposeAsync() => new(StopAsync());

        internal async Task StopAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);

            try
            {
                await _run.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            _stop.Dispose();
        }

        private void Report(string state, long? through, long? catalog, string? detail) =>
            Volatile.Write(ref _status, new ShippingStatus(Key, state, through ?? Status.ShippedThrough, catalog ?? Status.CatalogPosition, _holdOff.Failures, detail));

        private async Task RunAsync(CancellationToken stopping)
        {
            if (Target.Directory is not null)
            {
                Report(ShippingState.DirectoryNotShipped, null, null, "This build does not ship to a directory yet.");
                return;
            }

            LogShipper? shipper = null;
            (long Head, long Catalog)? shipped = null;

            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    // Sampled before the round: what the source's store held then, the target holds after it.
                    (long Head, long Catalog) now = (Dataset.Head.Value, Dataset.CatalogVersion.Value);

                    if (!_holdOff.Round() && (shipper is null || !Writes || shipped != now))
                    {
                        shipper ??= await LogShipper.StartAsync(Options(), stopping).ConfigureAwait(false);

                        if (shipper is not null)
                        {
                            ShippedRound round = await shipper.ShipAsync(stopping).ConfigureAwait(false);
                            shipped = now;
                            _holdOff.Succeeded();
                            Report(ShippingState.Shipping, Writes ? now.Head : null, round.Catalog?.Position.Value, null);
                        }
                    }
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception stop) when (stop is ShippingDivergedException or ShippingTargetNotEmptyException or ReplicaPromotedException)
                {
                    // The shipper stops for this target and changes nothing (ADR 0184 decision 3, ADR 0185).
                    string state = stop switch
                    {
                        ShippingDivergedException => ShippingState.Diverged,
                        ShippingTargetNotEmptyException => ShippingState.TargetNotEmpty,
                        _ => ShippingState.Promoted,
                    };
                    Report(state, null, null, stop.Message);
                    ShippingLog.Stopped(_owner._logger, _dataset, Key, state, stop.Message);
                    return;
                }
                catch (Exception failure) when (failure is IOException or HttpRequestException or TimeoutException or StoreUnverifiedException or InvalidOperationException or LogVerificationException or UnauthorizedAccessException)
                {
                    shipper = null;
                    _holdOff.Failed();
                    string state = failure switch
                    {
                        StoreUnverifiedException => ShippingState.StoreUnverified,
                        InvalidOperationException => ShippingState.TargetUnreachable,
                        _ => ShippingState.Retrying,
                    };
                    Report(state, null, null, failure.Message);

                    if (_holdOff.Failures == 1)
                    {
                        ShippingLog.Failed(_owner._logger, _dataset, Key, state, failure.Message);
                    }
                }

                try
                {
                    await Task.Delay(_owner._settings.ShipInterval, _owner._clock, stopping).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private ShippingOptions Options()
        {
            // The provider is one the compatibility page of this build lists as verified (ADR 0184 decision 9).
            if (CompatibilityPage.StateOf(Target.Provider!) is var state && state != CompatibilityPage.Verified)
            {
                throw new StoreUnverifiedException("The shipping target's provider '" + Target.Provider + "' is " + state
                    + " on the compatibility page of this build (docs/operator/object-stores.md). Nothing was written to it.");
            }

            ObjectStores objects = _owner._objects!;
            FleetCopy fleet = _owner.FleetCopyOf(Target.Store!);
            string digest = DigestOf(Key);
            string staging = objects.StagingOf(_dataset, digest);
            Directory.CreateDirectory(staging);

            return new ShippingOptions
            {
                Source = objects.Store,
                SourcePrefix = ObjectStores.PrefixOf(_dataset),
                Target = objects.OpenTarget(Target.Store!, _owner._settings),
                TargetPrefix = new ObjectName(Target.Prefix + "/"),
                Lineage = new LineageSource(new StoreAddress(objects.Address), ObjectStores.PrefixOf(_dataset), Dataset.Id, _owner._system().Id),
                BeforeLogWrite = stopping => fleet.ShipAsync(Writes, stopping),
                Reader = new ReaderId((objects.ConfiguredReader ?? objects.Instance.Value) + "-ship-" + digest),
                Holder = objects.Instance,
                Staging = new CacheDirectory(staging),
                Clock = _owner._clock,
            };
        }
    }

    private static partial class ShippingLog
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Shipping of dataset {Dataset} to {Target} failed ({State}): {Reason}. It is tried again with a back-off; the dataset is not affected.")]
        internal static partial void Failed(ILogger logger, string dataset, string target, string state, string reason);

        [LoggerMessage(Level = LogLevel.Error, Message = "Shipping of dataset {Dataset} to {Target} has stopped ({State}): {Reason}")]
        internal static partial void Stopped(ILogger logger, string dataset, string target, string state, string reason);
    }
}
