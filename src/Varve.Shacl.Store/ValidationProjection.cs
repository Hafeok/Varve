// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Shacl.Store.Model;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Shacl.Store;

/// <summary>
/// A binding's validation projection (ADR 0174): the report at every closed
/// position, maintained commit by commit. Its rows are keyed by (shape, focus
/// node), the shape the one that targets the node, each holding that pair's
/// results with a validity interval <c>[asserted, retracted)</c>. A data
/// commit replaces the rows of the pairs its delta affects (ADR 0171); a
/// shapes change starts a generation, a full validation that replaces every
/// open row and is published only when complete.
/// </summary>
/// <remarks>
/// Fed by <see cref="Dataset.CatchUpAsync"/> or a subscription, from one
/// caller at a time; read from any thread. The report at a position equals
/// <see cref="BindingValidation.Validate"/> over <see cref="Dataset.AsOfAsync"/>
/// there, byte for byte in canonical N-Quads.
/// </remarks>
[Contract(typeof(TheValidationProjection.OneProjectionPerBinding), Role = "a binding's validation projection")]
public sealed class ValidationProjection : IProjection
{
    private readonly Dataset _dataset;
    private readonly ShaclOptions _options;
    private readonly Lock _lock = new();
    private readonly List<Row> _rows = [];
    private readonly Dictionary<(RdfTerm Shape, RdfTerm Focus), int> _open = new(PairComparer.Instance);
    private readonly List<Generation> _generations = [];
    private ShapesGraph? _shapes;
    private long _position;
    private long _floor;
    private TaskCompletionSource _advanced = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// The projection of <paramref name="binding"/> over <paramref name="dataset"/>,
    /// empty at position 0 until it is fed. <paramref name="options"/> are as
    /// for <see cref="BindingValidation.Validate"/>.
    /// </summary>
    public ValidationProjection(Dataset dataset, ShapesBinding binding, ShaclOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(binding);
        _dataset = dataset;
        Binding = binding;
        _options = BindingValidation.Options(binding, options);
        _generations.Add(Empty(0));
    }

    /// <summary>The binding projected.</summary>
    public ShapesBinding Binding { get; }

    /// <summary>
    /// The reserved graph the report is read as:
    /// <c>https://w3id.org/varve/ns#validation/{enc(binding IRI)}</c>, every
    /// byte outside RFC 3986's unreserved set percent-encoded (ADR 0174).
    /// </summary>
    public RdfTerm ReportGraph => RdfTerm.Iri(Encoding.UTF8.GetBytes("https://w3id.org/varve/ns#validation/" + ProjectionSnapshot.Encode(Binding.Binding.Lexical)));

    /// <inheritdoc />
    public Position Position => new(Volatile.Read(ref _position));

    /// <summary>
    /// The lowest position a report is held for: 0, or the checkpoint's
    /// position after a reset from one. Below it, nothing is held.
    /// </summary>
    public Position Floor => new(Volatile.Read(ref _floor));

    /// <inheritdoc />
    public async ValueTask ApplyAsync(Commit commit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);
        long position = commit.Position.Value;
        if (position <= Volatile.Read(ref _position))
        {
            return;
        }

        // A projection that keeps up reads its commit at the head, through the index; one catching up reads it as of.
        DatasetView view = _dataset.Pin();
        if (view.Position != commit.Position)
        {
            view.Dispose();
            view = await _dataset.AsOfAsync(commit.Position, cancellationToken).ConfigureAwait(false);
        }

        using (view)
        {
            Apply(view, commit.Delta, position, cancellationToken);
        }

        Advance(position);
    }

    /// <inheritdoc />
    public async ValueTask ResetAsync(DatasetView? checkpoint, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _rows.Clear();
            _open.Clear();
            _generations.Clear();
            _shapes = null;
        }

        if (checkpoint is null)
        {
            lock (_lock)
            {
                _generations.Add(Empty(0));
            }

            Volatile.Write(ref _floor, 0);
            Volatile.Write(ref _position, 0);
            return;
        }

        long position = checkpoint.Position.Value;
        Full(checkpoint, position, cancellationToken);
        Volatile.Write(ref _floor, position);
        Volatile.Write(ref _position, position);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// The report at <paramref name="position"/>: <see langword="false"/> when
    /// the projection has not reached it, or it is below <see cref="Floor"/>.
    /// The outcome carries the problems instead of a report where the shapes
    /// graph did not load or the validation failed there.
    /// </summary>
    public bool TryReportAt(Position position, out ValidationOutcome outcome)
    {
        long at = position.Value;
        List<ValidationReport> parts = [];
        Generation generation;
        lock (_lock)
        {
            if (at > _position || at < _floor || _generations.Count == 0)
            {
                outcome = null!;
                return false;
            }

            generation = _generations[0];
            foreach (Generation candidate in _generations)
            {
                if (candidate.Start <= at)
                {
                    generation = candidate;
                }
            }

            if (generation.Header is not null)
            {
                foreach (Row row in _rows)
                {
                    if (row.Start <= at && at < row.End)
                    {
                        parts.Add(row.Results);
                    }
                }
            }
        }

        outcome = generation.Header is { } header
            ? ValidationOutcome.Reported(ValidationReport.Compose(header, parts))
            : ValidationOutcome.Failed(generation.Problems);
        return true;
    }

    /// <summary>
    /// The report at <paramref name="position"/> restricted to
    /// <paramref name="pairs"/>' rows: what ADR 0173's digest of a commit at
    /// that position is over, when the pairs are the commit's affected ones.
    /// Null where <see cref="TryReportAt"/> gives no report.
    /// </summary>
    internal ValidationReport? RestrictedReportAt(Position position, IEnumerable<FocusPair> pairs)
    {
        HashSet<(RdfTerm, RdfTerm)> wanted = new(PairComparer.Instance);
        foreach (FocusPair pair in pairs)
        {
            wanted.Add((pair.Shape, pair.FocusNode));
        }

        long at = position.Value;
        List<ValidationReport> parts = [];
        ValidationReport? header = null;
        lock (_lock)
        {
            if (at > _position || at < _floor)
            {
                return null;
            }

            foreach (Generation candidate in _generations)
            {
                if (candidate.Start <= at)
                {
                    header = candidate.Header;
                }
            }

            foreach (Row row in _rows)
            {
                if (row.Start <= at && at < row.End && wanted.Contains((row.Shape, row.Focus)))
                {
                    parts.Add(row.Results);
                }
            }
        }

        return header is null ? null : ValidationReport.Compose(header, parts);
    }

    /// <summary>
    /// Waits until the projection reaches <paramref name="position"/>: the
    /// bounded wait a read of the report graph makes before
    /// <c>validation-behind</c>, bounded by the caller's token.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled first.</exception>
    public async ValueTask WaitForAsync(Position position, CancellationToken cancellationToken)
    {
        while (Volatile.Read(ref _position) < position.Value)
        {
            Task advanced;
            lock (_lock)
            {
                advanced = _advanced.Task;
            }

            if (Volatile.Read(ref _position) >= position.Value)
            {
                return;
            }

            await advanced.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes the projection's state to <paramref name="store"/>, the local
    /// cache's derived store (ADR 0174), under
    /// <c>validation/{enc(binding)}/</c>, and deletes the older states there.
    /// Its position is written with its state, in one blob, in the dataset's
    /// checked envelope (ADR 0213).
    /// </summary>
    public async ValueTask SaveAsync(IDerivedStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArrayBufferWriter<byte> bytes = new();
        long position;
        lock (_lock)
        {
            position = _position;
            ProjectionSnapshot.Header(bytes, Binding, _position, _floor);
            ProjectionSnapshot.Count(bytes, _generations.Count);
            foreach (Generation generation in _generations)
            {
                ProjectionSnapshot.Long(bytes, generation.Start);
                if (generation.Header is { } header)
                {
                    ProjectionSnapshot.Byte(bytes, 1);
                    ArrayBufferWriter<byte> report = new();
                    header.WriteTo(report);
                    ProjectionSnapshot.Blob(bytes, report.WrittenSpan);
                }
                else
                {
                    ProjectionSnapshot.Byte(bytes, 0);
                }
            }

            ProjectionSnapshot.Count(bytes, _rows.Count);
            ArrayBufferWriter<byte> results = new();
            foreach (Row row in _rows)
            {
                ProjectionSnapshot.Term(bytes, row.Shape);
                ProjectionSnapshot.Term(bytes, row.Focus);
                ProjectionSnapshot.Long(bytes, row.Start);
                ProjectionSnapshot.Long(bytes, row.End);
                results.ResetWrittenCount();
                row.Results.WriteTo(results);
                ProjectionSnapshot.Blob(bytes, results.WrittenSpan);
            }
        }

        BlobName name = new(ProjectionSnapshot.Name(Binding, position));
        await _dataset.WriteCheckedAsync(store, name, new Position(position), bytes.WrittenMemory, cancellationToken).ConfigureAwait(false);

        string directory = ProjectionSnapshot.Directory(Binding);
        foreach (BlobName existing in await store.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (existing.Value.StartsWith(directory, StringComparison.Ordinal) && existing != name)
            {
                await store.DeleteAsync(existing, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Reads the newest state <see cref="SaveAsync"/> wrote for this binding
    /// to <paramref name="store"/>: whether one was read. A state that does
    /// not verify (ADR 0213), that is bound to another dataset or commit, for
    /// another binding's identity or format, or that does not read is a
    /// miss, and the projection stays as it was — empty on a cold start, to
    /// be rebuilt from the log.
    /// </summary>
    public async ValueTask<bool> LoadAsync(IDerivedStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        string directory = ProjectionSnapshot.Directory(Binding);
        BlobName? newest = null;
        foreach (BlobName existing in await store.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (existing.Value.StartsWith(directory + "state-", StringComparison.Ordinal) && (newest is null || existing > newest.Value))
            {
                newest = existing;
            }
        }

        if (newest is not { } name)
        {
            return false;
        }

        if (await _dataset.TryReadCheckedAsync(store, name, cancellationToken).ConfigureAwait(false) is not { } content)
        {
            return false;
        }

        byte[] bytes = content.Bytes.ToArray();

        List<Row> rows = [];
        List<(long Start, ValidationReport? Header)> generations = [];
        long position, floor;
        try
        {
            ProjectionSnapshot.Reader reader = new(bytes);
            if (!reader.Header(Binding, out position, out floor) || position != content.Position.Value || floor > position)
            {
                return false;
            }

            int count = reader.Count();
            for (int i = 0; i < count; i++)
            {
                long start = reader.Long();
                generations.Add((start, reader.Byte() == 1 ? ValidationReport.ReadFrom(reader.Blob(), _options) : null));
            }

            count = reader.Count();
            for (int i = 0; i < count; i++)
            {
                RdfTerm shape = reader.Term(), focus = reader.Term();
                long start = reader.Long(), end = reader.Long();
                rows.Add(new Row(shape, focus, ValidationReport.ReadFrom(reader.Blob(), _options), start, end));
            }

            if (!reader.AtEnd || generations.Count == 0)
            {
                return false;
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Bytes that verified and still do not decode are a miss as
            // well, never an error (ADR 0213).
            return false;
        }

        // A failed generation's problems are the full validation's at its start; the shapes are the last generation's.
        List<Generation> read = [];
        foreach ((long start, ValidationReport? header) in generations)
        {
            if (header is not null)
            {
                read.Add(new Generation(start, header, []));
                continue;
            }

            using DatasetView view = await _dataset.AsOfAsync(new Position(start), cancellationToken).ConfigureAwait(false);
            ValidationOutcome outcome = BindingValidation.Validate(Binding, view, _options, cancellationToken);
            read.Add(new Generation(start, null, outcome.Problems.Count > 0 ? outcome.Problems : [new ShaclProblem(ShaclProblemKind.ExtensionFailure, null, null, "The validation failed at this position.")]));
        }

        ShapesGraph? shapes = null;
        if (read[^1].Header is not null)
        {
            using DatasetView view = await _dataset.AsOfAsync(new Position(position), cancellationToken).ConfigureAwait(false);
            shapes = BindingValidation.Load(Binding, view, _options).Shapes;
        }

        lock (_lock)
        {
            _rows.Clear();
            _rows.AddRange(rows);
            _open.Clear();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].End == long.MaxValue)
                {
                    _open[(_rows[i].Shape, _rows[i].Focus)] = i;
                }
            }

            _generations.Clear();
            _generations.AddRange(read);
            _shapes = shapes;
            Volatile.Write(ref _floor, floor);
        }

        Advance(position);
        return true;
    }

    private void Advance(long position)
    {
        TaskCompletionSource advanced;
        lock (_lock)
        {
            Volatile.Write(ref _position, position);
            advanced = _advanced;
            _advanced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        advanced.TrySetResult();
    }

    private void Apply(DatasetView view, QuadDelta delta, long position, CancellationToken cancellationToken)
    {
        if (_shapes is null || BindingValidation.Touches(view, delta, Binding.ShapesGraph))
        {
            Full(view, position, cancellationToken);
            return;
        }

        DataGraphSource data = new(view, Binding);
        QuadDelta changes = BindingValidation.InDataGraph(data, delta);
        if (changes.IsEmpty)
        {
            return;
        }

        IncrementalOutcome outcome = IncrementalValidation.Compare(_shapes, data, GraphPattern.Any, changes, _options, cancellationToken);
        if (outcome.Comparison is not { } comparison)
        {
            // A comparison that fails decides nothing: the position is validated in full.
            Full(view, position, cancellationToken);
            return;
        }

        lock (_lock)
        {
            foreach (FocusPair pair in comparison.Pairs)
            {
                (RdfTerm, RdfTerm) key = (pair.Shape, pair.FocusNode);
                if (_open.Remove(key, out int index))
                {
                    _rows[index] = _rows[index] with { End = position };
                }

                if (pair.Results is { Results.Count: > 0 } results)
                {
                    _open[key] = _rows.Count;
                    _rows.Add(new Row(pair.Shape, pair.FocusNode, results, position, long.MaxValue));
                }
            }
        }
    }

    /// <summary>A generation: the whole validation at <paramref name="position"/>, replacing every open row.</summary>
    private void Full(IQuadSource state, long position, CancellationToken cancellationToken)
    {
        ShapesLoad load = BindingValidation.Load(Binding, state, _options);
        PairedOutcome? validated = load.Shapes is { } shapes
            ? ShaclValidator.ValidateByPair(shapes, new DataGraphSource(state, Binding), GraphPattern.Any, _options, cancellationToken)
            : null;

        lock (_lock)
        {
            foreach (int index in _open.Values)
            {
                _rows[index] = _rows[index] with { End = position };
            }

            _open.Clear();
            if (validated?.Report is not { } report)
            {
                // Failed: the next commit validates in full again.
                _shapes = null;
                _generations.Add(new Generation(position, null, validated?.Problems ?? load.Problems));
                return;
            }

            _shapes = load.Shapes;
            foreach (FocusPair pair in validated.Pairs)
            {
                _open[(pair.Shape, pair.FocusNode)] = _rows.Count;
                _rows.Add(new Row(pair.Shape, pair.FocusNode, pair.Results!, position, long.MaxValue));
            }

            _generations.Add(new Generation(position, report, []));
        }
    }

    /// <summary>The generation of an empty dataset: no shapes, no data, a report with no result.</summary>
    private Generation Empty(long position)
    {
        ValidationOutcome outcome = BindingValidation.Validate(Binding, new InMemoryDatasetBuilder().ToDataset(), _options);
        return new Generation(position, outcome.Report, outcome.Problems);
    }

    /// <summary>A pair's results over <c>[Start, End)</c>; an open row ends at <see cref="long.MaxValue"/>.</summary>
    private readonly record struct Row(RdfTerm Shape, RdfTerm Focus, ValidationReport Results, long Start, long End);

    /// <summary>From <c>Start</c>, the shapes' report header, or the problems that refused them.</summary>
    private sealed record Generation(long Start, ValidationReport? Header, IReadOnlyList<ShaclProblem> Problems);

    private sealed class PairComparer : IEqualityComparer<(RdfTerm Shape, RdfTerm Focus)>
    {
        internal static readonly PairComparer Instance = new();

        public bool Equals((RdfTerm Shape, RdfTerm Focus) x, (RdfTerm Shape, RdfTerm Focus) y) =>
            RdfTerm.Comparer.Equals(x.Shape, y.Shape) && RdfTerm.Comparer.Equals(x.Focus, y.Focus);

        public int GetHashCode((RdfTerm Shape, RdfTerm Focus) obj) =>
            HashCode.Combine(RdfTerm.Comparer.GetHashCode(obj.Shape), RdfTerm.Comparer.GetHashCode(obj.Focus));
    }
}
