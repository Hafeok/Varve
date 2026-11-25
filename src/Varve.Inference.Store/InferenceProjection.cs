// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
    private readonly ConcurrentDictionary<RdfTerm, RdfTerm[]> _premiseSets = new(RdfTerm.Comparer);
    private long _closedBytes;
    private long _position;
    private long _floor;
    private long _heldFrom;
    private long _stoppedAt = long.MaxValue;
    private ByteCount _reached;
    private Exception? _failure;
    private int _generations;
    private Materialisation? _state;
    private InferenceSnapshot _snapshot = InferenceSnapshot.None;
    private InferencePass? _pass;
    private bool _abandoned;
    private readonly ProjectionTerms _terms = new(Empty);
    private TaskCompletionSource _advanced = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The projection of <paramref name="binding"/> over <paramref name="dataset"/>, at position 0 until it is fed.</summary>
    public InferenceProjection(Dataset dataset, InferenceBinding binding, InferenceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(binding);
        _dataset = dataset;
        Binding = binding;
        _options = options ?? new InferenceOptions();
        TBoxGraph = InferredTBox(binding.Binding.Lexical);
        Rebuild(Empty, 0, CancellationToken.None);
        Publish(0);
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

    /// <summary>
    /// Whether the binding is stopped (ADR 0195 point 6): by its memory limit, or by a pass that failed
    /// (<see cref="Failure"/>). A stopped binding is never tried again by
    /// the projection: it stays refused until its owner makes another.
    /// </summary>
    public bool Stopped => Volatile.Read(ref _stoppedAt) != long.MaxValue;

    /// <summary>
    /// Whether a failure is the store's, which a later read can cure: an
    /// I/O failure, a timeout, a request to an object store that failed. A
    /// pass that meets one holds off and is applied again from the
    /// projection's position, never stopped and never rebuilt for it (ADR
    /// 0195 point 6). A log that does not verify or a format this
    /// build cannot read is not one: the same bytes fail again.
    /// </summary>
    public static bool IsStorageFailure(Exception error) =>
        error is IOException or TimeoutException or System.Net.Http.HttpRequestException;

    /// <summary>The generations made: builds from scratch that replaced the closures, the first included, for the tests.</summary>
    internal Generations GenerationsMade => new(Volatile.Read(ref _generations));

    /// <summary>For the tests: a wrapper over the view each pass reads through, to make the store fail.</summary>
    internal Func<IQuadSource, IQuadSource>? ReadThrough { get; set; }

    /// <summary>Why a stopped binding failed, when it was not its memory limit: the exception its pass threw; null otherwise.</summary>
    public Exception? Failure => Volatile.Read(ref _failure);

    /// <summary>
    /// Stops the binding at its memory limit before it builds anything, at
    /// every position (ADR 0195 point 6): its owner has
    /// no memory to give it, the bindings' pool being empty. Every closure
    /// read is then refused as the memory limit refuses one, and nothing
    /// tries it again; a restart with memory for a pool makes another.
    /// </summary>
    public void Refuse() => StopAll(0, new MemoryAmount(0), failure: null);

    /// <summary>The limit a stopped binding passed, and what its closures held when it stopped: a lower bound on what they need.</summary>
    public (ByteCount Limit, ByteCount Reached) MemoryLimit => (_options.ClosureBytes, _reached);

    /// <summary>
    /// What the projection published at the end of its last pass: read without
    /// waiting for a pass, in constant time (ADR 0195 point 6, amended
    /// 2026-11-25).
    /// </summary>
    public InferenceSnapshot Snapshot => Volatile.Read(ref _snapshot);

    /// <summary>The pass running now, if any: its kind, the position it brings the projection to, and since when.</summary>
    public InferencePass? Pass => Volatile.Read(ref _pass);

    /// <summary>
    /// Whether a pass was cancelled part way: the closures it was changing are
    /// dropped, nothing more is applied or saved, and the position stays where
    /// the last whole pass left it, until a reset builds again.
    /// </summary>
    public bool Abandoned => Volatile.Read(ref _abandoned);

    /// <inheritdoc />
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled during the pass,
    /// within a thousand rule firings: the projection is <see cref="Abandoned"/>.
    /// </exception>
    public async ValueTask ApplyAsync(Commit commit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);
        long position = commit.Position.Value;
        if (position <= Volatile.Read(ref _position))
        {
            return;
        }

        if (Volatile.Read(ref _abandoned))
        {
            throw new InvalidOperationException("A cancelled pass left the projection without closures; reset it before applying more.");
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
                Begin(InferencePassKind.Commit, position);
                try
                {
                    Apply(ReadThrough?.Invoke(view) ?? view, commit.Delta, position, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Abandon();
                    throw;
                }
                finally
                {
                    Volatile.Write(ref _pass, null);
                }
            }
        }

        Advance(position);
    }

    /// <inheritdoc />
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled during the build: the
    /// projection is <see cref="Abandoned"/>.
    /// </exception>
    public ValueTask ResetAsync(DatasetView? checkpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_applying)
        {
            Begin(InferencePassKind.ColdStart, checkpoint?.Position.Value ?? 0);
            try
            {
                Reset(checkpoint, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Abandon();
                throw;
            }
            finally
            {
                Volatile.Write(ref _pass, null);
            }
        }

        return ValueTask.CompletedTask;
    }

    private void Reset(DatasetView? checkpoint, CancellationToken cancellationToken)
    {
        Volatile.Write(ref _abandoned, false);
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
            long position = checkpoint?.Position.Value ?? 0;
            if (Volatile.Read(ref _stoppedAt) != long.MaxValue)
            {
                // A stopped binding is not tried again by a reset: it stays refused from the reset on.
                Volatile.Write(ref _stoppedAt, position);
            }
            else
            {
                Rebuild(checkpoint is null ? Empty : checkpoint, position, cancellationToken);
            }

            Volatile.Write(ref _floor, position);
            Volatile.Write(ref _position, position);
            Publish(position);
        }
    }

    // A pass begins: readiness names it until it ends.
    private void Begin(InferencePassKind kind, long position) =>
        Volatile.Write(ref _pass, new InferencePass(kind, new Position(position), new PassStart(_options.Clock.GetUtcNow())));

    // A pass was cancelled part way: its closures are dropped, and nothing is applied or saved until a reset.
    private void Abandon()
    {
        _state?.Release();
        _state = null;
        Volatile.Write(ref _abandoned, true);
    }

    /// <summary>
    /// Publishes what the pass that ends here leaves (ADR 0195 point 6,
    /// amended 2026-11-25): its clashes, kept per pass and read once here,
    /// and what the closures hold. Under the pass's lock; read without it.
    /// </summary>
    private void Publish(long position)
    {
        IReadOnlyList<PlacedClash> clashes = [];
        if (_state is { } state)
        {
            // A check names terms through the source: a pinned view of its own, at or above every handle the closure holds.
            using DatasetView view = _dataset.Pin();
            state.Terms.Source = view;
            clashes = state.Clashes();
        }

        Volatile.Write(ref _snapshot, new InferenceSnapshot(new Position(position), clashes, PremiseSetGraphs(), ClosureHeld, ClosurePeak));
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
            // A binding stopped at or below the position read is refused before it is waited for: no wait helps (ADR 0195 point 6).
            if (position >= _stoppedAt)
            {
                return InferenceReadOutcome.Unavailable;
            }

            if (position > _position)
            {
                return InferenceReadOutcome.Behind;
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
    /// Explains a quad at <paramref name="at"/> (ADR 0199): in a data graph,
    /// <paramref name="graph"/> null for the default graph, or in
    /// <see cref="TBoxGraph"/>. Answered from the binding's closures at the
    /// projection's position, or below it from scratch; behind and stopped as
    /// a read is.
    /// </summary>
    /// <exception cref="ClosureLimitExceededException">A closure from scratch would pass the binding's limit.</exception>
    public async ValueTask<(InferenceReadOutcome Outcome, Explanation? Explanation)> ExplainAsync(Position at, RdfTerm? graph, RdfTerm subject, RdfTerm predicate, RdfTerm @object, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(@object);
        lock (_rowsLock)
        {
            if (at.Value >= _stoppedAt)
            {
                return (InferenceReadOutcome.Unavailable, null);
            }

            if (at.Value > _position)
            {
                return (InferenceReadOutcome.Behind, null);
            }
        }

        using DatasetView view = await _dataset.AsOfAsync(at, cancellationToken).ConfigureAwait(false);
        if (at.Value == Volatile.Read(ref _position))
        {
            // The live closures are read under the pass's lock, waited for at most
            // InferenceOptions.ExplainWait: a pass that holds it longer is behind
            // the answer, and explain says so rather than wait (ADR 0195 point 6,
            // amended 2026-11-25).
            if (!_applying.TryEnter(_options.ExplainWait.Value))
            {
                return (InferenceReadOutcome.Behind, null);
            }

            try
            {
                if (at.Value == Volatile.Read(ref _position) && _state is { } live)
                {
                    live.Terms.Source = view;
                    return (InferenceReadOutcome.Held, new Explainer(live, view, Binding).Explain(graph, subject, predicate, @object));
                }
            }
            finally
            {
                _applying.Exit();
            }
        }

        Materialisation built = Materialisation.Build(view, new ProjectionTerms(view), this, cancellationToken);
        try
        {
            return (InferenceReadOutcome.FromScratch, new Explainer(built, view, Binding).Explain(graph, subject, predicate, @object));
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
    /// <remarks>The <see cref="Snapshot"/>'s: never waits for a pass, and never matches a check.</remarks>
    public IReadOnlyList<PlacedClash> Clashes() => Snapshot.Clashes;

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
            if (_abandoned)
            {
                // A cancelled pass dropped the closures: the state saved last stays the newest, and the next start loads it.
                return;
            }

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

        if (saved.StoppedAt != long.MaxValue)
        {
            // A binding saved stopped is built again by the restart that reads it,
            // under the limit the restart reads (ADR 0195 point 6): a miss.
            return false;
        }

        using DatasetView view = await _dataset.AsOfAsync(content.Position, cancellationToken).ConfigureAwait(false);
        lock (_applying)
        {
            Begin(InferencePassKind.Load, saved.Position);
            try
            {
                if (!Load(saved, view, cancellationToken))
                {
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled while it built: nothing was taken in, and the projection is as it was, empty at 0.
                _terms.Restore([]);
                throw;
            }
            finally
            {
                Volatile.Write(ref _pass, null);
            }
        }

        Advance(saved.Position);
        return true;
    }

    // The saved state taken in, when the closure the log gives at its position agrees with its open rows.
    private bool Load(Saved saved, DatasetView view, CancellationToken cancellationToken)
    {
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
            Publish(saved.Position);
        }

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

    /// <summary>What the binding's closures hold now, by their budget's count (ADR 0195 point 6): <c>held</c> on readiness.</summary>
    public MemoryAmount ClosureHeld => _state?.Budget.Used ?? new MemoryAmount(0);

    /// <summary>The most they have held at once since they were built, the build included, by the budget's count: <c>peak</c> on readiness, what the binding needs of <c>ClosureBytes</c>.</summary>
    public MemoryAmount ClosurePeak => _state?.Budget.Peak ?? new MemoryAmount(0);

    /// <summary>A budget of <see cref="InferenceOptions.ClosureBytes"/> at the binding's profile's costs (ADR 0195 point 6).</summary>
    internal FactBudget NewBudget() => new(new MemoryAmount(_options.ClosureBytes.Value), InferenceOptions.CostsFor(Binding.Profile), _options.Pool);

    /// <summary>
    /// Applies a commit (ADR 0195 point 4). A
    /// generation is made for these causes only: a commit that changes a
    /// bound TBox graph, and a commit that allocates a term a rule names. A
    /// budget refusal or a memory failure stops the binding at its memory
    /// limit; any other failure stops it as failed. Neither is retried.
    /// </summary>
    private void Apply(IQuadSource view, QuadDelta delta, long position, CancellationToken cancellationToken)
    {
        if (_state is not { } state)
        {
            // Stopped: refused until the binding's owner makes another projection, never tried again here.
            return;
        }

        state.Terms.Source = view;
        Dictionary<TermHandle, HashSet<Fact>>? changed;
        try
        {
            foreach (RdfTerm local in state.Terms.LocalTerms)
            {
                if (view.TryInternalise(local, out _))
                {
                    // A term a rule names is the dataset's now: recompile over it.
                    Generation(view, position, cancellationToken);
                    return;
                }
            }

            changed = state.Apply(view, delta, cancellationToken);
        }
        catch (ClosureLimitExceededException refused)
        {
            Stop(state, position, refused.Wanted, failure: null);
            return;
        }
        catch (OutOfMemoryException)
        {
            // Not the mechanism (the budget is), but if the heap is exhausted on this pass all the same, the binding is refused as at its limit.
            Stop(state, position, new MemoryAmount(_options.ClosureBytes.Value), failure: null);
            return;
        }
        catch (Exception error) when (IsStorageFailure(error))
        {
            // A store read that failed before anything changed: the pass holds off, and
            // the commit is applied again from the projection's position when the store answers.
            throw;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Stop(state, position, new MemoryAmount(0), failure: error);
            return;
        }

        if (changed is null)
        {
            // A TBox commit: a generation over the new TBox closure.
            Generation(view, position, cancellationToken);
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

        Learn(state);
        Publish(position);
    }

    // A generation in a commit's pass: readiness names it as one, and it publishes as the pass's end.
    private void Generation(IQuadSource view, long position, CancellationToken cancellationToken)
    {
        Begin(InferencePassKind.Generation, position);
        Rebuild(view, position, cancellationToken);
        Publish(position);
    }

    /// <summary>
    /// A generation at <paramref name="position"/>: the closures rebuilt from
    /// <paramref name="source"/>, and only the rows that differ opened or
    /// closed. Past the memory limit, the binding stops.
    /// </summary>
    private void Rebuild(IQuadSource source, long position, CancellationToken cancellationToken)
    {
        Materialisation built;
        try
        {
            built = Materialisation.Build(source, _terms, this, cancellationToken);
        }
        catch (ClosureLimitExceededException refused)
        {
            StopAll(position, refused.Wanted, failure: null);
            return;
        }
        catch (OutOfMemoryException)
        {
            StopAll(position, new MemoryAmount(_options.ClosureBytes.Value), failure: null);
            return;
        }
        catch (Exception error) when (IsStorageFailure(error))
        {
            // The build read the store and failed; the closures in use are untouched, and the generation is made again when the store answers.
            throw;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            StopAll(position, new MemoryAmount(0), failure: error);
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
        Interlocked.Increment(ref _generations);
    }

    /// <summary>
    /// The data graphs of a cross-graph binding's premise-set graph (ADR 0190
    /// point 6), the default graph as <c>varve:defaultGraph</c>; null for a
    /// graph that is not one. A caller reads it when it reads every one of
    /// them. A premise-set graph's name is its set's, so the answer never
    /// changes once given.
    /// </summary>
    public IReadOnlyList<RdfTerm>? PremiseSetOf(RdfTerm graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return _premiseSets.TryGetValue(graph, out RdfTerm[]? set) ? set : null;
    }

    /// <summary>The premise-set graphs with open rows at <see cref="Position"/>: <c>premiseSets</c> on readiness counts them.</summary>
    public IReadOnlyList<RdfTerm> PremiseSetGraphs()
    {
        List<RdfTerm> graphs = [];
        lock (_rowsLock)
        {
            foreach (TermHandle graph in _open.Keys)
            {
                if (ProjectionTerms.IsLocal(graph) && _terms.TryExternalise(graph, out RdfTerm? term) && _premiseSets.ContainsKey(term))
                {
                    graphs.Add(term);
                }
            }
        }

        return graphs;
    }

    /// <summary>Records the premise-set graphs a state has minted, while its terms' source can name their data graphs.</summary>
    internal void Learn(Materialisation state)
    {
        foreach ((TermHandle graph, TermHandle[] members) in state.PremiseSetGraphs)
        {
            if (!state.Terms.TryExternalise(graph, out RdfTerm? name) || _premiseSets.ContainsKey(name))
            {
                continue;
            }

            RdfTerm[] set = new RdfTerm[members.Length];
            for (int i = 0; i < set.Length; i++)
            {
                set[i] = members[i].IsNone ? DefaultGraphTerm
                    : state.Terms.TryExternalise(members[i], out RdfTerm? member) ? member
                    : throw new InvalidOperationException("A data graph the projection cannot name.");
            }

            _premiseSets[name] = set;
        }
    }

    private static readonly RdfTerm DefaultGraphTerm = RdfTerm.Iri(Encoding.UTF8.GetBytes(Reserved + "defaultGraph"));

    /// <summary>Stops the binding at <paramref name="position"/>: its closures dropped, every open row closed, and reads refused from there on.</summary>
    private void Stop(Materialisation state, long position, MemoryAmount wanted, Exception? failure)
    {
        state.Release();
        _state = null;
        StopAll(position, wanted, failure);
    }

    private void StopAll(long position, MemoryAmount wanted, Exception? failure)
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

        _reached = new ByteCount(wanted.Value);
        Volatile.Write(ref _failure, failure);
        Volatile.Write(ref _stoppedAt, position);
        Publish(position);
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

/// <summary>How many generations a projection has made, for the tests.</summary>
/// <param name="Value">The count.</param>
internal readonly record struct Generations(int Value);
