// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Varve.Protocol.Model;
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
/// its local cache under <c>CachePath/{name}</c>. The provider's adapter is
/// made once and shared; each dataset opens for write, claiming the next
/// epoch (ADR 0136).
/// </summary>
internal sealed class ObjectStores : IDisposable
{
    private const string Manifest = "/log/MANIFEST";

    private readonly IObjectStore _store;
    private readonly ObjectStorageSettings _settings;
    private readonly TimeProvider _clock;

    private ObjectStores(IObjectStore store, ObjectStorageSettings settings, TimeProvider clock)
    {
        _store = store;
        _settings = settings;
        _clock = clock;
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
                Clock = clock,
            }),
            _ => null,
        };

        return store is null ? null : new ObjectStores(store, settings, clock);
    }

    /// <summary>
    /// The names of the datasets the store holds: every prefix with a manifest
    /// under it. A dataset name starts with a letter or a digit (ADR 0093), so
    /// the store is listed by each of those, and no name is missed.
    /// </summary>
    internal async Task<List<string>> DiscoverAsync(CancellationToken cancellationToken)
    {
        List<string> names = [];
        const string Starts = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

        foreach (char start in Starts)
        {
            foreach (ObjectEntry entry in await _store.ListAsync(new ObjectName(start.ToString()), cancellationToken).ConfigureAwait(false))
            {
                string value = entry.Name.Value;

                if (value.EndsWith(Manifest, StringComparison.Ordinal) && value.IndexOf('/') == value.Length - Manifest.Length && DatasetName.TryParse(value[..^Manifest.Length], out _))
                {
                    names.Add(value[..^Manifest.Length]);
                }
            }
        }

        return names;
    }

    /// <summary>Whether the store holds a dataset of this name: its manifest.</summary>
    internal async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken) =>
        await _store.ReadAsync(new ObjectName(name + Manifest), new ByteOffset(0), new ByteCount(1), cancellationToken).ConfigureAwait(false) is not null;

    /// <summary>Opens a dataset's storage for write: the next epoch is claimed, and the log fenced with it.</summary>
    internal async Task<Backend> OpenAsync(string name, CancellationToken cancellationToken)
    {
        string cache = Path.Combine(_settings.CachePath!, name);
        Directory.CreateDirectory(cache);

        return await Backend.OpenAsync(new ObjectStorageOptions
        {
            Store = _store,
            Prefix = new ObjectName(name + "/"),
            Cache = new CacheDirectory(cache),
            Access = ObjectStorageAccess.Write,
            Instance = new WriterInstance(string.IsNullOrWhiteSpace(_settings.Instance) ? Environment.MachineName : _settings.Instance),
            CacheBytes = new ByteCount(_settings.CacheBytes),
            Reader = string.IsNullOrWhiteSpace(_settings.ReaderId) ? null : new ReaderId(_settings.ReaderId),
            MaxObjectBytes = _settings.Provider == "Azure" ? AzureBlobStore.MaxSingleUploadBytes : S3ObjectStore.MaxSingleUploadBytes,
            Clock = _clock,
            ProbeRandomness = SystemProbeRandomness.Instance,
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes every object of a dataset, and its cache: all of it, and not undoable (ADR 0106).</summary>
    internal async Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        foreach (ObjectEntry entry in await _store.ListAsync(new ObjectName(name + "/"), cancellationToken).ConfigureAwait(false))
        {
            await _store.DeleteAsync(entry.Name, cancellationToken).ConfigureAwait(false);
        }

        string cache = Path.Combine(_settings.CachePath!, name);

        if (Directory.Exists(cache))
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    public void Dispose() => (_store as IDisposable)?.Dispose();
}

/// <summary>The operating system's generator, for a writer's probe key (ADR 0134).</summary>
internal sealed class SystemProbeRandomness : IProbeRandomness
{
    internal static SystemProbeRandomness Instance { get; } = new();

    public void Fill(Span<byte> destination) => System.Security.Cryptography.RandomNumberGenerator.Fill(destination);
}
