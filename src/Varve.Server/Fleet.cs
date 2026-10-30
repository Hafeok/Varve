// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Varve.Management;
using Varve.Management.Model;
using Varve.Protocol;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Varve.Store.ObjectStorage.Model;
using Backend = Varve.Store.ObjectStorage.ObjectStorage;

namespace Varve.Server;

/// <summary>
/// This instance's part in its fleet (ADRs 0163, 0164, 0165, 0168): the
/// system dataset, held as its writer or followed; the grants at the position
/// applied, pinned per request; the datasets, converged to the declarations;
/// the on-demand advance of followers; and the writer directory that tells
/// a write where to go. The system dataset is advanced on the interval; a
/// data follower only when a read arrives (ADR 0164).
/// </summary>
internal sealed partial class Fleet : IManagementPlane, IManagementAuthority, IDatasetFollowing, IWriterDirectory, IAsyncDisposable
{
    /// <summary>The system dataset's name in the store and under the root: no dataset name can take it (ADR 0093).</summary>
    internal const string SystemName = "_fleet";

    private const string SnapshotKey = "Varve.Server.Fleet.Snapshot";

    private readonly SemaphoreSlim _advance = new(1, 1);
    private readonly ConcurrentDictionary<string, Follow> _follows = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, WriterRoute> _routes = new(StringComparer.Ordinal);
    private readonly ServerSettings _settings;
    private readonly ObjectStores? _objects;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly IHttpContextAccessor _accessor;
    private readonly ManagementOrder _order;
    private readonly ShapesBindings _shapes = new();
    private readonly WriterInstance _instance;
    private readonly bool _mayClaim;
    private readonly string[] _bootstrap;
    private Dataset _system;
    private IAsyncDisposable? _systemStorage;
    private ManagementRole _role;
    private Snapshot _snapshot;
    private bool _placeholder;
    private bool _superseded;

    private Fleet(ServerSettings settings, ObjectStores? objects, TimeProvider clock, ILogger logger, IHttpContextAccessor accessor, Dataset system, IAsyncDisposable? storage, ManagementRole role, bool placeholder)
    {
        _settings = settings;
        _objects = objects;
        _clock = clock;
        _logger = logger;
        _accessor = accessor;
        _order = new ManagementOrder();
        _instance = objects?.Instance ?? new WriterInstance(string.IsNullOrWhiteSpace(settings.ObjectStorage.Instance) ? Environment.MachineName : settings.ObjectStorage.Instance);
        IsFleetOfOne = objects is null;
        _mayClaim = IsFleetOfOne || !string.Equals(settings.Fleet.Writer, "false", StringComparison.OrdinalIgnoreCase);
        _bootstrap = [.. settings.Manage.BootstrapOperators];
        _system = system;
        _systemStorage = storage;
        _role = role;
        _placeholder = placeholder;
        _snapshot = new Snapshot(GrantTable.Empty(_bootstrap), BootstrapState.Absent);
        Datasets = new OpenDatasets(settings, objects, _order, clock, logger, Moved);
        Instance = new WriterName(_instance.Value);
        Projections = new ValidationProjections(() => Volatile.Read(ref _snapshot).Table.Declarations, Datasets, ReportWait, logger);
        Classifications = new RowClassifications(() => Volatile.Read(ref _snapshot).Table.Declarations, Datasets, ReportWait, logger);
    }

    /// <summary>How long a read of a report graph waits for its projection before <c>validation-behind</c> (ADR 0174).</summary>
    internal static readonly TimeSpan ReportWait = TimeSpan.FromSeconds(2);

    /// <summary>The validation projections of the datasets held here (ADR 0174).</summary>
    internal ValidationProjections Projections { get; }

    /// <summary>The classification projections of the datasets held here (ADR 0233).</summary>
    internal RowClassifications Classifications { get; }

    // Whether a dataset's event declarations are moving or moved at the applied position (ADR 0167).
    private bool Moved(string dataset) =>
        Volatile.Read(ref _snapshot).Table.Declarations.Datasets.TryGetValue(dataset, out DatasetDeclaration? declared)
        && declared.EventDeclarations != EventDeclarationState.InDataset;

    /// <summary>Whether the fleet is this instance alone: no object store, so no follower and no forwarding.</summary>
    internal bool IsFleetOfOne { get; }

    /// <summary>The declared datasets, as this instance holds them.</summary>
    internal OpenDatasets Datasets { get; }

    // ---- start ----

    /// <summary>
    /// Opens the system dataset in this instance's role, records the bootstrap
    /// operators when it is the first writer or <paramref name="recordBootstrap"/>
    /// asks, applies the declarations, and opens the datasets they place here.
    /// </summary>
    internal static async Task<Fleet> StartAsync(ServerSettings settings, bool recordBootstrap, TimeProvider clock, ILogger logger, IHttpContextAccessor accessor, CancellationToken cancellationToken)
    {
        ObjectStores? objects = ObjectStores.From(settings.ObjectStorage, clock);
        Fleet fleet;

        try
        {
            fleet = await OpenSystemAsync(settings, objects, clock, logger, accessor, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            objects?.Dispose();
            throw;
        }

        try
        {
            if (fleet._role == ManagementRole.Writer)
            {
                await fleet.RecordBootstrapAsync(recordBootstrap, cancellationToken).ConfigureAwait(false);
            }
            else if (recordBootstrap)
            {
                throw new InvalidOperationException("--record-bootstrap is the system writer's: this instance follows the system dataset (ADR 0163 point 7).");
            }

            await fleet.AdvanceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await fleet.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return fleet;
    }

    private static async Task<Fleet> OpenSystemAsync(ServerSettings settings, ObjectStores? objects, TimeProvider clock, ILogger logger, IHttpContextAccessor accessor, CancellationToken cancellationToken)
    {
        DatasetOptions options = new() { Clock = clock, Name = SystemName };

        if (objects is null && settings.DatasetsRoot is { Length: > 0 } root)
        {
            // A fleet of one on the file backend: the system dataset under the root.
            FileStorage files = await FileStorage.OpenAsync(new DatasetDirectory(Path.Combine(root, SystemName)), new FileStorageOptions { Clock = clock }, cancellationToken).ConfigureAwait(false);

            try
            {
                bool exists = !(await files.Log.ReadManifestAsync(cancellationToken).ConfigureAwait(false)).IsEmpty;
                Dataset system = exists
                    ? await Dataset.OpenAsync(files, options, cancellationToken).ConfigureAwait(false)
                    : await Dataset.CreateAsync(files, new DatasetId(Guid.NewGuid()), options, cancellationToken).ConfigureAwait(false);
                return new Fleet(settings, null, clock, logger, accessor, system, files, ManagementRole.Writer, placeholder: false);
            }
            catch
            {
                await files.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        if (objects is null)
        {
            // No root and no store: management lives as long as the process.
            FleetLog.MemoryOnly(logger);
            Dataset memory = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), options, cancellationToken).ConfigureAwait(false);
            return new Fleet(settings, null, clock, logger, accessor, memory, null, ManagementRole.Writer, placeholder: false);
        }

        if (IsSystemWriter(settings, objects.Instance))
        {
            (Dataset system, Backend storage) = await ClaimSystemAsync(settings, objects, options, cancellationToken).ConfigureAwait(false);
            return new Fleet(settings, objects, clock, logger, accessor, system, storage, ManagementRole.Writer, placeholder: false);
        }

        (Dataset? followed, Backend? tailing) = await FollowSystemAsync(objects, options, cancellationToken).ConfigureAwait(false);

        if (followed is null)
        {
            // The writer has not created it yet: an empty stand-in until it has.
            Dataset empty = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), options, cancellationToken).ConfigureAwait(false);
            return new Fleet(settings, objects, clock, logger, accessor, empty, null, ManagementRole.Follower, placeholder: true);
        }

        return new Fleet(settings, objects, clock, logger, accessor, followed, tailing, ManagementRole.Follower, placeholder: false);
    }

    private static bool IsSystemWriter(ServerSettings settings, WriterInstance instance) => settings.Fleet.Writer switch
    {
        null => true,
        { } writer when string.Equals(writer, "true", StringComparison.OrdinalIgnoreCase) => true,
        { } writer when string.Equals(writer, "false", StringComparison.OrdinalIgnoreCase) => false,
        { } writer => string.Equals(writer, instance.Value, StringComparison.Ordinal),
    };

    private static async Task<(Dataset, Backend)> ClaimSystemAsync(ServerSettings settings, ObjectStores objects, DatasetOptions options, CancellationToken cancellationToken)
    {
        WriterAddress? address = settings.Fleet.InternalUrl is { } url ? new WriterAddress(url) : null;
        Backend storage = await objects.OpenAsync(SystemName, ObjectStorageAccess.Write, address, cancellationToken).ConfigureAwait(false);

        try
        {
            bool held = !(await storage.Log.ReadManifestAsync(cancellationToken).ConfigureAwait(false)).IsEmpty;
            Dataset system = held
                ? await Dataset.OpenAsync(storage, options, cancellationToken).ConfigureAwait(false)
                : await Dataset.CreateAsync(storage, new DatasetId(Guid.NewGuid()), options, cancellationToken).ConfigureAwait(false);
            return (system, storage);
        }
        catch
        {
            await storage.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<(Dataset?, Backend?)> FollowSystemAsync(ObjectStores objects, DatasetOptions options, CancellationToken cancellationToken)
    {
        if (!await objects.ExistsAsync(SystemName, cancellationToken).ConfigureAwait(false))
        {
            return (null, null);
        }

        Backend storage = await objects.OpenAsync(SystemName, ObjectStorageAccess.ReadOnly, null, cancellationToken).ConfigureAwait(false);

        try
        {
            return (await Dataset.OpenFollowerAsync(storage, options, cancellationToken).ConfigureAwait(false), storage);
        }
        catch
        {
            await storage.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // The first writer of an empty system dataset records the configured
    // bootstrap operators; --record-bootstrap records them again, replacing
    // what is recorded, as an explicit system commit (ADR 0163 point 7).
    private async Task RecordBootstrapAsync(bool replace, CancellationToken cancellationToken)
    {
        List<string> recorded;

        using (DatasetView view = _system.Pin())
        {
            recorded = [.. DeclarationReader.Read(view).Bootstrap];
        }

        bool empty = _system.Head.Value == 0;

        if ((!empty && !replace) || (empty && _bootstrap.Length == 0))
        {
            return;
        }

        RdfTerm graph = ManagementVocabulary.BootstrapGraph;
        RdfTerm predicate = RdfTerm.Iri(Encoding.UTF8.GetBytes(ManagementVocabulary.Namespace + "bootstrapOperator"));
        CommitRequestBuilder request = new()
        {
            ExpectedPosition = _system.Head,
            Metadata = new CommitMetadata { Cause = RdfTerm.Literal(Encoding.UTF8.GetBytes(replace ? "varve serve --record-bootstrap" : "varve serve: the first system writer")) },
        };

        foreach (string principal in recorded)
        {
            request = request.Retract(graph, predicate, RdfTerm.Literal(Encoding.UTF8.GetBytes(principal)), graph);
        }

        foreach (string principal in _bootstrap)
        {
            request = request.Assert(graph, predicate, RdfTerm.Literal(Encoding.UTF8.GetBytes(principal)), graph);
        }

        CommitResult result = await _system.CommitAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.Outcome is not (CommitOutcome.Committed or CommitOutcome.NoChange))
        {
            throw new InvalidOperationException("The bootstrap operators were not recorded: " + result.Outcome + ".");
        }

        FleetLog.BootstrapRecorded(_logger, _bootstrap.Length, result.Position.Value);
    }

    // ---- the management plane ----

    /// <summary>The system dataset, as this instance holds it.</summary>
    public Dataset System => Volatile.Read(ref _system);

    /// <summary>The grants pinned for this request, or the applied ones outside a request.</summary>
    public GrantTable Table => Pinned().Table;

    /// <summary>The validator of commits to the system dataset (ADR 0166).</summary>
    public ICommitValidator Validator { get; } = new ManagementValidator();

    public ManagementStatus Status
    {
        get
        {
            Snapshot current = Volatile.Read(ref _snapshot);
            Dataset system = System;
            long epoch = _systemStorage is Backend { Writer: { } record } ? record.Epoch.Value : 0;
            string? address = _role == ManagementRole.Writer
                ? _settings.Fleet.InternalUrl
                : _routes.TryGetValue(SystemName, out WriterRoute route) ? route.Address : null;
            return new ManagementStatus(_role, epoch, current.Table.Position, new ManagementPosition(Math.Max(system.Head.Value, current.Table.Position.Value)), current.Bootstrap, address);
        }
    }

    // ---- the authority ----

    /// <summary>The position the grants of this request are at.</summary>
    public ManagementPosition Applied => Pinned().Table.Position;

    /// <summary>The grants in force now, which a request pins at its start (ADR 0163 point 3).</summary>
    internal Snapshot Current => Volatile.Read(ref _snapshot);

    /// <summary>Pins <paramref name="snapshot"/> for the request: every check it makes uses those grants.</summary>
    internal static void Pin(HttpContext context, Snapshot snapshot) => context.Items[SnapshotKey] = snapshot;

    private Snapshot Pinned() => _accessor.HttpContext?.Items[SnapshotKey] as Snapshot ?? Volatile.Read(ref _snapshot);

    /// <summary>
    /// The validators a data write carries, authorised at the request's pinned
    /// management position: the order of positions (ADR 0165); the freeze of
    /// the catalogue graphs once the dataset's event declarations are moving or
    /// moved there (ADR 0167); and the dataset's shapes bindings at that
    /// position (ADR 0169).
    /// </summary>
    public ICommitValidator OrderOf(DatasetName dataset, ManagementPosition authorised)
    {
        ICommitValidator validator = _order.Validator(dataset, authorised);
        Declarations declarations = Pinned().Table.Declarations;

        if (declarations.Datasets.TryGetValue(dataset.Value, out DatasetDeclaration? declared) && declared.EventDeclarations != EventDeclarationState.InDataset)
        {
            validator = new OrderAndShapes(validator, new EventCatalogueFreeze(dataset));
        }

        return _shapes.GateOf(dataset, declarations) is { } gate ? new OrderAndShapes(validator, gate) : validator;
    }

    /// <inheritdoc />
    public IReadOnlyList<RdfTerm>? CheckBindings(Declarations proposed) =>
        ShapesBindings.Check(proposed, name => Datasets.TryResolve(name, out Dataset? dataset) ? dataset : null);

    /// <inheritdoc />
    public BindingDescription? DescribeBinding(DatasetName dataset, RdfTerm binding)
    {
        GrantTable table = Pinned().Table;
        return ShapesBindings.Describe(dataset, binding, table.Declarations, table.Position, Datasets.TryResolve(dataset, out Dataset? held) ? held : null);
    }

    /// <summary>The highest management position <paramref name="dataset"/>'s log records, as this writer knows (ADR 0165).</summary>
    internal ManagementPosition Recorded(DatasetName dataset) => _order.Highest(dataset);

    /// <summary>
    /// Advances the system dataset once, when this instance follows it,
    /// compiles the grants at the position reached when it moved, and converges
    /// the datasets to the declarations there.
    /// </summary>
    public async ValueTask<ManagementPosition> AdvanceAsync(CancellationToken cancellationToken)
    {
        await _advance.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await StepAsync(cancellationToken).ConfigureAwait(false);
            return Volatile.Read(ref _snapshot).Table.Position;
        }
        finally
        {
            _advance.Release();
        }
    }

    private async Task StepAsync(CancellationToken cancellationToken)
    {
        await FollowSystemOnceAsync(cancellationToken).ConfigureAwait(false);
        Dataset system = System;
        Snapshot current = Volatile.Read(ref _snapshot);
        bool changed = system.Head.Value != current.Table.Position.Value || current.Bootstrap == BootstrapState.Absent;

        if (changed)
        {
            Declarations declarations;

            using (DatasetView view = system.Pin())
            {
                declarations = DeclarationReader.Read(view);
            }

            current = new Snapshot(GrantTable.Compile(declarations, new ManagementPosition(system.Head.Value), _bootstrap), BootstrapOf(declarations.Bootstrap));
            Volatile.Write(ref _snapshot, current);
        }

        Placement placement = new(_instance, _mayClaim, _role == ManagementRole.Writer, IsFleetOfOne);
        await Datasets.ReconcileAsync(current.Table.Declarations, current.Table.Position, placement, cancellationToken).ConfigureAwait(false);

        // The validation projections follow the bindings in force and the datasets held (ADR 0174).
        foreach (DatasetEntry entry in Datasets.List())
        {
            Projections.Reconcile(entry.Name);
            Classifications.Reconcile(entry.Name);
        }
    }

    private BootstrapState BootstrapOf(IReadOnlyList<string> recorded)
    {
        if (recorded.Count == 0)
        {
            return BootstrapState.Absent;
        }

        HashSet<string> configured = new(_bootstrap, StringComparer.Ordinal);
        return configured.SetEquals(recorded) ? BootstrapState.Recorded : BootstrapState.Mismatch;
    }

    // The follower's one advance: the stand-in is replaced once the writer
    // has created the system dataset, a superseded writer follows from then
    // on, and a follower refreshes.
    private async Task FollowSystemOnceAsync(CancellationToken cancellationToken)
    {
        if (_objects is null)
        {
            return;
        }

        DatasetOptions options = new() { Clock = _clock, Name = SystemName };

        if (_role == ManagementRole.Writer && _systemStorage is Backend { Writer: { } held } writing && !writing.IsSuperseded)
        {
            // An idle writer makes no write to learn it was superseded: it
            // reads its record on the interval (ADR 0136; the handover ADR's
            // point 9, a mistaken second writer).
            if (await _objects.ReadWriterAsync(SystemName, cancellationToken).ConfigureAwait(false) is { } record && record.Epoch > held.Epoch)
            {
                _superseded = true;
            }
        }

        if (_role == ManagementRole.Writer && (_superseded || _systemStorage is Backend { IsSuperseded: true }))
        {
            // A mistaken second writer, or a successor: this instance is
            // superseded, reports it, and follows (ADR 0136).
            FleetLog.Superseded(_logger);
            _role = ManagementRole.Superseded;
            await SwapSystemAsync(null, null).ConfigureAwait(false);
            _placeholder = true;
        }

        if (_placeholder)
        {
            (Dataset? followed, Backend? tailing) = await FollowSystemAsync(_objects, options, cancellationToken).ConfigureAwait(false);

            if (followed is null)
            {
                return;
            }

            await SwapSystemAsync(followed, tailing).ConfigureAwait(false);
            _placeholder = false;
            return;
        }

        if (System.IsFollower)
        {
            try
            {
                await System.AdvanceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // The grants stay at the last applied position (ADR 0163 point 8).
                FleetLog.AdvanceFailed(_logger, SystemName, error.Message);
            }
        }
    }

    private async Task SwapSystemAsync(Dataset? next, IAsyncDisposable? storage)
    {
        next ??= await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = _clock, Name = SystemName }).ConfigureAwait(false);
        Dataset previous = Interlocked.Exchange(ref _system, next);
        IAsyncDisposable? previousStorage = Interlocked.Exchange(ref _systemStorage, storage);
        await previous.DisposeAsync().ConfigureAwait(false);

        if (previousStorage is not null)
        {
            await previousStorage.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The interval loop (ADR 0164): the system dataset is advanced, and the
    /// datasets converged, every <c>Varve:Fleet:FollowInterval</c>.
    /// </summary>
    internal async Task RunAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_settings.Fleet.FollowInterval, _clock, stopping).ConfigureAwait(false);
                await AdvanceAsync(stopping).ConfigureAwait(false);
                await ResumeMovesAsync(stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                FleetLog.AdvanceFailed(_logger, SystemName, error.Message);
            }
        }
    }

    /// <summary>
    /// Finishes a move of a dataset's event declarations (ADR 0167): set by
    /// the host once its protocol is composed. Null leaves every move to the
    /// request that started it.
    /// </summary>
    internal Func<DatasetName, CancellationToken, Task<bool>>? Resume { get; set; }

    // A move this instance can finish alone: it writes the system dataset and
    // the dataset both (ADR 0167 point 3). Run on the interval, outside the
    // advance, which the move itself takes.
    private async Task ResumeMovesAsync(CancellationToken cancellationToken)
    {
        if (Resume is not { } resume || _role != ManagementRole.Writer)
        {
            return;
        }

        foreach ((string name, DatasetDeclaration declared) in Volatile.Read(ref _snapshot).Table.Declarations.Datasets)
        {
            DatasetName dataset = new(name);

            if (declared.EventDeclarations == EventDeclarationState.Moving && Datasets.RoleOf(dataset) == DatasetRole.Writer)
            {
                bool moved = await resume(dataset, cancellationToken).ConfigureAwait(false);
                FleetLog.MoveResumed(_logger, name, moved);
            }
        }
    }

    // ---- following ----

    public async ValueTask FollowAsync(DatasetName name, Dataset dataset, Position? atLeast, CancellationToken cancellationToken)
    {
        if (!dataset.IsFollower)
        {
            return;
        }

        Follow follow = _follows.GetOrAdd(name.Value, static _ => new Follow());
        long now = _clock.GetTimestamp();

        if (atLeast is not { } wanted || wanted <= dataset.Head)
        {
            if (_clock.GetElapsedTime(Volatile.Read(ref follow.Last), now) < _settings.Fleet.FollowInterval)
            {
                return;
            }
        }

        await follow.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Another read advanced it while this one waited.
            if ((atLeast is not { } again || again <= dataset.Head) && _clock.GetElapsedTime(Volatile.Read(ref follow.Last), now) < _settings.Fleet.FollowInterval)
            {
                return;
            }

            await dataset.AdvanceAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref follow.Last, _clock.GetTimestamp());
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // The read is served at what is applied; Varve-Min-Position still
            // refuses it when that is too little.
            FleetLog.AdvanceFailed(_logger, name.Value, error.Message);
        }
        finally
        {
            follow.Gate.Release();
        }
    }

    // ---- the writer directory ----

    public WriterName Instance { get; }

    public async ValueTask<WriterRoute> RouteAsync(WriterTarget target, CancellationToken cancellationToken)
    {
        string name;

        if (target.Dataset is { } dataset)
        {
            if (Datasets.RoleOf(dataset) != DatasetRole.Follower)
            {
                // Written here, or not held here at all: served, or not found, here.
                return WriterRoute.Local;
            }

            name = dataset.Value;
        }
        else
        {
            if (_role == ManagementRole.Writer)
            {
                return WriterRoute.Local;
            }

            name = SystemName;
        }

        if (_routes.TryGetValue(name, out WriterRoute known))
        {
            return known;
        }

        WriterRecord? record = await _objects!.ReadWriterAsync(name, cancellationToken).ConfigureAwait(false);
        WriterRoute route = RouteOf(record);
        _routes[name] = route;
        return route;
    }

    private WriterRoute RouteOf(WriterRecord? record)
    {
        if (record is null)
        {
            return new WriterRoute(WriterRouteKind.NoWriter, null, 0);
        }

        long epoch = record.Epoch.Value;
        EpochClaim? claim = record.History.Count > 0 ? record.History[^1] : null;

        if (record.Released is not null || claim?.Address is not { } address || record.Holder == _instance)
        {
            return new WriterRoute(WriterRouteKind.NoWriter, null, epoch);
        }

        return new WriterRoute(WriterRouteKind.Remote, address.Value, epoch);
    }

    public void Stale(WriterTarget target) => _routes.TryRemove(target.Dataset?.Value ?? SystemName, out _);

    public async ValueTask DisposeAsync()
    {
        await Projections.DisposeAsync().ConfigureAwait(false);
        await Classifications.DisposeAsync().ConfigureAwait(false);
        await Datasets.DisposeAsync().ConfigureAwait(false);
        await _system.DisposeAsync().ConfigureAwait(false);

        if (_systemStorage is not null)
        {
            await _systemStorage.DisposeAsync().ConfigureAwait(false);
        }

        _objects?.Dispose();
        _advance.Dispose();

        foreach (Follow follow in _follows.Values)
        {
            follow.Dispose();
        }
    }

    /// <summary>The grants in force at one position, and the bootstrap state there.</summary>
    internal sealed record Snapshot(GrantTable Table, BootstrapState Bootstrap);

    private sealed class Follow : IDisposable
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal long Last;

        public void Dispose() => Gate.Dispose();
    }

    private static partial class FleetLog
    {
        [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "Neither Varve:DatasetsRoot nor Varve:ObjectStorage is set: the management plane's system dataset is in memory, and every declaration is lost when the process stops (ADR 0163).")]
        internal static partial void MemoryOnly(ILogger logger);

        [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Recorded {Count} bootstrap operators at system position {Position} (ADR 0163 point 7).")]
        internal static partial void BootstrapRecorded(ILogger logger, int count, long position);

        [LoggerMessage(EventId = 12, Level = LogLevel.Error, Message = "A newer writer holds the system dataset: this instance is superseded, follows it, and is not ready (ADR 0136).")]
        internal static partial void Superseded(ILogger logger);

        [LoggerMessage(EventId = 13, Level = LogLevel.Warning, Message = "Advancing '{Name}' failed; it serves what it applied: {Reason}")]
        internal static partial void AdvanceFailed(ILogger logger, string name, string reason);

        [LoggerMessage(EventId = 14, Level = LogLevel.Information, Message = "Resumed the move of '{Name}''s event declarations to the management plane; moved: {Moved} (ADR 0167).")]
        internal static partial void MoveResumed(ILogger logger, string name, bool moved);
    }
}
