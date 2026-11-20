// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store;
using Varve.Store.Azure;
using Varve.Store.Azure.Model;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Varve.Store.S3;
using Varve.Store.S3.Model;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Server;

/// <summary>
/// The server's object store (ADRs 0134–0139): one container or bucket from
/// <c>Varve:ObjectStorage</c>, a dataset per prefix <c>{name}/</c>, each with
/// its local cache under <c>CachePath/{name}</c>, and the fleet's system
/// dataset under <c>_fleet/</c> (ADR 0163). The provider's adapter is made
/// once and shared; a dataset opens for write, claiming the next epoch (ADR
/// 0136), or read-only, as a follower (ADR 0164).
/// </summary>
internal sealed class ObjectStores : IDisposable
{
    private const string Manifest = "/log/MANIFEST";

    private readonly ObjectStorageSettings _settings;
    private readonly Lock _targetsLock = new();
    private readonly Dictionary<string, IObjectStore> _targets = new(StringComparer.Ordinal);

    private ObjectStores(IObjectStore store, ObjectStorageSettings settings, TimeProvider clock)
    {
        Store = store;
        _settings = settings;
        Clock = clock;
    }

    /// <summary>The server's store, or null when no provider is configured.</summary>
    internal static ObjectStores? From(ObjectStorageSettings settings, TimeProvider clock)
    {
        IObjectStore? store = settings.Provider switch
        {
            "Azure" => AzureBlobStore.Create(new AzureBlobStoreOptions { ContainerUrl = new Uri(settings.ContainerUrl!), Clock = clock }),
            "S3Compatible" => S3ObjectStore.Create(new S3ObjectStoreOptions
            {
                Endpoint = new Uri(settings.Endpoint!),
                Bucket = new S3Bucket(settings.Bucket!),
                Region = new S3Region(settings.Region!),
                StsEndpoint = settings.StsEndpoint is { Length: > 0 } sts ? new Uri(sts) : null,
                Clock = clock,
            }),
            _ => null,
        };

        return store is null ? null : new ObjectStores(store, settings, clock);
    }

    /// <summary>The server's view of a store it is given, for a fleet run in one process by a test (#332).</summary>
    internal static ObjectStores Over(IObjectStore store, ObjectStorageSettings settings, TimeProvider clock) => new(store, settings, clock);

    /// <summary>The server's store at its root, as a shipper reads it (ADR 0184).</summary>
    internal IObjectStore Store { get; }

    /// <summary>The address of the server's store, as a lineage record names a source (ADR 0185): its container, or <c>s3://{bucket}</c>.</summary>
    internal string Address => _settings.Provider == "Azure" ? _settings.ContainerUrl! : "s3://" + _settings.Bucket;

    /// <summary>The prefix of a dataset in the server's store.</summary>
    internal static ObjectName PrefixOf(string name) => new(name + "/");

    /// <summary>The directory under the cache in which a shipper stages what it copies to one target.</summary>
    internal string StagingOf(string name, string target) => Path.Combine(_settings.CachePath!, "_shipping", name, target);

    /// <summary>The reader id this instance announces under, or none when it is not configured.</summary>
    internal string? ConfiguredReader => string.IsNullOrWhiteSpace(_settings.ReaderId) ? null : _settings.ReaderId;

    /// <summary>A test's target stores by address, in place of the providers' adapters (#332).</summary>
    internal Func<string, IObjectStore?>? TargetsForTests { get; set; }

    /// <summary>
    /// A shipping target's store at its root (ADR 0184), reached as this
    /// instance's identity: an Azure container by its address, an
    /// S3-compatible bucket by the endpoint and region
    /// <c>Varve:Replication:Stores</c> gives for it. One adapter per address.
    /// </summary>
    /// <exception cref="InvalidOperationException">The address is an S3 bucket this instance has no endpoint for.</exception>
    internal IObjectStore OpenTarget(string address, ReplicationSettings replication)
    {
        lock (_targetsLock)
        {
            if (_targets.TryGetValue(address, out IObjectStore? open))
            {
                return open;
            }

            IObjectStore store = TargetsForTests?.Invoke(address) ?? Create(address, replication, Clock);
            _targets[address] = store;
            return store;
        }
    }

    private static IObjectStore Create(string address, ReplicationSettings replication, TimeProvider clock)
    {
        if (!address.StartsWith("s3://", StringComparison.Ordinal))
        {
            return AzureBlobStore.Create(new AzureBlobStoreOptions { ContainerUrl = new Uri(address), Clock = clock });
        }

        ReplicationStoreSettings? reached = replication.Stores.Find(store => string.Equals(store.Address, address, StringComparison.Ordinal));

        if (reached is null || string.IsNullOrEmpty(reached.Endpoint) || string.IsNullOrEmpty(reached.Region))
        {
            throw new InvalidOperationException("This instance has no endpoint for the shipping target '" + address + "': name it under Varve:Replication:Stores with its Address, Endpoint and Region (ADR 0184).");
        }

        return S3ObjectStore.Create(new S3ObjectStoreOptions
        {
            Endpoint = new Uri(reached.Endpoint),
            Bucket = new S3Bucket(address["s3://".Length..].TrimEnd('/')),
            Region = new S3Region(reached.Region),
            StsEndpoint = reached.StsEndpoint is { Length: > 0 } sts ? new Uri(sts) : null,
            Clock = clock,
        });
    }

    /// <summary>Who this server is in a writer record (ADR 0136): the configured instance, or the machine name.</summary>
    internal WriterInstance Instance => new(string.IsNullOrWhiteSpace(_settings.Instance) ? Environment.MachineName : _settings.Instance);

    /// <summary>The writer record of a dataset's prefix, as the store holds it now; none before the first claim.</summary>
    internal async Task<WriterRecord?> ReadWriterAsync(string name, CancellationToken cancellationToken) =>
        await Backend.ReadWriterAsync(Store, new ObjectName(name + "/"), cancellationToken).ConfigureAwait(false);

    /// <summary>Whether a dataset is a replica, or a copy whose promotion did not finish (ADR 0185): read before this instance claims it.</summary>
    internal async Task<ReplicaState> ReplicaStateAsync(string name, CancellationToken cancellationToken) =>
        await Backend.ReplicaStateAsync(Store, PrefixOf(name), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// The position a copy was promoted at, or none for a dataset that is no
    /// promoted copy (ADR 0185 decision 8): every commit at or below it was
    /// written under another fleet, whose management positions mean nothing
    /// here.
    /// </summary>
    internal async Task<Position?> PromotedAtAsync(string name, CancellationToken cancellationToken) =>
        (await Backend.ReadLineageAsync(Store, PrefixOf(name), cancellationToken).ConfigureAwait(false))?.Promoted?.Position;

    /// <summary>A dataset's lineage record, or none for a dataset that is no copy (ADR 0185).</summary>
    internal async Task<LineageRecord?> LineageAsync(string name, CancellationToken cancellationToken) =>
        await Backend.ReadLineageAsync(Store, PrefixOf(name), cancellationToken).ConfigureAwait(false);

    /// <summary>The clock this instance's storages wait on.</summary>
    internal TimeProvider Clock { get; }

    /// <summary>
    /// The system dataset of another fleet as its shipper wrote it beside the
    /// copies this fleet serves (ADR 0185 decision 8), under
    /// <c>fleets/{fleet id}/</c>, opened read-only; none where the store
    /// holds no such copy.
    /// </summary>
    internal async Task<Backend?> OpenShippedFleetAsync(DatasetId fleet, CancellationToken cancellationToken)
    {
        string name = "fleets/" + fleet.Value.ToString("D");

        if (!await ExistsAsync(name, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        string cache = Path.Combine(_settings.CachePath!, "_fleets", fleet.Value.ToString("D"));
        Directory.CreateDirectory(cache);

        return await Backend.OpenAsync(new ObjectStorageOptions
        {
            Store = Store,
            Prefix = new ObjectName(name + "/"),
            Cache = new CacheDirectory(cache),
            Access = ObjectStorageAccess.ReadOnly,
            CacheBytes = new ByteCount(_settings.CacheBytes),
            MaxObjectBytes = _settings.Provider == "Azure" ? AzureBlobStore.MaxSingleUploadBytes : S3ObjectStore.MaxSingleUploadBytes,
            Clock = Clock,
            ProbeRandomness = SystemProbeRandomness.Instance,
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether the store holds a dataset of this name: its manifest.</summary>
    internal async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken) =>
        await Store.ReadAsync(new ObjectName(name + Manifest), new ByteOffset(0), new ByteCount(1), cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>
    /// Opens a dataset's storage: for write, the next epoch is claimed, the log
    /// fenced with it, and <paramref name="address"/> recorded for forwarding
    /// (ADRs 0136, 0168); read-only, for a follower, nothing is claimed.
    /// </summary>
    internal async Task<Backend> OpenAsync(string name, ObjectStorageAccess access, WriterAddress? address, CancellationToken cancellationToken)
    {
        // A follower caches apart from the writer, so that the two can be open
        // at once while a placement changes; no dataset name starts with '_'.
        string cache = access == ObjectStorageAccess.Write ? Path.Combine(_settings.CachePath!, name) : Path.Combine(_settings.CachePath!, "_followers", name);
        Directory.CreateDirectory(cache);

        return await Backend.OpenAsync(new ObjectStorageOptions
        {
            Store = Store,
            Prefix = new ObjectName(name + "/"),
            Cache = new CacheDirectory(cache),
            Access = access,
            Instance = access == ObjectStorageAccess.Write ? Instance : null,
            Address = access == ObjectStorageAccess.Write ? address : null,
            CacheBytes = new ByteCount(_settings.CacheBytes),
            Reader = ReaderOf(access),
            MaxObjectBytes = _settings.Provider == "Azure" ? AzureBlobStore.MaxSingleUploadBytes : S3ObjectStore.MaxSingleUploadBytes,
            Clock = Clock,
            ProbeRandomness = SystemProbeRandomness.Instance,
        }, cancellationToken).ConfigureAwait(false);
    }

    // A writer and a follower of one dataset can be open here at once while a
    // placement changes, each with a reader record of its own (ADR 0160): a
    // configured id names the writer's, and the follower's is it with
    // "-follower". Unconfigured, each keeps its own id in its own cache.
    private ReaderId? ReaderOf(ObjectStorageAccess access) => string.IsNullOrWhiteSpace(_settings.ReaderId)
        ? null
        : new ReaderId(access == ObjectStorageAccess.Write ? _settings.ReaderId : _settings.ReaderId + "-follower");

    /// <summary>Deletes every object of a dataset, and its cache: all of it, and not undoable (ADR 0106).</summary>
    internal async Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        foreach (ObjectEntry entry in await Store.ListAsync(new ObjectName(name + "/"), cancellationToken).ConfigureAwait(false))
        {
            await Store.DeleteAsync(entry.Name, cancellationToken).ConfigureAwait(false);
        }

        string cache = Path.Combine(_settings.CachePath!, name);

        if (Directory.Exists(cache))
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    public void Dispose()
    {
        (Store as IDisposable)?.Dispose();

        lock (_targetsLock)
        {
            foreach (IObjectStore target in _targets.Values)
            {
                (target as IDisposable)?.Dispose();
            }

            _targets.Clear();
        }
    }
}

/// <summary>The operating system's generator, for a writer's probe key (ADR 0134).</summary>
internal sealed class SystemProbeRandomness : IProbeRandomness
{
    internal static SystemProbeRandomness Instance { get; } = new();

    public void Fill(Span<byte> destination) => System.Security.Cryptography.RandomNumberGenerator.Fill(destination);
}
