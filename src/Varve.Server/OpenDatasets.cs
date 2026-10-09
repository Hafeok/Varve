// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Varve.Protocol;
using Varve.Protocol.Model;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server;

/// <summary>
/// The server's datasets (ADRs 0093, 0101, 0106): the configured ones, opened
/// before the server listens, and every directory directly under the root
/// that holds a dataset, discovered at start — a directory that fails to
/// open is kept as <see cref="DatasetState.Failed"/> with its reason, never
/// skipped. The admin API creates, opens, closes and deletes through this
/// map. The name is the host's: a File dataset is the directory of that name
/// under the root.
/// </summary>
internal sealed partial class OpenDatasets : IDatasetResolver, IDatasetAdministration, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly SortedDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly string? _root;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _leaseWait;
    private readonly ILogger _logger;

    private OpenDatasets(string? root, TimeProvider clock, TimeSpan leaseWait, ILogger logger)
    {
        _root = root;
        _clock = clock;
        _leaseWait = leaseWait;
        _logger = logger;
    }

    internal static async Task<OpenDatasets> OpenAsync(ServerSettings settings, TimeProvider clock, ILogger logger, CancellationToken cancellationToken)
    {
        OpenDatasets opened = new(string.IsNullOrWhiteSpace(settings.DatasetsRoot) ? null : settings.DatasetsRoot, clock, settings.Lease.WaitFor, logger);

        try
        {
            foreach ((string name, DatasetSettings configured) in settings.Datasets)
            {
                Entry entry = new(configured.Storage == "Memory" ? DatasetStorage.Memory : DatasetStorage.File, DatasetOrigin.Configured);
                opened._entries[name] = entry;
                await opened.OpenEntryAsync(name, entry, cancellationToken).ConfigureAwait(false);

                if (entry.State == DatasetState.Failed)
                {
                    // A configured dataset that does not open is the 7a
                    // behaviour: the server does not start.
                    throw new InvalidOperationException("The dataset '" + name + "' did not open: " + entry.Reason);
                }
            }

            if (opened._root is { } root)
            {
                Directory.CreateDirectory(root);

                foreach (string directory in Directory.EnumerateDirectories(root))
                {
                    string name = Path.GetFileName(directory);

                    if (opened._entries.ContainsKey(name) || !DatasetName.TryParse(name, out _) || !Directory.Exists(Path.Combine(directory, "log")))
                    {
                        continue;
                    }

                    Entry entry = new(DatasetStorage.File, DatasetOrigin.Discovered);
                    opened._entries[name] = entry;
                    await opened.OpenEntryAsync(name, entry, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            await opened.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return opened;
    }

    public bool TryResolve(DatasetName name, [NotNullWhen(true)] out Dataset? dataset)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(name.Value, out Entry? entry) && entry.State == DatasetState.Open)
            {
                dataset = entry.Dataset;
                return dataset is not null;
            }
        }

        dataset = null;
        return false;
    }

    public IReadOnlyList<DatasetEntry> List()
    {
        List<DatasetEntry> list = [];

        lock (_gate)
        {
            foreach ((string name, Entry entry) in _entries)
            {
                Dataset? dataset = entry.Dataset;
                list.Add(new DatasetEntry(
                    new DatasetName(name),
                    entry.State,
                    entry.Storage,
                    entry.Origin,
                    entry.Reason,
                    dataset?.Id,
                    dataset?.Head));
            }
        }

        return list;
    }

    public async ValueTask<AdminOutcome> CreateAsync(DatasetName name, DatasetStorage storage, CancellationToken cancellationToken)
    {
        Entry entry = new(storage, DatasetOrigin.Created);

        lock (_gate)
        {
            if (_entries.ContainsKey(name.Value) || (storage == DatasetStorage.File && _root is { } root && Directory.Exists(Path.Combine(root, name.Value))))
            {
                return AdminOutcome.Exists;
            }

            if (storage == DatasetStorage.File && _root is null)
            {
                entry.Fail("Varve:DatasetsRoot is not configured, so a File dataset has nowhere to be created.");
                return AdminOutcome.Failed;
            }

            _entries[name.Value] = entry;
        }

        await OpenEntryAsync(name.Value, entry, cancellationToken).ConfigureAwait(false);
        return entry.State == DatasetState.Open ? AdminOutcome.Done : AdminOutcome.Failed;
    }

    public async ValueTask<AdminOutcome> OpenAsync(DatasetName name, CancellationToken cancellationToken)
    {
        Entry? entry;

        lock (_gate)
        {
            if (!_entries.TryGetValue(name.Value, out entry))
            {
                // A directory an operator copied under the root since the start.
                if (_root is { } root && Directory.Exists(Path.Combine(root, name.Value, "log")))
                {
                    entry = new Entry(DatasetStorage.File, DatasetOrigin.Discovered);
                    _entries[name.Value] = entry;
                }
                else
                {
                    return AdminOutcome.NotFound;
                }
            }

            if (entry.State == DatasetState.Open)
            {
                return AdminOutcome.Done;
            }
        }

        await OpenEntryAsync(name.Value, entry, cancellationToken).ConfigureAwait(false);
        return entry.State == DatasetState.Open ? AdminOutcome.Done : AdminOutcome.Failed;
    }

    public async ValueTask<AdminOutcome> CloseAsync(DatasetName name, CancellationToken cancellationToken)
    {
        Entry? entry;

        lock (_gate)
        {
            if (!_entries.TryGetValue(name.Value, out entry) || entry.State != DatasetState.Open)
            {
                return AdminOutcome.NotFound;
            }

            // Closed first, so that no new request resolves it while it drains.
            entry.Closing();
        }

        await entry.ReleaseAsync().ConfigureAwait(false);
        return AdminOutcome.Done;
    }

    public async ValueTask<AdminOutcome> DeleteAsync(DatasetName name, CancellationToken cancellationToken)
    {
        Entry? entry;

        lock (_gate)
        {
            if (!_entries.TryGetValue(name.Value, out entry))
            {
                return AdminOutcome.NotFound;
            }

            if (entry.State == DatasetState.Open)
            {
                return AdminOutcome.Open;
            }

            _entries.Remove(name.Value);
        }

        await entry.ReleaseAsync().ConfigureAwait(false);

        if (entry.Storage == DatasetStorage.File && _root is { } root)
        {
            string directory = Path.Combine(root, name.Value);

            if (Directory.Exists(directory))
            {
                // The log, the derived data and the lease file: all of it, and not undoable (ADR 0106).
                Directory.Delete(directory, recursive: true);
            }
        }

        return AdminOutcome.Done;
    }

    /// <summary>
    /// Closes every dataset: each drains its sequencer, so a commit in progress
    /// finishes, then releases its lease (ADR 0101). The active segment is not
    /// sealed.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        List<Entry> entries;

        lock (_gate)
        {
            entries = [.. _entries.Values];
            _entries.Clear();
        }

        foreach (Entry entry in entries)
        {
            await entry.ReleaseAsync().ConfigureAwait(false);
        }
    }

    // Opens the entry's dataset; on failure the entry is Failed with the
    // reason and nothing is held.
    private async Task OpenEntryAsync(string name, Entry entry, CancellationToken cancellationToken)
    {
        DatasetOptions options = new() { Clock = _clock, Name = name };

        try
        {
            if (entry.Storage == DatasetStorage.Memory)
            {
                entry.Opened(await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), options, cancellationToken).ConfigureAwait(false), null);
                return;
            }

            string directory = Path.Combine(_root!, name);
            bool exists = Directory.Exists(Path.Combine(directory, "log"));
            FileStorage files = await LeaseAsync(name, directory, cancellationToken).ConfigureAwait(false);

            try
            {
                Dataset dataset = exists
                    ? await Dataset.OpenAsync(files, options, cancellationToken).ConfigureAwait(false)
                    : await Dataset.CreateAsync(files, new DatasetId(Guid.NewGuid()), options, cancellationToken).ConfigureAwait(false);
                entry.Opened(dataset, files);
            }
            catch
            {
                await files.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            entry.Fail(error.Message);
        }
    }

    // A refused lease is waited for, never taken (ADR 0116): a predecessor
    // may still be draining. Retried each second for Varve:Lease:WaitFor,
    // logging who holds it; then the exception stands and the entry fails
    // with the holder as its reason.
    private async Task<FileStorage> LeaseAsync(string name, string directory, CancellationToken cancellationToken)
    {
        DateTimeOffset started = _clock.GetUtcNow();

        while (true)
        {
            try
            {
                return await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = _clock }, cancellationToken).ConfigureAwait(false);
            }
            catch (DatasetLeasedException held) when (_clock.GetUtcNow() - started < _leaseWait)
            {
                LeaseLog.Waiting(_logger, name, held.Message);
                await Task.Delay(TimeSpan.FromSeconds(1), _clock, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static partial class LeaseLog
    {
        [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "The dataset '{Name}' is leased elsewhere; waiting for it to be released (ADR 0116): {Holder}")]
        internal static partial void Waiting(ILogger logger, string name, string holder);
    }

    /// <summary>One dataset's slot: its storage, its state, and what it holds while open.</summary>
    private sealed class Entry(DatasetStorage storage, DatasetOrigin origin)
    {
        internal DatasetStorage Storage { get; } = storage;

        internal DatasetOrigin Origin { get; } = origin;

        internal DatasetState State { get; private set; } = DatasetState.Closed;

        internal string? Reason { get; private set; }

        internal Dataset? Dataset { get; private set; }

        private FileStorage? Files { get; set; }

        internal void Opened(Dataset dataset, FileStorage? files)
        {
            Dataset = dataset;
            Files = files;
            State = DatasetState.Open;
            Reason = null;
        }

        internal void Fail(string reason)
        {
            State = DatasetState.Failed;
            Reason = reason;
        }

        internal void Closing()
        {
            State = DatasetState.Closed;
            Reason = null;
        }

        internal async ValueTask ReleaseAsync()
        {
            Dataset? dataset = Dataset;
            FileStorage? files = Files;
            Dataset = null;
            Files = null;

            if (dataset is not null)
            {
                await dataset.DisposeAsync().ConfigureAwait(false);
            }

            if (files is not null)
            {
                await files.DisposeAsync().ConfigureAwait(false);
            }

            if (State == DatasetState.Open)
            {
                State = DatasetState.Closed;
            }
        }
    }
}
