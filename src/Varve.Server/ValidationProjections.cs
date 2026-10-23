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
using Varve.Management.Model;
using Varve.Protocol;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Shacl;
using Varve.Shacl.Store;
using Varve.Shacl.Store.Model;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Server;

/// <summary>
/// The server's validation projections (ADR 0174): one per acting shapes
/// binding of each dataset this instance holds open, under the declarations
/// in force, each fed the dataset's commits by a subscription of its own and
/// served as its reserved report graph. A file dataset's projection keeps its
/// state in the dataset's own <c>derived/</c>; any other is rebuilt from the
/// log when the instance starts. A projection's lag is reported on
/// readiness and never makes the dataset unready.
/// </summary>
[Contract(typeof(TheValidationProjection.OneProjectionPerBinding), Role = "the server's validation projections, fed and served")]
internal sealed partial class ValidationProjections : IValidationReports, IAsyncDisposable
{
    /// <summary>How many commits a projection applies between two saves of its state.</summary>
    private const int SaveEvery = 1_000;

    private readonly Func<Declarations> _declarations;
    private readonly OpenDatasets _datasets;
    private readonly TimeSpan _wait;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Dataset, string Binding), Entry> _entries = [];
    private bool _disposed;

    /// <summary>Projections of <paramref name="datasets"/> under the declarations <paramref name="declarations"/> answers; a read waits up to <paramref name="wait"/> for one to reach its position.</summary>
    [DesignDecision(typeof(TheValidationProjection.ValidationBehind), Scope = ExceptionScope.Boundary)]
    internal ValidationProjections(Func<Declarations> declarations, OpenDatasets datasets, TimeSpan wait, ILogger logger)
    {
        _declarations = declarations;
        _datasets = datasets;
        _wait = wait;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<IValidationReportGraph> Of(DatasetName dataset)
    {
        Reconcile(dataset);
        List<IValidationReportGraph> graphs = [];
        lock (_lock)
        {
            foreach (((string name, string _), Entry entry) in _entries)
            {
                if (name == dataset.Value)
                {
                    graphs.Add(entry);
                }
            }
        }

        return graphs;
    }

    /// <summary>Each binding's lag behind its dataset's head, by binding IRI, for readiness.</summary>
    internal SortedDictionary<string, long> LagOf(DatasetName dataset)
    {
        Reconcile(dataset);
        SortedDictionary<string, long> lags = new(StringComparer.Ordinal);
        lock (_lock)
        {
            foreach (((string name, string _), Entry entry) in _entries)
            {
                if (name == dataset.Value)
                {
                    lags[Encoding.UTF8.GetString(entry.Binding.Lexical)] = Math.Max(0, entry.Dataset.Head.Value - entry.Projection.Position.Value);
                }
            }
        }

        return lags;
    }

    /// <summary>
    /// Brings <paramref name="dataset"/>'s projections in line with its
    /// acting bindings: a new or changed binding gets a projection, fed from
    /// its saved state or from the start of the log; a binding gone, a
    /// dataset closed or reopened, stops its own.
    /// </summary>
    internal void Reconcile(DatasetName dataset)
    {
        Dataset? held = _datasets.TryResolve(dataset, out Dataset? open) ? open : null;
        Dictionary<string, ShapesBinding> wanted = new(StringComparer.Ordinal);
        if (held is not null && _declarations().Datasets.TryGetValue(dataset.Value, out DatasetDeclaration? declared))
        {
            foreach (ShapesBinding binding in ShapesBindings.Acting(declared))
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

            foreach ((string key, ShapesBinding binding) in wanted)
            {
                if (!_entries.ContainsKey((dataset.Value, key)))
                {
                    Entry entry = new(key, held!, new ValidationProjection(held!, binding), _datasets.LocalDerivedOf(dataset), _wait);
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

    /// <summary>What makes two bindings the same projection: everything the report depends on, and not the mode.</summary>
    private static string Identity(ShapesBinding binding)
    {
        StringBuilder key = new();
        key.Append(Encoding.UTF8.GetString(binding.Binding.Lexical)).Append('|')
            .Append(Encoding.UTF8.GetString(binding.ShapesGraph.Lexical)).Append('|')
            .Append(binding.SubClassOfInShapesGraph).Append('|')
            .Append(binding.Validates.IsAll).Append(binding.Validates.IncludesDefault);
        foreach (RdfTerm graph in binding.Validates.Graphs)
        {
            key.Append('<').Append(Encoding.UTF8.GetString(graph.Lexical)).Append('>');
        }

        foreach (string prefix in binding.Validates.Prefixes)
        {
            key.Append('"').Append(prefix).Append('"');
        }

        return key.ToString();
    }

    /// <summary>One binding's projection, its feed, and its report graph.</summary>
    private sealed class Entry : IValidationReportGraph, IAsyncDisposable
    {
        private readonly IDerivedStore? _cache;
        private readonly TimeSpan _wait;
        private readonly CancellationTokenSource _stop = new();
        private Task _feed = Task.CompletedTask;

        internal Entry(string key, Dataset dataset, ValidationProjection projection, IDerivedStore? cache, TimeSpan wait)
        {
            Key = key;
            Dataset = dataset;
            Projection = projection;
            _cache = cache;
            _wait = wait;
            Graph = projection.ReportGraph;
            Name = Encoding.UTF8.GetString(projection.Binding.Binding.Lexical);
        }

        /// <summary>The binding's IRI, as text for the log.</summary>
        internal string Name { get; }

        internal string Key { get; }

        internal Dataset Dataset { get; }

        internal ValidationProjection Projection { get; }

        public RdfTerm Binding => Projection.Binding.Binding;

        public RdfTerm Graph { get; }

        public GraphScope Validates => Projection.Binding.Validates;

        public bool TryReportAt(Position position, out ValidationOutcome outcome) => Projection.TryReportAt(position, out outcome);

        public ValidationOutcome ReportAt(Position position)
        {
            if (Projection.TryReportAt(position, out ValidationOutcome outcome))
            {
                return outcome;
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

            return Projection.TryReportAt(position, out outcome)
                ? outcome
                : throw new ValidationBehindException(Binding, position, Projection.Position);
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
                    ProjectionLog.Loaded(logger, Name, Projection.Position.Value);
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
                // The dataset closed or failed under the feed: its next reconcile starts another.
                ProjectionLog.Stopped(logger, Name, failure.Message);
            }
        }
    }

    private static partial class ProjectionLog
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "The validation projection of {Binding} read its saved state at position {Position}.")]
        internal static partial void Loaded(ILogger logger, string binding, long position);

        [LoggerMessage(Level = LogLevel.Warning, Message = "The validation projection of {Binding} stopped: {Reason}. It starts again when its dataset is next reconciled.")]
        internal static partial void Stopped(ILogger logger, string binding, string reason);
    }
}
