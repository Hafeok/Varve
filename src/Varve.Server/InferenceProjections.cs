// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Microsoft.Extensions.Logging;
using Varve.Inference;
using Varve.Inference.Model;
using Varve.Inference.Store;
using Varve.Inference.Store.Model;
using Varve.Management.Model;
using Varve.Protocol;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server;

/// <summary>
/// The server's inference projections (ADR 0195): one per materialising TBox
/// binding of each dataset this instance holds open, under the declarations
/// in force, each fed the dataset's commits by a subscription of its own and
/// read through <see cref="IInferenceClosures"/>, the one seam of tier 2. A
/// file dataset's projection keeps its state in the dataset's own
/// <c>derived/</c>. One with no saved state starts at the head, a generation
/// there, its floor. A projection's lag, held history, clashes and state are
/// reported on readiness and never make the dataset unready.
/// </summary>
/// <remarks>
/// A cross-graph binding's rows are placed by premise set (ADR 0190 point
/// 6); <see cref="TryGetPremiseSet"/> is the rule a caller's scope governs
/// its premise-set graphs by.
/// </remarks>
[Contract(typeof(TheInferenceProjection.OneSeamForTierTwo), Role = "the server's inference projections, fed and served")]
internal sealed partial class InferenceProjections : IInferenceClosures, IAsyncDisposable
{
    /// <summary>How many commits a projection applies between two saves of its state, at most.</summary>
    private const int SaveEvery = 1_000;

    /// <summary>How long a projection that applies commits goes between two saves of its state, at most.</summary>
    private static readonly TimeSpan SaveAfter = TimeSpan.FromMinutes(5);

    private readonly Func<Declarations> _declarations;
    private readonly OpenDatasets _datasets;
    private readonly TimeSpan _wait;
    private readonly InferenceOptions _options;

    /// <summary>Why the settings leave the bindings' pool nothing, or null: see <see cref="NoMemory(ServerSettings)"/>.</summary>
    internal NoClosureMemory? WithoutMemory { get; init; }
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Dataset, string Binding), Entry> _entries = [];
    private bool _disposed;

    /// <summary>Projections of <paramref name="datasets"/> under the declarations <paramref name="declarations"/> answers; a read waits up to <paramref name="wait"/> for one to reach its position.</summary>
    [DesignDecision(typeof(TheInferenceProjection.InferenceBehind), Scope = ExceptionScope.Boundary)]
    internal InferenceProjections(Func<Declarations> declarations, OpenDatasets datasets, TimeSpan wait, InferenceOptions options, ILogger logger, TimeSpan? stopWait = null)
    {
        _declarations = declarations;
        _datasets = datasets;
        _wait = wait;
        _options = options;
        _logger = logger;
        _stopWait = stopWait ?? TimeSpan.FromSeconds(10);
    }

    private readonly TimeSpan _stopWait;

    /// <summary>
    /// The pool every binding of the process takes from (ADR 0195 point 6): the collector's heap limit less the memtable's
    /// worst case and <c>Varve:Inference:ReserveBytes</c>, kept for queries,
    /// caches and requests; none when they leave nothing.
    /// </summary>
    internal static long PoolBytes(ServerSettings settings) =>
        Math.Max(0, MemtableDefault.HeapLimit() - SettingsCheck.MemtableWorstBytes(settings) - settings.Inference.ReserveBytes);

    /// <summary>
    /// Why the settings leave the bindings' pool nothing, or null when they
    /// leave it something (ADR 0195 point 6). With
    /// none, every materialising binding is stopped at its memory limit as it
    /// is declared, before it builds; the instance starts all the same, so a
    /// server that declares no such binding runs at any heap limit.
    /// </summary>
    internal static NoClosureMemory? NoMemory(ServerSettings settings) =>
        PoolBytes(settings) > 0
            ? null
            : new NoClosureMemory(new ByteCount(MemtableDefault.HeapLimit()), new ByteCount(SettingsCheck.MemtableWorstBytes(settings)), new ByteCount(settings.Inference.ReserveBytes));

    /// <summary>
    /// A binding's limit when <c>Varve:Inference:ClosureBytes</c> is unset: a
    /// quarter of the collector's heap limit, or the pool if that is less.
    /// </summary>
    internal static long DefaultClosureBytes(ServerSettings settings) =>
        Math.Min(Math.Max(1L << 20, MemtableDefault.HeapLimit() / 4), PoolBytes(settings));

    /// <summary>
    /// The projection options a server's settings give:
    /// <c>Varve:Inference:HistoryBytes</c>; <c>Varve:Inference:ClosureBytes</c>
    /// or, unset, <see cref="DefaultClosureBytes"/>; and the pool every binding
    /// also takes from, of <see cref="PoolBytes"/>.
    /// </summary>
    internal static InferenceOptions Options(ServerSettings settings) => new()
    {
        HistoryBytes = new ByteCount(settings.Inference.HistoryBytes),
        ClosureBytes = new ByteCount(settings.Inference.ClosureBytes ?? DefaultClosureBytes(settings)),
        Pool = new FactBudget(new MemoryAmount(PoolBytes(settings)), ClosureCosts.For(InferenceProfile.Rdfs)),
    };

    /// <inheritdoc />
    public IReadOnlyList<IInferenceClosure> Of(DatasetName dataset)
    {
        Reconcile(dataset);
        List<IInferenceClosure> closures = [];
        lock (_lock)
        {
            foreach (((string name, string _), Entry entry) in _entries)
            {
                if (name == dataset.Value)
                {
                    closures.Add(entry);
                }
            }
        }

        return closures;
    }

    /// <summary>Each binding's state for readiness, by binding IRI: its lag, its held history, its clashes, and whether it is stopped.</summary>
    internal SortedDictionary<string, InferenceStatus> StatusOf(DatasetName dataset)
    {
        Reconcile(dataset);
        SortedDictionary<string, InferenceStatus> statuses = new(StringComparer.Ordinal);
        lock (_lock)
        {
            foreach (((string name, string _), Entry entry) in _entries)
            {
                if (name == dataset.Value)
                {
                    statuses[entry.Name] = entry.Status();
                }
            }
        }

        return statuses;
    }

    /// <summary>
    /// The governing rule for a premise-set graph of one of
    /// <paramref name="dataset"/>'s cross-graph bindings (ADR 0190 point 6;
    /// ADR 0107, amended): its data graphs, null for the default graph.
    /// </summary>
    internal bool TryGetPremiseSet(DatasetName dataset, RdfTerm graph, out IReadOnlyList<RdfTerm?>? governors)
    {
        lock (_lock)
        {
            foreach (((string name, string _), Entry entry) in _entries)
            {
                if (name == dataset.Value && entry.Projection.PremiseSetOf(graph) is { } set)
                {
                    RdfTerm?[] members = new RdfTerm?[set.Count];
                    for (int i = 0; i < members.Length; i++)
                    {
                        members[i] = set[i].Equals(DefaultGraph) ? null : set[i];
                    }

                    governors = members;
                    return true;
                }
            }
        }

        governors = null;
        return false;
    }

    private static readonly RdfTerm DefaultGraph = RdfTerm.Iri("https://w3id.org/varve/ns#defaultGraph"u8);

    /// <summary>
    /// Brings <paramref name="dataset"/>'s projections in line with its
    /// materialising bindings: a new or changed binding gets a projection,
    /// fed from its saved state or from the head; a binding gone, a dataset
    /// closed or reopened, stops its own.
    /// </summary>
    internal void Reconcile(DatasetName dataset)
    {
        Dataset? held = _datasets.TryResolve(dataset, out Dataset? open) ? open : null;
        Dictionary<string, InferenceBinding> wanted = new(StringComparer.Ordinal);
        if (held is not null && _declarations().Datasets.TryGetValue(dataset.Value, out DatasetDeclaration? declared))
        {
            foreach (TBoxBindingDeclaration binding in declared.TBoxBindings)
            {
                if (Materialises(binding.Profile))
                {
                    InferenceBinding mapped = Map(binding);
                    wanted[Identity(mapped)] = mapped;
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
                if (name == dataset.Value && (!wanted.ContainsKey(key) || !ReferenceEquals(entry.Dataset, held)))
                {
                    stopped.Add(entry);
                }
            }

            foreach (Entry entry in stopped)
            {
                _entries.Remove((dataset.Value, entry.Key));
            }

            foreach ((string key, InferenceBinding binding) in wanted)
            {
                if (!_entries.ContainsKey((dataset.Value, key)))
                {
                    Entry entry = new(key, held!, new InferenceProjection(held!, binding, _options), _datasets.LocalDerivedOf(dataset), _wait, _stopWait, _options.Clock, _logger);
                    _entries[(dataset.Value, key)] = entry;
                    entry.Start(_logger, WithoutMemory);
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
    }

    private static bool Materialises(TBoxProfile profile) => profile is TBoxProfile.Rdfs or TBoxProfile.Owl2Rl;

    private static InferenceBinding Map(TBoxBindingDeclaration binding) =>
        new(binding.Node, binding.TBoxGraphs, ShapesBindings.Scope(binding.DataGraphs), binding.Profile == TBoxProfile.Owl2Rl ? InferenceProfile.Owl2Rl : InferenceProfile.Rdfs)
        {
            CrossGraph = binding.CrossGraph,
        };

    /// <summary>What makes two bindings the same projection: everything the closure depends on.</summary>
    private static string Identity(InferenceBinding binding)
    {
        StringBuilder key = new();
        key.Append(Encoding.UTF8.GetString(binding.Binding.Lexical)).Append('|').Append(binding.Profile).Append('|').Append(binding.CrossGraph).Append('|');
        foreach (RdfTerm graph in binding.TBoxGraphs)
        {
            key.Append('<').Append(Encoding.UTF8.GetString(graph.Lexical)).Append('>');
        }

        key.Append('|').Append(binding.DataGraphs.IsAll).Append(binding.DataGraphs.IncludesDefault);
        foreach (RdfTerm graph in binding.DataGraphs.Graphs)
        {
            key.Append('<').Append(Encoding.UTF8.GetString(graph.Lexical)).Append('>');
        }

        foreach (string prefix in binding.DataGraphs.Prefixes)
        {
            key.Append('"').Append(prefix).Append('"');
        }

        return key.ToString();
    }

    /// <summary>One binding's projection, its feed, and its closure as the seam reads it.</summary>
    [Contract(typeof(TheInferenceProjection.ShutdownCancelsAPass), Role = "a binding's feed, stopped by cancelling its pass")]
    private sealed class Entry : IInferenceClosure, IAsyncDisposable
    {
        private readonly IDerivedStore? _cache;
        private readonly TimeSpan _wait;
        private readonly CancellationTokenSource _stop = new();
        private Task _feed = Task.CompletedTask;
        private NoClosureMemory? _withoutMemory;
        private readonly TimeSpan _stopWait;
        private readonly TimeProvider _clock;
        private readonly ILogger _logger;

        internal Entry(string key, Dataset dataset, InferenceProjection projection, IDerivedStore? cache, TimeSpan wait, TimeSpan stopWait, TimeProvider clock, ILogger logger)
        {
            _clock = clock;
            Key = key;
            Dataset = dataset;
            Projection = projection;
            _cache = cache;
            _wait = wait;
            _stopWait = stopWait;
            _logger = logger;
            Name = Encoding.UTF8.GetString(projection.Binding.Binding.Lexical);
        }

        /// <summary>The binding's IRI, as text for the log and readiness.</summary>
        internal string Name { get; }

        internal string Key { get; }

        internal Dataset Dataset { get; }

        internal InferenceProjection Projection { get; }

        public RdfTerm Binding => Projection.Binding.Binding;

        public RdfTerm TBoxConclusions => Projection.TBoxGraph;

        public IReadOnlyList<RdfTerm> TBoxGraphs => Projection.Binding.TBoxGraphs;

        public GraphScope DataGraphs => Projection.Binding.DataGraphs;

        public InferenceProfile Profile => Projection.Binding.Profile;

        public IReadOnlyList<RdfTerm>? PremiseSetOf(RdfTerm graph) => Projection.PremiseSetOf(graph);

        public IReadOnlyList<RdfTerm>? PremiseSetGraphs() => Projection.Binding.CrossGraph ? Projection.PremiseSetGraphs() : null;

        public InferredRows RowsAt(Position position)
        {
            switch (Projection.TryRead(position, out InferredRows? rows))
            {
                case InferenceReadOutcome.Held:
                    return rows!;
                case InferenceReadOutcome.FromScratch:
                    return FromScratch(position);
                case InferenceReadOutcome.Unavailable:
                    throw Unavailable();
            }

            // The one bounded wait, on this thread: a read's match is synchronous.
            try
            {
                using CancellationTokenSource bound = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                bound.CancelAfter(_wait);
                Projection.WaitForAsync(position, bound.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ended) when (ended is OperationCanceledException or ObjectDisposedException)
            {
                // The wait ran out, or the projection stopped: either way it has not reached the position.
            }

            return Projection.TryRead(position, out rows) switch
            {
                InferenceReadOutcome.Held => rows!,
                InferenceReadOutcome.FromScratch => FromScratch(position),
                InferenceReadOutcome.Unavailable => throw Unavailable(),
                _ => throw new InferenceBehindException(Binding, position, Projection.Position),
            };
        }

        public IReadOnlyList<PlacedClash> Clashes() => Projection.Clashes();

        public Explanation Explain(Position position, RdfTerm? graph, RdfTerm subject, RdfTerm predicate, RdfTerm @object)
        {
            (InferenceReadOutcome outcome, Explanation? explanation) = Once();
            if (outcome == InferenceReadOutcome.Behind && Projection.Position < position)
            {
                // The one bounded wait, as a read makes.
                try
                {
                    using CancellationTokenSource bound = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    bound.CancelAfter(_wait);
                    Projection.WaitForAsync(position, bound.Token).AsTask().GetAwaiter().GetResult();
                }
                catch (Exception ended) when (ended is OperationCanceledException or ObjectDisposedException)
                {
                }

                (outcome, explanation) = Once();
            }

            return outcome switch
            {
                InferenceReadOutcome.Behind when Projection.Position >= position => throw new InferenceBehindException(Binding, position, PassNames.Busy(Projection.Pass, _clock)),
                InferenceReadOutcome.Behind => throw new InferenceBehindException(Binding, position, Projection.Position),
                InferenceReadOutcome.Unavailable => throw Unavailable(),
                _ => explanation!,
            };

            (InferenceReadOutcome, Explanation?) Once()
            {
                try
                {
                    return Projection.ExplainAsync(position, graph, subject, predicate, @object, _stop.Token).AsTask().GetAwaiter().GetResult();
                }
                catch (ClosureLimitExceededException)
                {
                    throw Unavailable();
                }
            }
        }

        /// <summary>
        /// The binding's state on readiness: read from what the projection
        /// published at the end of its last pass, and the pass running now,
        /// never under a pass's lock and never by matching a check (ADR 0195
        /// point 6, amended 2026-11-25).
        /// </summary>
        internal InferenceStatus Status()
        {
            (ByteCount limit, ByteCount reached) = Projection.MemoryLimit;
            // One snapshot for every figure, the lag included: a pass that has moved the position but not yet published
            // shows as the lag and the pass it is, never as the old figures at no lag.
            InferenceSnapshot snapshot = Projection.Snapshot;
            InferencePass? pass = Projection.Pass;
            return new InferenceStatus(
                Math.Max(0, Dataset.Head.Value - snapshot.Position.Value),
                Projection.HeldFrom.Value,
                !Projection.Stopped ? "following" : Projection.Failure is null ? "stopped (memory-limit)" : "stopped (failed)",
                snapshot.Clashes.Count,
                limit.Value,
                Projection.Stopped ? reached.Value : 0,
                Projection.Binding.CrossGraph ? snapshot.PremiseSetGraphs.Count : null)
            {
                Held = snapshot.Held.Value,
                Peak = snapshot.Peak.Value,
                Pass = pass,
                PassRunning = pass is null ? TimeSpan.Zero : PassNames.Running(pass, _clock),
                UnreadLists = snapshot.UnreadLists,
            };
        }

        internal void Start(ILogger logger, NoClosureMemory? withoutMemory)
        {
            if (withoutMemory is not null)
            {
                // No pool to build in: stopped at its memory limit before anything is built, and never tried again (ADR 0195 point 6).
                _withoutMemory = withoutMemory;
                Projection.Refuse();
                ProjectionLog.NoMemory(logger, Name, withoutMemory.HeapLimit.Value, withoutMemory.MemtableWorst.Value, withoutMemory.Reserve.Value);
                return;
            }

            _feed = Task.Run(() => FeedAsync(logger, _stop.Token));
        }

        public ValueTask DisposeAsync() => new(StopAsync());

        /// <summary>
        /// Stops the feed (ADR 0195 point 6, amended 2026-11-25): its pass is
        /// cancelled, and seen within a thousand rule firings; a pass that has
        /// not ended after <c>Varve:Inference:StopWait</c> is abandoned. The
        /// state is saved when no pass was cut short, so the next start loads
        /// it; otherwise the state saved last stays the newest.
        /// </summary>
        internal async Task StopAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            try
            {
                await _feed.WaitAsync(_stopWait).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
                ProjectionLog.Abandoned(_logger, Name, _stopWait);
                return;
            }

            if (_cache is not null && !Projection.Abandoned)
            {
                try
                {
                    await Projection.SaveAsync(_cache, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is System.IO.IOException or ObjectDisposedException or InvalidOperationException or ArgumentOutOfRangeException)
                {
                    // Derived and droppable: the next start rebuilds what was not saved.
                }
            }

            _stop.Dispose();
        }

        private InferredRows FromScratch(Position position)
        {
            try
            {
                return Projection.ReadFromScratchAsync(position, _stop.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (ClosureLimitExceededException)
            {
                throw Unavailable();
            }
        }

        private InferenceUnavailableException Unavailable()
        {
            if (_withoutMemory is { } memory)
            {
                return new InferenceUnavailableException(Binding, memory);
            }

            if (Projection.Failure is { } failure)
            {
                return new InferenceUnavailableException(Binding, failure);
            }

            (ByteCount limit, ByteCount reached) = Projection.MemoryLimit;
            return new InferenceUnavailableException(Binding, limit, reached);
        }

        /// <summary>
        /// Feeds the projection from its position (ADR 0195 point 6). A store read or write that fails holds the feed off:
        /// it waits, longer each time up to half a minute, and resumes from
        /// the projection's position, as a follower catches up, with no
        /// generation; a projection a failure has stopped stays stopped.
        /// </summary>
        private async Task FeedAsync(ILogger logger, CancellationToken stopping)
        {
            bool started = false;
            TimeSpan wait = TimeSpan.FromSeconds(1);
            while (true)
            {
                try
                {
                    if (!started)
                    {
                        if (_cache is not null && await Projection.LoadAsync(_cache, stopping).ConfigureAwait(false))
                        {
                            ProjectionLog.Loaded(logger, Name, Projection.Position.Value);
                        }
                        else if (Dataset.Head.Value > 0)
                        {
                            // A cold start: one generation at the head, which is the projection's floor (ADR 0195 point 5).
                            using DatasetView head = Dataset.Pin();
                            await Projection.ResetAsync(head, stopping).ConfigureAwait(false);

                            // Saved once built, so a stop from here on never leaves the next start cold.
                            if (_cache is not null)
                            {
                                await SaveAsync(logger, stopping).ConfigureAwait(false);
                            }
                        }

                        started = true;
                    }

                    int applied = 0;
                    long saved = Environment.TickCount64;
                    await foreach (Commit commit in Dataset.Subscribe(Projection.Position, SubscriptionFilter.All, stopping).ConfigureAwait(false))
                    {
                        await Projection.ApplyAsync(commit, stopping).ConfigureAwait(false);
                        wait = TimeSpan.FromSeconds(1);
                        if (_cache is not null && (++applied % SaveEvery == 0 || Environment.TickCount64 - saved >= (long)SaveAfter.TotalMilliseconds))
                        {
                            await SaveAsync(logger, stopping).ConfigureAwait(false);
                            applied = 0;
                            saved = Environment.TickCount64;
                        }
                    }

                    return;
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception failure) when (InferenceProjection.IsStorageFailure(failure))
                {
                    // The store did not answer: hold off, and resume from where the projection is.
                    ProjectionLog.HeldOff(logger, Name, Projection.Position.Value, failure.Message, wait);
                    try
                    {
                        await Task.Delay(wait, stopping).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    wait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, TimeSpan.FromSeconds(30).Ticks));
                }
                catch (Exception failure) when (failure is DatasetUnavailableException or ObjectDisposedException or InvalidOperationException)
                {
                    // The dataset closed or failed under the feed: its next reconcile starts another.
                    ProjectionLog.Stopped(logger, Name, failure.Message);
                    return;
                }
            }
        }

        /// <summary>Saves the projection's state; a store that does not take it is told in the log, and the next save tries again.</summary>
        private async Task SaveAsync(ILogger logger, CancellationToken stopping)
        {
            try
            {
                await Projection.SaveAsync(_cache!, stopping).ConfigureAwait(false);
            }
            catch (Exception failure) when (InferenceProjection.IsStorageFailure(failure))
            {
                ProjectionLog.NotSaved(logger, Name, failure.Message);
            }
        }
    }

    private static partial class ProjectionLog
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "The inference projection of {Binding} read its saved state at position {Position}.")]
        internal static partial void Loaded(ILogger logger, string binding, long position);

        [LoggerMessage(Level = LogLevel.Warning, Message = "The inference projection of {Binding} stopped: {Reason}. It starts again when its dataset is next reconciled.")]
        internal static partial void Stopped(ILogger logger, string binding, string reason);

        [LoggerMessage(Level = LogLevel.Warning, Message = "The inference projection of {Binding} held off at position {Position}: the store failed ({Reason}). It resumes from there in {Wait}.")]
        internal static partial void HeldOff(ILogger logger, string binding, long position, string reason, TimeSpan wait);

        [LoggerMessage(Level = LogLevel.Warning, Message = "The inference projection of {Binding} did not end its pass within {Wait} of the stop: the pass is abandoned and nothing more is saved; the next start loads the state saved last (ADR 0195).")]
        internal static partial void Abandoned(ILogger logger, string binding, TimeSpan wait);

        [LoggerMessage(Level = LogLevel.Warning, Message = "The inference projection of {Binding} could not save its state: {Reason}. The next save tries again.")]
        internal static partial void NotSaved(ILogger logger, string binding, string reason);

        [LoggerMessage(Level = LogLevel.Error, Message = "The inference projection of {Binding} is stopped at its memory limit and not built: the heap limit, {HeapLimit} bytes, less the memtable's worst case, {MemtableWorst} bytes, and Varve:Inference:ReserveBytes, {Reserve} bytes, leaves the bindings' pool nothing (ADR 0195). Lower Varve:Inference:ReserveBytes or Varve:Store:MemtableLimit, or raise the heap limit, then restart.")]
        internal static partial void NoMemory(ILogger logger, string binding, long heapLimit, long memtableWorst, long reserve);
    }
}

/// <summary>One binding's state on readiness (ADR 0195 point 6): reported, and never a reason to be unready.</summary>
/// <param name="Lag">Head minus the projection's position.</param>
/// <param name="HeldFrom">The lowest position its held history serves.</param>
/// <param name="State"><c>following</c>, <c>stopped (memory-limit)</c>, or <c>stopped (failed)</c>: a stopped binding is not tried again.</param>
/// <param name="Clashes">The clashes at its position (ADR 0197 point 5).</param>
/// <param name="Limit">Its memory limit, in bytes.</param>
/// <param name="Reached">What a stopped binding's closure held when it stopped, in bytes.</param>
/// <param name="PremiseSets">A cross-graph binding's premise-set graphs holding rows (ADR 0190 point 6); null for an intra-graph one.</param>
[DesignDecision(typeof(TheInferenceProjection.InferenceBehind), Scope = ExceptionScope.Boundary)]
internal sealed record InferenceStatus(long Lag, long HeldFrom, string State, int Clashes, long Limit, long Reached, int? PremiseSets = null)
{
    /// <summary>What the binding's closures hold now, in bytes by their budget's count.</summary>
    internal long Held { get; init; }

    /// <summary>The most they have held at once since they were built, the build included: what the binding needs of its limit.</summary>
    internal long Peak { get; init; }

    /// <summary>The pass running now, if any: readiness names its kind, the position it brings the binding to, and how long it has run.</summary>
    internal InferencePass? Pass { get; init; }

    /// <summary>How long <see cref="Pass"/> had run when the status was read.</summary>
    internal TimeSpan PassRunning { get; init; }

    /// <summary>The cells of asserted lists its rules do not read, each with its graph and why (ADR 0197, amended 2026-11-24).</summary>
    internal IReadOnlyList<NamedUnreadList> UnreadLists { get; init; } = [];

    /// <summary>A graph or cell as the status names it: an IRI as itself, a blank node as <c>_:label</c>.</summary>
    internal static string Text(RdfTerm term) =>
        term.Kind == RdfTermKind.BlankNode ? "_:" + Encoding.UTF8.GetString(term.Lexical) : Encoding.UTF8.GetString(term.Lexical);

    /// <summary>Why a cell is unread, as the status names it.</summary>
    internal static string ReasonName(UnreadListReason reason) => reason switch
    {
        UnreadListReason.SeveralFirsts => "several-first",
        UnreadListReason.SeveralRests => "several-rest",
        UnreadListReason.NoFirst => "no-first",
        UnreadListReason.NoRest => "no-rest",
        UnreadListReason.Cycle => "cycle",
        UnreadListReason.Broken => "broken",
        UnreadListReason.SpansGraphs => "spans-graphs",
        _ => "differs-from-tbox",
    };
}

/// <summary>
/// A binding's pass as readiness and explain name it (ADR 0195 point 6,
/// amended 2026-11-25): explain at the projection's position waits a read's
/// bound for the pass, then answers <c>inference-behind</c> naming it.
/// </summary>
[Contract(typeof(TheInferenceProjection.ExplainWaitsAtMostTheReadBound), Role = "a pass, named to a reader who would wait for it")]
internal static class PassNames
{
    /// <summary>A pass's kind as readiness and explain name it.</summary>
    internal static string Kind(InferencePassKind kind) => kind switch
    {
        InferencePassKind.ColdStart => "cold-start",
        InferencePassKind.Generation => "generation",
        InferencePassKind.Commit => "commit",
        _ => "load",
    };

    /// <summary>How long <paramref name="pass"/> has run by <paramref name="clock"/>; never less than zero.</summary>
    internal static TimeSpan Running(InferencePass pass, TimeProvider clock)
    {
        TimeSpan running = clock.GetUtcNow() - pass.Since.Value;
        return running < TimeSpan.Zero ? TimeSpan.Zero : running;
    }

    /// <summary>What explain says when a pass holds the live closures past its wait: the pass, and that explain answers when it ends.</summary>
    internal static string Busy(InferencePass? pass, TimeProvider clock)
    {
        string what = pass is null ? "A pass" : "A " + Kind(pass.Kind) + " pass to position " + pass.Position.Value.ToString(CultureInfo.InvariantCulture)
            + ", running " + Running(pass, clock).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s,";
        return what + " holds the closures of this binding. Explain answers at this position when it ends: retry.";
    }
}
