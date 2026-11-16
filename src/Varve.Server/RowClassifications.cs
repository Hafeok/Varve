// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Microsoft.Extensions.Logging;
using Varve.Classification;
using Varve.Classification.Model;
using Varve.Management.Model;
using Varve.Protocol;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server;

/// <summary>
/// The server's classification projections (ADR 0233): one per classifier
/// binding a dataset declares, fed from the dataset's commits, saved to the
/// local cache of a file dataset, and started at the head on a cold start.
/// It answers the protocol's row-restricted reads and writes, and its lag is
/// on readiness, never making the dataset unready.
/// </summary>
[Contract(typeof(TheClassificationProjection.OneClassificationProjectionPerBinding), Role = "the server's classification projections, fed and served")]
internal sealed partial class RowClassifications : IRowClassifications, IAsyncDisposable
{
    private const int SaveEvery = 1_000;

    private readonly Func<Declarations> _declarations;
    private readonly OpenDatasets _datasets;
    private readonly TimeSpan _wait;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Dataset, string Binding), Entry> _entries = [];
    private bool _disposed;

    [DesignDecision(typeof(TheClassificationProjection.ClassificationLagNeverUnready), Scope = ExceptionScope.Boundary)]
    internal RowClassifications(Func<Declarations> declarations, OpenDatasets datasets, TimeSpan wait, ILogger logger)
    {
        _declarations = declarations;
        _datasets = datasets;
        _wait = wait;
        _logger = logger;
    }

    /// <inheritdoc />
    public IRowClassification? At(DatasetName dataset, DatasetView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        List<Entry> entries = EntriesOf(dataset);

        if (entries.Count == 0)
        {
            return null;
        }

        IRowClassification[] parts = new IRowClassification[entries.Count];

        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = entries[i].At(view);
        }

        return ShapeClassifier.Compose(parts);
    }

    /// <inheritdoc />
    public IClassifier? Classifier(DatasetName dataset)
    {
        List<Entry> entries = EntriesOf(dataset);
        return entries.Count == 0 ? null : new ShapeClassifier(entries.ConvertAll(static e => e.Projection.Binding));
    }

    /// <inheritdoc />
    public IReadOnlyList<TermHandle> ChangedAt(DatasetName dataset, Position position)
    {
        List<TermHandle> changed = [];

        foreach (Entry entry in EntriesOf(dataset))
        {
            changed.AddRange(entry.Projection.ChangedAt(position));
        }

        return changed;
    }

    /// <summary>Each binding's lag behind its dataset's head, by binding IRI, for readiness.</summary>
    internal SortedDictionary<string, long> LagOf(DatasetName dataset)
    {
        SortedDictionary<string, long> lags = new(StringComparer.Ordinal);

        foreach (Entry entry in EntriesOf(dataset))
        {
            lags[entry.Name] = Math.Max(0, entry.Dataset.Head.Value - entry.Projection.Position.Value);
        }

        return lags;
    }

    /// <summary>Brings <paramref name="dataset"/>'s projections in line with the classifier bindings it declares.</summary>
    internal void Reconcile(DatasetName dataset)
    {
        Dataset? held = _datasets.TryResolve(dataset, out Dataset? open) ? open : null;
        Dictionary<string, ClassifierBinding> wanted = new(StringComparer.Ordinal);

        if (held is not null && _declarations().Datasets.TryGetValue(dataset.Value, out DatasetDeclaration? declared))
        {
            foreach (ClassifierBinding binding in Declared(declared))
            {
                wanted[Identity(binding)] = binding;
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

            foreach ((string key, ClassifierBinding binding) in wanted)
            {
                if (!_entries.ContainsKey((dataset.Value, key)))
                {
                    Entry entry = new(key, held!, new ClassificationProjection(held!, binding), _datasets.LocalDerivedOf(dataset), _wait);
                    _entries[(dataset.Value, key)] = entry;
                    entry.Start(_logger);
                }
            }
        }

        foreach (Entry entry in stopped)
        {
            _ = entry.StopAsync();
        }
    }

    /// <summary>The classifier bindings a dataset declares, as the values <c>Varve.Classification</c> takes.</summary>
    internal static List<ClassifierBinding> Declared(DatasetDeclaration declared)
    {
        List<ClassifierBinding> bindings = [];

        foreach (BindingDeclaration binding in declared.Bindings)
        {
            if (binding.Kind == BindingKind.Classifier && binding.Classifies is { } graphs)
            {
                GraphScope scope = graphs.All
                    ? GraphScope.All
                    : GraphScope.Of([.. graphs.Graphs], [.. graphs.Prefixes], graphs.DefaultGraph ? DefaultGraphAccess.Included : DefaultGraphAccess.Excluded);
                bindings.Add(new ClassifierBinding(binding.Node, binding.BoundGraph, scope));
            }
        }

        return bindings;
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

    private List<Entry> EntriesOf(DatasetName dataset)
    {
        Reconcile(dataset);
        List<Entry> entries = [];

        lock (_lock)
        {
            foreach (((string name, string _), Entry entry) in _entries)
            {
                if (name == dataset.Value)
                {
                    entries.Add(entry);
                }
            }
        }

        entries.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return entries;
    }

    private static string Identity(ClassifierBinding binding)
    {
        StringBuilder key = new();
        key.Append(Encoding.UTF8.GetString(binding.Binding.Lexical)).Append('|')
            .Append(Encoding.UTF8.GetString(binding.ShapesGraph.Lexical)).Append('|')
            .Append(binding.Classifies.IsAll).Append(binding.Classifies.IncludesDefault);

        foreach (RdfTerm graph in binding.Classifies.Graphs)
        {
            key.Append('<').Append(Encoding.UTF8.GetString(graph.Lexical)).Append('>');
        }

        foreach (string prefix in binding.Classifies.Prefixes)
        {
            key.Append('"').Append(prefix).Append('"');
        }

        return key.ToString();
    }

    /// <summary>One binding's projection and its feed.</summary>
    private sealed class Entry : IAsyncDisposable
    {
        private readonly IDerivedStore? _cache;
        private readonly TimeSpan _wait;
        private readonly CancellationTokenSource _stop = new();
        private Task _feed = Task.CompletedTask;

        internal Entry(string key, Dataset dataset, ClassificationProjection projection, IDerivedStore? cache, TimeSpan wait)
        {
            Key = key;
            Dataset = dataset;
            Projection = projection;
            _cache = cache;
            _wait = wait;
            Name = Encoding.UTF8.GetString(projection.Binding.Binding.Lexical);
        }

        internal string Name { get; }

        internal string Key { get; }

        internal Dataset Dataset { get; }

        internal ClassificationProjection Projection { get; }

        // The classification at the view's position, after one bounded wait on this thread: a read's match is synchronous.
        internal IRowClassification At(DatasetView view)
        {
            if (Projection.TryAt(view.Position, view, out IRowClassification rows))
            {
                return rows;
            }

            try
            {
                using CancellationTokenSource bound = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                bound.CancelAfter(_wait);
                Projection.WaitForAsync(view.Position, bound.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ended) when (ended is OperationCanceledException or ObjectDisposedException)
            {
                // The wait ran out, or the projection stopped: either way it has not reached the position.
            }

            return Projection.TryAt(view.Position, view, out rows)
                ? rows
                : throw new ClassificationBehindException(Projection.Binding.Binding, view.Position, Projection.Position);
        }

        internal void Start(ILogger logger) => _feed = Task.Run(() => FeedAsync(logger, _stop.Token));

        public ValueTask DisposeAsync() => new(StopAsync());

        internal async Task StopAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);

            try
            {
                await _feed.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            if (_cache is not null)
            {
                try
                {
                    await Projection.SaveAsync(_cache, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is System.IO.IOException or ObjectDisposedException or InvalidOperationException)
                {
                    // Derived and droppable: the next start rebuilds what was not saved.
                }
            }

            _stop.Dispose();
        }

        private async Task FeedAsync(ILogger logger, CancellationToken stopping)
        {
            try
            {
                if (_cache is not null && await Projection.LoadAsync(_cache, stopping).ConfigureAwait(false))
                {
                    ClassificationLog.Loaded(logger, Name, Projection.Position.Value);
                }
                else if (Dataset.Head.Value > 0)
                {
                    // A cold start: a generation at the head, which is the projection's floor (ADR 0233).
                    using DatasetView head = Dataset.Pin();
                    await Projection.ResetAsync(head, stopping).ConfigureAwait(false);
                }

                int applied = 0;

                await foreach (Commit commit in Dataset.Subscribe(Projection.Position, SubscriptionFilter.All, stopping).ConfigureAwait(false))
                {
                    await Projection.ApplyAsync(commit, stopping).ConfigureAwait(false);

                    if (_cache is not null && ++applied % SaveEvery == 0)
                    {
                        await Projection.SaveAsync(_cache, stopping).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
            }
            catch (Exception failure) when (failure is DatasetUnavailableException or ObjectDisposedException or System.IO.IOException or InvalidOperationException)
            {
                ClassificationLog.Stopped(logger, Name, failure.Message);
            }
        }
    }

    private static partial class ClassificationLog
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "The classification projection of {Binding} read its saved state at position {Position}.")]
        internal static partial void Loaded(ILogger logger, string binding, long position);

        [LoggerMessage(Level = LogLevel.Warning, Message = "The classification projection of {Binding} stopped: {Reason}. It starts again when its dataset is next reconciled.")]
        internal static partial void Stopped(ILogger logger, string binding, string reason);
    }
}
