// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Inference.Model;
using Varve.Inference.Store.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Inference.Store;

/// <summary>
/// A materialising binding's projection (ADR 0195): its implicit quads at
/// every position it holds, maintained commit by commit. Each row is one
/// implicit quad with a validity interval <c>[asserted, retracted)</c>,
/// placed as ADR 0190 places it: a conclusion drawn from the TBox graphs
/// alone in the binding's <c>varve:inferred/{enc(binding)}/tbox</c> graph,
/// any other in the data graph it was drawn in.
/// </summary>
/// <remarks>
/// <para>
/// Each data graph has a closure of its own quads and the TBox graphs', and
/// the TBox graphs one of theirs. A data commit is applied to the closures it
/// touches, and a TBox commit to every one; the rows change where the
/// closures do. A commit that allocates a term a rule names, or whose pass
/// fails, starts a generation: the closures are rebuilt from the commit's
/// state, and only the rows that differ open or close.
/// </para>
/// <para>
/// Closed rows are held up to <see cref="InferenceOptions.HistoryBytes"/>,
/// oldest positions out first (ADR 0195 point 5a), so what is held is the
/// window <c>[HeldFrom, Position]</c>. A read below it, or below the floor, is
/// from scratch. The closures together hold at most
/// <see cref="InferenceOptions.ClosureBytes"/>; past it the binding stops,
/// serves nothing at or above the commit that stopped it, and is tried again
/// by a commit that changes a TBox graph (point 6).
/// </para>
/// <para>
/// Fed by <see cref="Dataset.CatchUpAsync"/> or a subscription, from one
/// caller at a time; read from any thread.
/// </para>
/// </remarks>
[Contract(typeof(TheInferenceProjection.OneProjectionPerMaterialisingBinding), Role = "a binding's inference projection")]
public sealed class InferenceProjection : IProjection
{
    private const string Reserved = "https://w3id.org/varve/ns#";

    /// <summary>The empty dataset: where a projection at position 0 starts.</summary>
    private static readonly InMemoryDataset Empty = new InMemoryDatasetBuilder().ToDataset();

    private readonly Dataset _dataset;
    private readonly InferenceOptions _options;
    private readonly Lock _rowsLock = new();
    private readonly Lock _applying = new();
    private readonly Dictionary<TermHandle, Dictionary<Fact, long>> _open = [];
    private readonly Queue<ClosedRow> _closed = new();
    private long _closedBytes;
    private long _position;
    private long _floor;
    private long _heldFrom;
    private long _stoppedAt = long.MaxValue;
    private ByteCount _reached;
    private Materialisation? _state;
    private readonly ProjectionTerms _terms = new(Empty);
    private TaskCompletionSource _advanced = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The projection of <paramref name="binding"/> over <paramref name="dataset"/>, at position 0 until it is fed.</summary>
    /// <exception cref="NotSupportedException">The binding is cross-graph, which is not built yet (#171).</exception>
    public InferenceProjection(Dataset dataset, InferenceBinding binding, InferenceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.CrossGraph)
        {
            // A stub, reported on #171: placement by premise set is the next slice.
            throw new NotSupportedException("A cross-graph binding's projection is not built yet (#171): its conclusions are placed by premise set (ADR 0190 point 6).");
        }

        _dataset = dataset;
        Binding = binding;
        _options = options ?? new InferenceOptions();
        TBoxGraph = InferredTBox(binding.Binding.Lexical);
        Rebuild(Empty, 0);
    }

    /// <summary>The binding projected.</summary>
    public InferenceBinding Binding { get; }

    /// <summary>
    /// The reserved graph a TBox-only conclusion is placed in:
    /// <c>https://w3id.org/varve/ns#inferred/{enc(binding IRI)}/tbox</c>, every
    /// byte outside RFC 3986's unreserved set percent-encoded (ADR 0198).
    /// </summary>
    public RdfTerm TBoxGraph { get; }

    /// <inheritdoc />
    public Position Position => new(Volatile.Read(ref _position));

    /// <summary>The lowest position the projection has held rows for: 0, or the head at a cold start (ADR 0195 point 5).</summary>
    public Position Floor => new(Volatile.Read(ref _floor));

    /// <summary>The lowest position the held history serves (ADR 0195 point 5a): a read below it, or below <see cref="Floor"/>, is from scratch.</summary>
    public Position HeldFrom => new(Math.Max(Volatile.Read(ref _heldFrom), Volatile.Read(ref _floor)));

    /// <summary>The bytes of closed rows held, for the gauge <c>varve.inference.history.bytes</c>.</summary>
    public ByteCount HistoryHeld => new(Volatile.Read(ref _closedBytes));

    /// <summary>Whether the binding is stopped by its memory limit (ADR 0195 point 6).</summary>
    public bool Stopped => Volatile.Read(ref _stoppedAt) != long.MaxValue;

    /// <summary>The limit a stopped binding passed, and what its closures held when it stopped: a lower bound on what they need.</summary>
    public (ByteCount Limit, ByteCount Reached) MemoryLimit => (_options.ClosureBytes, _reached);

    /// <inheritdoc />
    public async ValueTask ApplyAsync(Commit commit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);
        long position = commit.Position.Value;
        if (position <= Volatile.Read(ref _position))
        {
            return;
        }

        DatasetView view = _dataset.Pin();
        if (view.Position != commit.Position)
        {
            view.Dispose();
            view = await _dataset.AsOfAsync(commit.Position, cancellationToken).ConfigureAwait(false);
        }

        using (view)
        {
            lock (_applying)
            {
                Apply(view, commit.Delta, position);
            }
        }

        Advance(position);
    }

    /// <inheritdoc />
    public ValueTask ResetAsync(DatasetView? checkpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_applying)
        {
            lock (_rowsLock)
            {
                _open.Clear();
                _closed.Clear();
                _closedBytes = 0;
                _heldFrom = 0;
            }

            _state?.Release();
            _state = null;
            Volatile.Write(ref _stoppedAt, long.MaxValue);
            long position = checkpoint?.Position.Value ?? 0;
            Rebuild(checkpoint is null ? Empty : checkpoint, position);
            Volatile.Write(ref _floor, position);
            Volatile.Write(ref _position, position);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The rows valid at <paramref name="at"/>, when they are held; otherwise
    /// what a read must do instead: wait, compute from scratch, or refuse.
    /// </summary>
    public InferenceReadOutcome TryRead(Position at, out InferredRows? rows)
    {
        rows = null;
        long position = at.Value;
        List<InferredRow> found = [];
        lock (_rowsLock)
        {
            if (position > _position)
            {
                return InferenceReadOutcome.Behind;
            }

            if (position >= _stoppedAt)
            {
                return InferenceReadOutcome.Unavailable;
            }

            if (position < Math.Max(_heldFrom, _floor))
            {
                return InferenceReadOutcome.FromScratch;
            }

            foreach ((TermHandle graph, Dictionary<Fact, long> open) in _open)
            {
                foreach ((Fact fact, long start) in open)
                {
                    if (start <= position)
                    {
                        found.Add(new InferredRow(graph, fact));
                    }
                }
            }

            foreach (ClosedRow row in _closed)
            {
                if (row.Start <= position && position < row.End)
                {
                    found.Add(new InferredRow(row.Graph, row.Fact));
                }
            }

        }

        ProjectionTerms terms = _terms;
        rows = new InferredRows(found, handle => ProjectionTerms.IsLocal(handle) && terms.TryExternalise(handle, out RdfTerm? term) ? term : null);
        return InferenceReadOutcome.Held;
    }

    /// <summary>
    /// The binding's implicit quads as of <paramref name="at"/>, computed from
    /// scratch: what a read below the floor or the held history is answered
    /// with (ADR 0195 points 5 and 5a), under the projection's memory limit.
    /// </summary>
    /// <exception cref="ClosureLimitExceededException">The closure as of <paramref name="at"/> would pass the limit.</exception>
    public async ValueTask<InferredRows> ReadFromScratchAsync(Position at, CancellationToken cancellationToken)
    {
        using DatasetView view = await _dataset.AsOfAsync(at, cancellationToken).ConfigureAwait(false);
        Materialisation built = Materialisation.Build(view, new ProjectionTerms(view), this, cancellationToken);
        try
        {
            List<InferredRow> rows = [];
            foreach ((TermHandle graph, Fact fact) in built.Rows())
            {
                rows.Add(new InferredRow(graph, fact));
            }

            ProjectionTerms terms = built.Terms;
            return new InferredRows(rows, handle => ProjectionTerms.IsLocal(handle) && terms.TryExternalise(handle, out RdfTerm? term) ? term : null);
        }
        finally
        {
            built.Release();
        }
    }

    /// <summary>
    /// The clashes in the binding's closures at <see cref="Position"/> (ADR
    /// 0197 point 5): those drawn from the TBox graphs alone placed in
    /// <see cref="TBoxGraph"/>, the rest in the data graph whose closure
    /// holds them. None while the binding is stopped.
    /// </summary>
    public IReadOnlyList<PlacedClash> Clashes()
    {
        lock (_applying)
        {
            return _state?.Clashes() ?? [];
        }
    }

    /// <summary>
    /// Writes the projection's state to <paramref name="store"/>, the local
    /// cache's derived store (ADR 0195 point 5), under
    /// <c>inference/{enc(binding)}/</c>, in the dataset's checked envelope
    /// (ADR 0213), and deletes the older states there: the positions, the
    /// projection's own terms, and every row it holds.
    /// </summary>
    public async ValueTask SaveAsync(IDerivedStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArrayBufferWriter<byte> bytes = new();
        long position;
        lock (_applying)
        {
            lock (_rowsLock)
            {
                position = _position;
                bytes.Write(StateSnapshot.Magic);
                ArrayBufferWriter<byte> identity = new();
                StateSnapshot.Identity(identity, Binding);
                StateSnapshot.Blob(bytes, identity.WrittenSpan);
                StateSnapshot.Long(bytes, _position);
                StateSnapshot.Long(bytes, _floor);
                StateSnapshot.Long(bytes, _heldFrom);
                StateSnapshot.Long(bytes, _stoppedAt);
                StateSnapshot.Long(bytes, _reached.Value);
                IReadOnlyList<RdfTerm> locals = _terms.LocalTable();
                StateSnapshot.Count(bytes, locals.Count);
                foreach (RdfTerm term in locals)
                {
                    StateSnapshot.Term(bytes, term);
                }

                StateSnapshot.Count(bytes, _open.Count);
                foreach ((TermHandle graph, Dictionary<Fact, long> rows) in _open)
                {
                    StateSnapshot.Handle(bytes, graph);
                    StateSnapshot.Count(bytes, rows.Count);
                    foreach ((Fact fact, long start) in rows)
                    {
                        StateSnapshot.Fact(bytes, fact);
                        StateSnapshot.Long(bytes, start);
                    }
                }

                StateSnapshot.Count(bytes, _closed.Count);
                foreach (ClosedRow row in _closed)
                {
                    StateSnapshot.Handle(bytes, row.Graph);
                    StateSnapshot.Fact(bytes, row.Fact);
                    StateSnapshot.Long(bytes, row.Start);
                    StateSnapshot.Long(bytes, row.End);
                }
            }
        }

        BlobName name = new(StateSnapshot.Name(Binding, position));
        await _dataset.WriteCheckedAsync(store, name, new Position(position), bytes.WrittenMemory, cancellationToken).ConfigureAwait(false);

        string directory = StateSnapshot.Directory(Binding);
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
    /// not verify (ADR 0213), that is bound to another dataset or position,
    /// for another binding, that does not decode, or whose open rows are not
    /// the closure the log gives at its position, is a miss, and the
    /// projection stays empty at position 0, to be rebuilt from the log. A
    /// damaged entry is never a wrong entailment.
    /// </summary>
    /// <exception cref="InvalidOperationException">The projection has been fed already.</exception>
    public async ValueTask<bool> LoadAsync(IDerivedStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (Volatile.Read(ref _position) != 0)
        {
            throw new InvalidOperationException("A state is loaded into a projection that has not been fed.");
        }

        string directory = StateSnapshot.Directory(Binding) + "state-";
        BlobName? newest = null;
        foreach (BlobName existing in await store.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (existing.Value.StartsWith(directory, StringComparison.Ordinal) && (newest is null || existing > newest.Value))
            {
                newest = existing;
            }
        }

        if (newest is not { } name || await _dataset.TryReadCheckedAsync(store, name, cancellationToken).ConfigureAwait(false) is not { } content)
        {
            return false;
        }

        Saved saved;
        try
        {
            saved = Decode(content.Bytes.Span);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Bytes that verified and still do not decode are a miss, never an error (ADR 0213).
            return false;
        }

        if (saved.Position != content.Position.Value || saved.Floor > saved.Position)
        {
            return false;
        }

        using DatasetView view = await _dataset.AsOfAsync(content.Position, cancellationToken).ConfigureAwait(false);
        lock (_applying)
        {
            _terms.Restore(saved.Locals);
            Materialisation? built = null;
            if (saved.StoppedAt == long.MaxValue)
            {
                try
                {
                    built = Materialisation.Build(view, _terms, this, cancellationToken);
                }
                catch (ClosureLimitExceededException)
                {
                    _terms.Restore([]);
                    return false;
                }

                HashSet<(TermHandle, Fact)> rows = [.. built.Rows()];
                int open = 0;
                bool agrees = true;
                foreach ((TermHandle graph, Dictionary<Fact, long> facts) in saved.Open)
                {
                    foreach (Fact fact in facts.Keys)
                    {
                        open++;
                        agrees &= rows.Contains((graph, fact));
                    }
                }

                if (!agrees || open != rows.Count)
                {
                    built.Release();
                    _terms.Restore([]);
                    return false;
                }
            }

            lock (_rowsLock)
            {
                _open.Clear();
                foreach ((TermHandle graph, Dictionary<Fact, long> facts) in saved.Open)
                {
                    _open[graph] = facts;
                }

                _closed.Clear();
                foreach (ClosedRow row in saved.Closed)
                {
                    _closed.Enqueue(row);
                }

                _closedBytes = saved.Closed.Count * InferenceOptions.BytesPerClosedRow.Value;
                _heldFrom = saved.HeldFrom;
                Trim();
            }

            _state?.Release();
            _state = built;
            _reached = new ByteCount(saved.Reached);
            Volatile.Write(ref _floor, saved.Floor);
            Volatile.Write(ref _stoppedAt, saved.StoppedAt);
        }

        Advance(saved.Position);
        return true;
    }

    private Saved Decode(ReadOnlySpan<byte> bytes)
    {
        StateSnapshot.Reader reader = new(bytes);
        if (!reader.Header(Binding))
        {
            throw new System.IO.InvalidDataException("A state of another binding or format.");
        }

        long position = reader.Long(), floor = reader.Long(), heldFrom = reader.Long(), stoppedAt = reader.Long(), reached = reader.Long();
        if (reached < 0 || heldFrom < 0 || floor < 0)
        {
            throw new System.IO.InvalidDataException("A state with a negative position or size.");
        }

        List<RdfTerm> locals = [];
        for (int i = reader.Count(); i > 0; i--)
        {
            locals.Add(reader.Term());
        }

        List<(TermHandle, Dictionary<Fact, long>)> open = [];
        for (int i = reader.Count(); i > 0; i--)
        {
            TermHandle graph = reader.Handle();
            Dictionary<Fact, long> rows = [];
            for (int j = reader.Count(); j > 0; j--)
            {
                Fact fact = reader.Fact();
                long start = reader.Long();
                if (start > position || !rows.TryAdd(fact, start))
                {
                    throw new System.IO.InvalidDataException("A row opened above its state, or twice.");
                }
            }

            open.Add((graph, rows));
        }

        List<ClosedRow> closed = [];
        for (int i = reader.Count(); i > 0; i--)
        {
            TermHandle graph = reader.Handle();
            Fact fact = reader.Fact();
            long start = reader.Long(), end = reader.Long();
            if (start >= end || end > position || (closed.Count > 0 && end < closed[^1].End))
            {
                throw new System.IO.InvalidDataException("A closed row out of order.");
            }

            closed.Add(new ClosedRow(graph, fact, start, end));
        }

        if (!reader.AtEnd)
        {
            throw new System.IO.InvalidDataException("A state with bytes after its rows.");
        }

        return new Saved(position, floor, heldFrom, stoppedAt, reached, locals, open, closed);
    }

    /// <summary>A state as decoded, before it is checked against the log.</summary>
    private sealed record Saved(long Position, long Floor, long HeldFrom, long StoppedAt, long Reached, List<RdfTerm> Locals, List<(TermHandle Graph, Dictionary<Fact, long> Rows)> Open, List<ClosedRow> Closed);

    /// <summary>
    /// Waits until the projection reaches <paramref name="position"/>: the
    /// bounded wait a read makes before <c>inference-behind</c>, bounded by
    /// the caller's token.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled first.</exception>
    public async ValueTask WaitForAsync(Position position, CancellationToken cancellationToken)
    {
        while (Volatile.Read(ref _position) < position.Value)
        {
            Task advanced;
            lock (_rowsLock)
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

    /// <summary>Whether a graph is one of the binding's data graphs: in its scope, neither a TBox graph nor under <c>varve:</c>.</summary>
    internal bool IsData(RdfTerm? graph)
    {
        if (graph is null)
        {
            return Binding.DataGraphs.IncludesDefault;
        }

        foreach (RdfTerm tbox in Binding.TBoxGraphs)
        {
            if (tbox.Equals(graph))
            {
                return false;
            }
        }

        return !graph.Lexical.StartsWith(Encoding.UTF8.GetBytes(Reserved)) && Binding.DataGraphs.Allows(graph);
    }

    /// <summary>The facts the binding's closures hold now, for the tests.</summary>
    internal QuadCount FactsHeld => _state?.Budget.Used ?? new QuadCount(0);

    internal QuadCount FactLimit => new(Math.Max(1, _options.ClosureBytes.Value / InferenceOptions.BytesPerFact.Value));

    private void Apply(DatasetView view, QuadDelta delta, long position)
    {
        if (_state is not { } state)
        {
            // Stopped: tried again by a commit that changes a TBox graph.
            if (Touches(view, delta))
            {
                Rebuild(view, position);
            }

            return;
        }

        state.Terms.Source = view;
        foreach (RdfTerm local in state.Terms.LocalTerms)
        {
            if (view.TryInternalise(local, out _))
            {
                // A term a rule names is the dataset's now: recompile over it.
                Rebuild(view, position);
                return;
            }
        }

        Dictionary<TermHandle, HashSet<Fact>> changed;
        try
        {
            changed = state.Apply(view, delta);
        }
        catch (ClosureLimitExceededException)
        {
            Stop(state, position);
            return;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A pass that fails decides nothing: the commit is a generation.
            Rebuild(view, position);
            return;
        }

        lock (_rowsLock)
        {
            foreach ((TermHandle graph, HashSet<Fact> facts) in changed)
            {
                foreach (Fact fact in facts)
                {
                    Set(graph, fact, state.IsRow(graph, fact), position);
                }
            }

            foreach (TermHandle dropped in state.TakeDropped())
            {
                CloseAll(dropped, position);
            }

            Trim();
        }
    }

    /// <summary>
    /// A generation at <paramref name="position"/>: the closures rebuilt from
    /// <paramref name="source"/>, and only the rows that differ opened or
    /// closed. Past the memory limit, the binding stops.
    /// </summary>
    private void Rebuild(IQuadSource source, long position)
    {
        Materialisation built;
        try
        {
            built = Materialisation.Build(source, _terms, this, CancellationToken.None);
        }
        catch (ClosureLimitExceededException)
        {
            _state?.Release();
            _state = null;
            lock (_rowsLock)
            {
                foreach (TermHandle graph in new List<TermHandle>(_open.Keys))
                {
                    CloseAll(graph, position);
                }

                Trim();
            }

            _reached = _options.ClosureBytes;
            Volatile.Write(ref _stoppedAt, position);
            return;
        }

        HashSet<(TermHandle, Fact)> rows = [.. built.Rows()];
        lock (_rowsLock)
        {
            foreach ((TermHandle graph, Dictionary<Fact, long> open) in _open)
            {
                foreach (Fact fact in new List<Fact>(open.Keys))
                {
                    if (!rows.Contains((graph, fact)))
                    {
                        Close(graph, fact, position);
                    }
                }
            }

            foreach ((TermHandle graph, Fact fact) in rows)
            {
                Set(graph, fact, true, position);
            }

            Trim();
        }

        _state?.Release();
        _state = built;
        if (Volatile.Read(ref _stoppedAt) != long.MaxValue)
        {
            // Tried again and fits: nothing was held while it was stopped, so
            // the history starts here, and a read below it is from scratch.
            lock (_rowsLock)
            {
                _heldFrom = Math.Max(_heldFrom, position);
                while (_closed.Count > 0 && _closed.Peek().End <= _heldFrom)
                {
                    _closed.Dequeue();
                    _closedBytes -= InferenceOptions.BytesPerClosedRow.Value;
                }

                Volatile.Write(ref _closedBytes, _closedBytes);
            }

            Volatile.Write(ref _stoppedAt, long.MaxValue);
        }
    }

    private void Stop(Materialisation state, long position)
    {
        _reached = new ByteCount(state.Budget.Used.Value * InferenceOptions.BytesPerFact.Value);
        state.Release();
        _state = null;
        lock (_rowsLock)
        {
            foreach (TermHandle graph in new List<TermHandle>(_open.Keys))
            {
                CloseAll(graph, position);
            }

            Trim();
        }

        Volatile.Write(ref _stoppedAt, position);
    }

    private bool Touches(DatasetView view, QuadDelta delta)
    {
        foreach (Quad quad in delta.Asserted)
        {
            if (IsTBox(view, quad.Graph))
            {
                return true;
            }
        }

        foreach (Quad quad in delta.Retracted)
        {
            if (IsTBox(view, quad.Graph))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsTBox(DatasetView view, TermHandle graph)
    {
        if (graph.IsNone || !view.TryExternalise(graph, out RdfTerm? term))
        {
            return false;
        }

        foreach (RdfTerm tbox in Binding.TBoxGraphs)
        {
            if (tbox.Equals(term))
            {
                return true;
            }
        }

        return false;
    }

    private void Set(TermHandle graph, Fact fact, bool row, long position)
    {
        bool open = _open.TryGetValue(graph, out Dictionary<Fact, long>? rows) && rows.ContainsKey(fact);
        if (row && !open)
        {
            if (rows is null)
            {
                _open[graph] = rows = [];
            }

            rows[fact] = position;
        }
        else if (!row && open)
        {
            Close(graph, fact, position);
        }
    }

    private void Close(TermHandle graph, Fact fact, long position)
    {
        Dictionary<Fact, long> rows = _open[graph];
        long start = rows[fact];
        rows.Remove(fact);
        if (rows.Count == 0)
        {
            _open.Remove(graph);
        }

        if (start < position)
        {
            _closed.Enqueue(new ClosedRow(graph, fact, start, position));
            _closedBytes += InferenceOptions.BytesPerClosedRow.Value;
        }
    }

    private void CloseAll(TermHandle graph, long position)
    {
        if (_open.TryGetValue(graph, out Dictionary<Fact, long>? rows))
        {
            foreach (Fact fact in new List<Fact>(rows.Keys))
            {
                Close(graph, fact, position);
            }
        }
    }

    /// <summary>Drops the oldest closed rows past the ceiling, a whole position at a time (ADR 0195 point 5a).</summary>
    private void Trim()
    {
        while (_closedBytes > _options.HistoryBytes.Value && _closed.Count > 0)
        {
            long end = _closed.Peek().End;
            while (_closed.Count > 0 && _closed.Peek().End <= end)
            {
                _closed.Dequeue();
                _closedBytes -= InferenceOptions.BytesPerClosedRow.Value;
            }

            _heldFrom = Math.Max(_heldFrom, end);
        }

        Volatile.Write(ref _closedBytes, _closedBytes);
    }

    private void Advance(long position)
    {
        TaskCompletionSource advanced;
        lock (_rowsLock)
        {
            Volatile.Write(ref _position, position);
            advanced = _advanced;
            _advanced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        advanced.TrySetResult();
    }

    /// <summary>
    /// The reserved graph <c>varve:inferred/{enc(binding)}/tbox</c> (ADR
    /// 0198): every byte of the binding's IRI outside RFC 3986's unreserved
    /// set percent-encoded.
    /// </summary>
    internal static RdfTerm InferredTBox(ReadOnlySpan<byte> binding)
    {
        StringBuilder text = new(Reserved + "inferred/", Reserved.Length + 16 + binding.Length);
        ReadOnlySpan<byte> utf8 = binding;
        foreach (byte b in utf8)
        {
            bool unreserved = b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9'
                or (byte)'-' or (byte)'.' or (byte)'_' or (byte)'~';
            _ = unreserved ? text.Append((char)b) : text.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return RdfTerm.Iri(Encoding.UTF8.GetBytes(text.Append("/tbox").ToString()));
    }

    /// <summary>A row closed at <c>End</c>: valid over <c>[Start, End)</c>.</summary>
    private readonly record struct ClosedRow(TermHandle Graph, Fact Fact, long Start, long End);
}
