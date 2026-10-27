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
using Varve.Management.Model;
using Varve.Protocol;
using Varve.Protocol.Model;
using Varve.Store;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Server;

/// <summary>
/// The server's datasets (ADRs 0093, 0101, 0163, 0164): the ones the
/// management plane declares, at the position this instance has applied, each
/// opened in its role. A dataset placed on this instance is opened as its
/// writer; one in the object store placed elsewhere is followed (ADR 0164);
/// a file or memory dataset placed elsewhere is not held here. A dataset
/// declared closed is closed on every instance; one no longer declared is
/// released, and its writer deletes its storage. A dataset that fails to open
/// is kept as <see cref="DatasetState.Failed"/> with its reason, and tried
/// again on every round.
/// </summary>
internal sealed partial class OpenDatasets : IDatasetResolver, IDatasetAdministration, IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly SortedDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly string? _root;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _leaseWait;
    private readonly ILogger _logger;
    private readonly GroupCommitOptions _groupCommit;
    private readonly ExtentMergePolicy _merges;
    private readonly ObjectStores? _objects;
    private readonly ManagementOrder _order;
    private readonly WriterAddress? _address;
    private readonly Func<string, bool> _moved;

    internal OpenDatasets(ServerSettings settings, ObjectStores? objects, ManagementOrder order, TimeProvider clock, ILogger logger, Func<string, bool> moved)
    {
        _moved = moved;
        _objects = objects;
        _order = order;
        _groupCommit = new GroupCommitOptions
        {
            MaxRecords = settings.Store.GroupCommit.MaxRecords,
            MaxBytes = new ByteCount(settings.Store.GroupCommit.MaxBytes),
        };
        _root = string.IsNullOrWhiteSpace(settings.DatasetsRoot) ? null : settings.DatasetsRoot;
        _clock = clock;
        _leaseWait = settings.Lease.WaitFor;
        _logger = logger;
        _address = settings.Fleet.InternalUrl is { } url ? new WriterAddress(url) : null;
        _merges = new ExtentMergePolicy
        {
            Enabled = settings.Store.ExtentMerges.Enabled,
            FanOut = settings.Store.ExtentMerges.FanOut,
            MaxSetBytes = new ByteCount(settings.Store.ExtentMerges.MaxSetBytes),
        };
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

    /// <summary>The local derived store of an open dataset (ADR 0174): a file dataset's; null otherwise.</summary>
    internal IDerivedStore? LocalDerivedOf(DatasetName name)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(name.Value, out Entry? entry) && entry.State == DatasetState.Open ? entry.LocalDerived : null;
        }
    }

    /// <summary>This instance's role in a dataset; none when it does not know the dataset.</summary>
    internal DatasetRole? RoleOf(DatasetName name)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(name.Value, out Entry? entry) ? entry.Role : null;
        }
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
                    DatasetOrigin.Declared,
                    entry.Reason,
                    dataset?.Id,
                    dataset?.Head,
                    entry.Role));
            }
        }

        return list;
    }

    // Datasets are declared (ADR 0163), never opened or closed by an
    // imperative call: the management endpoints commit
    // declarations, and every instance converges to them.
    public ValueTask<AdminOutcome> OpenAsync(DatasetName name, CancellationToken cancellationToken) => ValueTask.FromResult(AdminOutcome.Failed);

    public ValueTask<AdminOutcome> CloseAsync(DatasetName name, CancellationToken cancellationToken) => ValueTask.FromResult(AdminOutcome.Failed);

    /// <summary>
    /// Converges the datasets to <paramref name="declarations"/>, as
    /// <paramref name="placement"/> places them, and their settings to the
    /// declared ones at <paramref name="applied"/>. A failed dataset is tried
    /// again, and a placement waiting on a release checked, on every call.
    /// </summary>
    internal async Task ReconcileAsync(Declarations declarations, ManagementPosition applied, Placement placement, CancellationToken cancellationToken)
    {
        List<(string Name, Entry Entry)> gone = [];

        lock (_gate)
        {
            foreach ((string name, Entry entry) in _entries)
            {
                if (!declarations.Datasets.ContainsKey(name))
                {
                    gone.Add((name, entry));
                }
            }

            foreach ((string name, _) in gone)
            {
                _entries.Remove(name);
            }
        }

        foreach ((string name, Entry entry) in gone)
        {
            bool wrote = entry.Role == DatasetRole.Writer;
            await entry.ReleaseAsync().ConfigureAwait(false);

            if (wrote)
            {
                // The writer deletes what it wrote: all of it, and not undoable (ADR 0106).
                await DeleteStorageAsync(name, entry.Storage, cancellationToken).ConfigureAwait(false);
                ReconcileLog.Deleted(_logger, name);
            }
        }

        foreach ((string name, DatasetDeclaration declared) in declarations.Datasets)
        {
            DatasetStorage storage = declared.Storage switch
            {
                StorageKind.Memory => DatasetStorage.Memory,
                StorageKind.ObjectStorage => DatasetStorage.ObjectStorage,
                _ => DatasetStorage.File,
            };
            DatasetRole role = placement.RoleOf(declared, storage);
            Entry? entry;
            bool fresh = false;

            lock (_gate)
            {
                if (!_entries.TryGetValue(name, out entry))
                {
                    entry = new Entry(storage);
                    _entries[name] = entry;
                    fresh = true;
                }
            }

            await ConvergeAsync(name, entry, role, declared, placement, fresh, cancellationToken).ConfigureAwait(false);

            if (entry.State == DatasetState.Open && entry.Role == DatasetRole.Writer && entry.Dataset is { } written)
            {
                await ConvergeSettingsAsync(new DatasetName(name), written, declared, applied, placement, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // Settings stay a commit kind of the dataset (ADR 0021), and its writer
    // converges them (ADR 0163 point 10): when the declared settings differ
    // from the dataset's, a Settings commit carries the management position
    // it converged to (ADR 0165). Its agent is the writer instance; its cause
    // names the position.
    private async Task ConvergeSettingsAsync(DatasetName name, Dataset dataset, DatasetDeclaration declared, ManagementPosition applied, Placement placement, CancellationToken cancellationToken)
    {
        if (declared.DefaultAccessScope is not { } scope)
        {
            return;
        }

        AccessScope wanted = scope == HistoryScope.Current ? AccessScope.Current : AccessScope.AllHistory;

        if (dataset.Settings.DefaultAccessScope == wanted)
        {
            return;
        }

        string position = applied.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        CommitMetadata metadata = new()
        {
            Agent = Varve.Rdf.RdfTerm.Iri(System.Text.Encoding.UTF8.GetBytes("urn:varve:writer:" + Uri.EscapeDataString(placement.Instance.Value))),
            Cause = Varve.Rdf.RdfTerm.Literal(System.Text.Encoding.UTF8.GetBytes("the declared settings at management position " + position)),
            Attachments = [Varve.Rdf.RdfTerm.Literal(System.Text.Encoding.UTF8.GetBytes(position), Varve.Rdf.RdfTerm.Iri(System.Text.Encoding.UTF8.GetBytes(ManagementOrder.PositionDatatype)))],
        };

        try
        {
            CommitResult result = await dataset.ChangeSettingsAsync(new SettingsChange { DefaultAccessScope = wanted }, metadata, null, cancellationToken).ConfigureAwait(false);

            if (result.Outcome == CommitOutcome.Committed)
            {
                _order.Observe(name, applied);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Tried again on the next round.
            ReconcileLog.SettingsFailed(_logger, name.Value, error.Message);
        }
    }

    private async Task ConvergeAsync(string name, Entry entry, DatasetRole role, DatasetDeclaration declared, Placement placement, bool fresh, CancellationToken cancellationToken)
    {
        if (declared.Closed)
        {
            if (entry.State != DatasetState.Closed)
            {
                await entry.ReleaseAsync().ConfigureAwait(false);
            }

            entry.Placed(role);
            return;
        }

        if (entry.State == DatasetState.Open && entry.Role == DatasetRole.Follower && role == DatasetRole.Follower && !entry.AwaitingRelease && entry.Dataset is { ReopenDue: true })
        {
            // A follower that has applied more than MemtableLimit since its
            // open is opened again from the writer's newest catalog (ADR 0164
            // point 5); reads pinned to the old one finish on it.
            ReconcileLog.Reopened(_logger, name);
            await SwitchAsync(name, entry, DatasetRole.Follower, wait: false, abdicate: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (entry.State == DatasetState.Open && entry.Role == role && !entry.AwaitingRelease && !(role == DatasetRole.Writer && entry.Superseded))
        {
            return;
        }

        if (entry.State == DatasetState.Open && entry.Role == DatasetRole.Writer && role == DatasetRole.Writer && entry.Superseded)
        {
            // Superseded where it is placed: it stays superseded, serving
            // reads, and never claims on its own (ADR 0136), unless the newer
            // holder handed the dataset over by releasing it for placement
            // (ADR 0163 point 12), as a mistaken system writer does once it
            // learns it was superseded.
            WriterRecord? handed = await _objects!.ReadWriterAsync(name, cancellationToken).ConfigureAwait(false);

            if (handed is not { Released: ReleaseReason.Placement } || handed.Holder == placement.Instance)
            {
                return;
            }

            // The superseded writer holds the local cache's lease, which the
            // claim needs, and serves only reads: it is released first.
            await entry.ReleaseAsync().ConfigureAwait(false);
            await OpenEntryAsync(name, entry, DatasetRole.Writer, wait: true, cancellationToken).ConfigureAwait(false);
            return;
        }


        if (role != DatasetRole.Writer)
        {
            entry.AwaitingRelease = false;
        }
        else if (entry.Storage == DatasetStorage.ObjectStorage && (!fresh || entry.AwaitingRelease))
        {
            // A placement change, by events (ADR 0163 point 12): the newly
            // placed instance claims once it observes the old writer's
            // release. Until then it follows. At start an instance claims
            // its placement whatever the record says, fencing any
            // predecessor: the orchestrator's restart is the recovery. A
            // failed open tried again is not a start: it claims only from a
            // release too, or it would fence a writer placed since.
            WriterRecord? record = await _objects!.ReadWriterAsync(name, cancellationToken).ConfigureAwait(false);

            if (record is { Released: null } && record.Holder != placement.Instance)
            {
                if (entry.State != DatasetState.Open || entry.Role != DatasetRole.Follower)
                {
                    await SwitchAsync(name, entry, DatasetRole.Follower, wait: true, abdicate: false, cancellationToken).ConfigureAwait(false);
                }

                entry.AwaitingRelease = true;
                return;
            }

            entry.AwaitingRelease = false;
        }

        // Placed elsewhere: abdicate with the reason, so the new writer
        // claims on seeing it (ADR 0136 point 6).
        bool abdicate = entry.State == DatasetState.Open && entry.Role == DatasetRole.Writer && role != DatasetRole.Writer;

        // A dataset that failed is tried again every round, without the
        // lease wait its first open had: its holder may have released it.
        bool wait = fresh || entry.State != DatasetState.Failed;
        await SwitchAsync(name, entry, role, wait, abdicate, cancellationToken).ConfigureAwait(false);
    }

    // The dataset in its new role is opened first, then takes the entry's
    // place, and only then is the old one released: a request resolves one
    // or the other, never neither. A file dataset's lease is one per
    // directory, so it is released before it is taken again.
    private async Task SwitchAsync(string name, Entry entry, DatasetRole role, bool wait, bool abdicate, CancellationToken cancellationToken)
    {
        if (entry.Storage == DatasetStorage.File)
        {
            await entry.ReleaseAsync().ConfigureAwait(false);
            await OpenEntryAsync(name, entry, role, wait, cancellationToken).ConfigureAwait(false);
            return;
        }

        Entry staged = new(entry.Storage);
        await OpenEntryAsync(name, staged, role, wait, cancellationToken).ConfigureAwait(false);

        if (staged.State == DatasetState.Failed && entry.State == DatasetState.Open && entry.Role != DatasetRole.Elsewhere && role != DatasetRole.Elsewhere)
        {
            // The old role keeps serving; the new one is tried next round.
            ReconcileLog.SwitchFailed(_logger, name, staged.Reason ?? "no reason");
            return;
        }

        if (abdicate)
        {
            entry.ReleaseFor(ReleaseReason.Placement);
            ReconcileLog.Abdicated(_logger, name);
        }

        Entry previous;

        lock (_gate)
        {
            previous = entry.Take(staged);
        }

        await previous.ReleaseAsync().ConfigureAwait(false);
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

    /// <summary>
    /// The highest writer epoch an open follower's storage has adopted, or
    /// none: the writer directory compares it with its route's (ADR 0179
    /// point 12).
    /// </summary>
    internal long? AdoptedEpochOf(DatasetName name)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(name.Value, out Entry? entry) ? entry.AdoptedEpoch : null;
        }
    }

    /// <summary>
    /// Whether an open dataset's storage no longer holds its writer epoch
    /// (ADR 0136): readiness reports it <c>superseded</c>.
    /// </summary>
    internal bool IsSuperseded(DatasetName name)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(name.Value, out Entry? entry) && entry.Superseded;
        }
    }

    // Opens the entry's dataset in the role; on failure the entry is Failed
    // with the reason and nothing is held.
    private async Task OpenEntryAsync(string name, Entry entry, DatasetRole role, bool wait, CancellationToken cancellationToken)
    {
        entry.Placed(role);

        if (role == DatasetRole.Elsewhere)
        {
            return;
        }

        // The bounds are a durable backend's; the memory backend has no flush
        // for a batch to share, and writes each request alone (ADR 0124).
        // The event catalogue's check gates every write path (ADR 0145).
        DatasetOptions options = new() { Clock = _clock, Name = name, GroupCommit = entry.Storage == DatasetStorage.Memory ? null : _groupCommit, ExtentMerges = _merges, Validators = [new EventCatalogueValidator(new DatasetName(name), () => _moved(name)), new ReportGraphGuard()] };

        try
        {
            if (entry.Storage == DatasetStorage.Memory)
            {
                entry.Opened(await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), options, cancellationToken).ConfigureAwait(false), null);
                return;
            }

            if (entry.Storage == DatasetStorage.ObjectStorage)
            {
                if (_objects is null)
                {
                    entry.Fail("Varve:ObjectStorage is not configured, so an ObjectStorage dataset has nowhere to be.");
                    return;
                }

                if (role == DatasetRole.Follower && !await _objects.ExistsAsync(name, cancellationToken).ConfigureAwait(false))
                {
                    entry.Fail("Its writer has not created it yet; it is followed once it has.");
                    return;
                }

                Backend objects = await _objects.OpenAsync(name, role == DatasetRole.Writer ? ObjectStorageAccess.Write : ObjectStorageAccess.ReadOnly, _address, cancellationToken).ConfigureAwait(false);

                try
                {
                    Dataset dataset;

                    if (role == DatasetRole.Follower)
                    {
                        dataset = await Dataset.OpenFollowerAsync(objects, options, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        bool held = !(await objects.Log.ReadManifestAsync(cancellationToken).ConfigureAwait(false)).IsEmpty;
                        dataset = held
                            ? await Dataset.OpenAsync(objects, options, cancellationToken).ConfigureAwait(false)
                            : await Dataset.CreateAsync(objects, new DatasetId(Guid.NewGuid()), options, cancellationToken).ConfigureAwait(false);
                    }

                    entry.Opened(dataset, objects);
                }
                catch
                {
                    await objects.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            else
            {
                if (_root is null)
                {
                    entry.Fail("Varve:DatasetsRoot is not configured, so a File dataset has nowhere to be.");
                    return;
                }

                string directory = Path.Combine(_root, name);
                bool exists = Directory.Exists(Path.Combine(directory, "log"));
                FileStorage files = await LeaseAsync(name, directory, wait ? _leaseWait : TimeSpan.Zero, cancellationToken).ConfigureAwait(false);

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

            if (role == DatasetRole.Writer && entry.Dataset is { } written)
            {
                await SeedOrderAsync(new DatasetName(name), written, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            entry.Fail(error.Message);
        }
    }

    // The highest management position the dataset records is its head
    // commit's (ADR 0165): positions never decrease along the log.
    private async Task SeedOrderAsync(DatasetName name, Dataset dataset, CancellationToken cancellationToken)
    {
        if (dataset.Head.Value == 0)
        {
            return;
        }

        await using IAsyncEnumerator<Commit> commits = dataset.Subscribe(new Position(dataset.Head.Value - 1), SubscriptionFilter.All, cancellationToken).GetAsyncEnumerator(cancellationToken);

        if (!await commits.MoveNextAsync().ConfigureAwait(false))
        {
            return;
        }

        Commit head = commits.Current;
        List<Varve.Rdf.RdfTerm> attachments = [];

        foreach (Varve.Rdf.TermHandle handle in head.Attachments.Span)
        {
            if (head.TryExternalise(handle, out Varve.Rdf.RdfTerm? term))
            {
                attachments.Add(term);
            }
        }

        if (ManagementOrder.PositionOf(attachments) is { } recorded)
        {
            _order.Observe(name, recorded);
        }
    }

    private async Task DeleteStorageAsync(string name, DatasetStorage storage, CancellationToken cancellationToken)
    {
        if (storage == DatasetStorage.ObjectStorage && _objects is { } objects)
        {
            await objects.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
        }

        if (storage == DatasetStorage.File && _root is { } root)
        {
            string directory = Path.Combine(root, name);

            if (Directory.Exists(directory))
            {
                // The log, the derived data and the lease file.
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // A refused lease is waited for, never taken (ADR 0116): a predecessor
    // may still be draining. Retried each second for Varve:Lease:WaitFor,
    // logging who holds it; then the exception stands and the entry fails
    // with the holder as its reason.
    private async Task<FileStorage> LeaseAsync(string name, string directory, TimeSpan leaseWait, CancellationToken cancellationToken)
    {
        DateTimeOffset started = _clock.GetUtcNow();

        while (true)
        {
            try
            {
                return await FileStorage.OpenAsync(new DatasetDirectory(directory), new FileStorageOptions { Clock = _clock }, cancellationToken).ConfigureAwait(false);
            }
            catch (DatasetLeasedException held) when (_clock.GetUtcNow() - started < leaseWait)
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

    private static partial class ReconcileLog
    {
        [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "The dataset '{Name}' is placed on another instance; this instance released its epoch with the reason placement (ADR 0163).")]
        internal static partial void Abdicated(ILogger logger, string name);

        [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "The dataset '{Name}' is no longer declared; its writer deleted its storage (ADR 0163).")]
        internal static partial void Deleted(ILogger logger, string name);

        [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "The dataset '{Name}' did not open in its new role, and keeps its old one until the next round: {Reason}")]
        internal static partial void SwitchFailed(ILogger logger, string name, string reason);

        [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "The dataset '{Name}' did not take its declared settings; tried again on the next round: {Reason}")]
        internal static partial void SettingsFailed(ILogger logger, string name, string reason);

        [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "The follower of '{Name}' applied more than its memtable limit since it opened, and opens again from the writer's newest catalog (ADR 0164).")]
        internal static partial void Reopened(ILogger logger, string name);
    }

    /// <summary>One dataset's slot: its storage, its role, its state, and what it holds while open.</summary>
    private sealed class Entry(DatasetStorage storage)
    {
        internal DatasetStorage Storage { get; } = storage;

        internal DatasetRole Role { get; private set; } = DatasetRole.Elsewhere;

        internal DatasetState State { get; private set; } = DatasetState.Closed;

        internal string? Reason { get; private set; }

        internal Dataset? Dataset { get; private set; }

        /// <summary>Placed here and following until the previous writer's release is seen.</summary>
        internal bool AwaitingRelease { get; set; }

        // The storage the dataset was opened on, released after it: a
        // directory's lease, or an object store's epoch (ADR 0136).
        private IAsyncDisposable? Files { get; set; }

        internal bool Superseded => Files is Backend { IsSuperseded: true };

        internal long? AdoptedEpoch => Files is Backend { AdoptedEpoch: { } epoch } ? epoch.Value : null;

        /// <summary>The derived store on this machine, where a projection of the server's own keeps its state (ADR 0174): a file dataset's; none for memory or an object store.</summary>
        internal IDerivedStore? LocalDerived => Files is FileStorage files ? files.Derived : null;

        internal void Placed(DatasetRole role) => Role = role;

        internal void Opened(Dataset dataset, IAsyncDisposable? files)
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

        /// <summary>
        /// Takes <paramref name="staged"/>'s role, state and what it holds;
        /// answers an entry holding what this one held, to release.
        /// </summary>
        internal Entry Take(Entry staged)
        {
            Entry previous = new(Storage) { Dataset = Dataset, Files = Files, State = State, Role = Role };
            Dataset = staged.Dataset;
            Files = staged.Files;
            State = staged.State;
            Reason = staged.Reason;
            Role = staged.Role;
            staged.Dataset = null;
            staged.Files = null;
            return previous;
        }

        internal void ReleaseFor(ReleaseReason reason)
        {
            if (Files is Backend objects)
            {
                objects.ReleaseOnClose = reason;
            }
        }

        internal async ValueTask ReleaseAsync()
        {
            Dataset? dataset = Dataset;
            IAsyncDisposable? files = Files;
            Dataset = null;
            Files = null;
            State = DatasetState.Closed;
            Reason = null;

            if (dataset is not null)
            {
                await dataset.DisposeAsync().ConfigureAwait(false);
            }

            if (files is not null)
            {
                await files.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// Where datasets are written (ADR 0163 point 12): this instance's name,
/// whether it may claim at all, whether it writes the system dataset, and
/// whether the fleet is one instance.
/// </summary>
internal sealed record Placement(WriterInstance Instance, bool MayClaim, bool SystemWriter, bool FleetOfOne)
{
    /// <summary>This instance's role in a declared dataset.</summary>
    internal DatasetRole RoleOf(DatasetDeclaration declared, DatasetStorage storage)
    {
        if (FleetOfOne)
        {
            return DatasetRole.Writer;
        }

        bool placedHere = declared.PlacedOn is { } placed ? placed == Instance.Value : SystemWriter;

        if (placedHere && MayClaim)
        {
            return DatasetRole.Writer;
        }

        return storage == DatasetStorage.ObjectStorage ? DatasetRole.Follower : DatasetRole.Elsewhere;
    }
}
