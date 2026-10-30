// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Classification.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Classification;

/// <summary>
/// The classification projection of one binding (ADR 0233): each classified
/// node's subject set with a validity interval, maintained commit by commit
/// from exact deltas, so that the classification at any position from its
/// floor to its position is served without reading the log, and below its
/// floor is classified from scratch over the as-of state.
/// </summary>
/// <remarks>
/// Keyed by the store's term ids, which are stable for a dataset's life
/// because its dictionary is append-only (spec §8). That holds with erasure
/// mode off; erasure mode (#203) revisits the key for private terms.
/// </remarks>
[Contract(typeof(TheClassificationProjection.OneClassificationProjectionPerBinding), Role = "the classification at every position")]
public sealed class ClassificationProjection : IProjection
{
    private readonly Dataset _dataset;
    private readonly Lock _lock = new();
    private readonly ConcurrentDictionary<TermHandle, Interval[]> _rows = new();
    private readonly Dictionary<long, TermHandle[]> _changed = [];
    private readonly List<Generation> _generations = [];
    private SetTable _sets = new();
    private long _position;
    private long _floor;
    private TaskCompletionSource _advanced = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The projection of <paramref name="binding"/> over <paramref name="dataset"/>, empty at position 0 until it is fed.</summary>
    public ClassificationProjection(Dataset dataset, ClassifierBinding binding)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(binding);
        _dataset = dataset;
        Binding = binding;
        _generations.Add(new Generation(0, null));
    }

    /// <summary>The binding projected.</summary>
    public ClassifierBinding Binding { get; }

    /// <inheritdoc />
    public Position Position => new(Volatile.Read(ref _position));

    /// <summary>The lowest position held: 0, or where a reset from a view put it. Below it, a classification is computed from scratch.</summary>
    public Position Floor => new(Volatile.Read(ref _floor));

    /// <summary>Why the shapes graph at the projection's position does not classify; empty when it does.</summary>
    public IReadOnlyList<ClassifierProblem> Problems
    {
        get
        {
            lock (_lock)
            {
                return _generations[^1].Annotations?.Problems ?? [];
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask ApplyAsync(Commit commit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);
        long position = commit.Position.Value;

        if (position <= Volatile.Read(ref _position))
        {
            return;
        }

        // A projection that keeps up reads its commit at the head; one catching up reads it as of.
        DatasetView view = _dataset.Pin();

        if (view.Position != commit.Position)
        {
            view.Dispose();
            view = await _dataset.AsOfAsync(commit.Position, cancellationToken).ConfigureAwait(false);
        }

        using (view)
        {
            lock (_lock)
            {
                Apply(view, commit.Delta, position);
            }
        }

        Advance(position);
    }

    /// <inheritdoc />
    public ValueTask ResetAsync(DatasetView? checkpoint, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _rows.Clear();
            _changed.Clear();
            _generations.Clear();
            _sets = new SetTable();

            if (checkpoint is null)
            {
                _generations.Add(new Generation(0, null));
                Volatile.Write(ref _floor, 0);
                Volatile.Write(ref _position, 0);
                return ValueTask.CompletedTask;
            }

            long position = checkpoint.Position.Value;
            Full(checkpoint, position);
            Volatile.Write(ref _floor, position);
            Volatile.Write(ref _position, position);
        }

        Advance(Volatile.Read(ref _position));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// The classification at <paramref name="position"/> in <paramref name="source"/>'s
    /// handles, <paramref name="source"/> being the state there: false when the
    /// projection has not reached the position. Below the floor it is
    /// classified from scratch over <paramref name="source"/> (ADR 0233 point 5).
    /// </summary>
    public bool TryAt(Position position, IQuadSource source, out IRowClassification classification)
    {
        ArgumentNullException.ThrowIfNull(source);
        long at = position.Value;

        if (at > Volatile.Read(ref _position))
        {
            classification = null!;
            return false;
        }

        if (at < Volatile.Read(ref _floor))
        {
            classification = ShapeClassifier.Scratch(source, Binding);
            return true;
        }

        Generation generation;
        SetTable sets;
        lock (_lock)
        {
            generation = _generations.Last(g => g.Start <= at);
            sets = _sets;
        }

        Annotations annotations = generation.Annotations ?? Annotations.Read(source, Binding);
        INodeSets nodes = annotations.Problems.Count > 0 ? new FailedSets(sets) : new AtPosition(_rows, at);
        classification = new RowClassification(source, Binding, nodes, sets, new Engine(source, Binding, annotations).Public);
        return true;
    }

    /// <summary>Waits until the projection reaches <paramref name="position"/>, or until <paramref name="cancellationToken"/> ends the wait: whether it reached it.</summary>
    public async ValueTask<bool> WaitForAsync(Position position, CancellationToken cancellationToken)
    {
        while (Volatile.Read(ref _position) < position.Value)
        {
            Task advanced = Volatile.Read(ref _advanced).Task;

            if (Volatile.Read(ref _position) >= position.Value)
            {
                break;
            }

            try
            {
                await advanced.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The nodes whose subject set changed at <paramref name="position"/>, as store handles: what the feed adds to a commit's own delta (ADR 0237 point 4).</summary>
    public IReadOnlyList<TermHandle> ChangedAt(Position position)
    {
        lock (_lock)
        {
            return _changed.TryGetValue(position.Value, out TermHandle[]? nodes) ? nodes : [];
        }
    }

    /// <summary>Writes the state to the local cache's <paramref name="store"/> (ADR 0233 point 6), deleting older states of the binding.</summary>
    public async ValueTask SaveAsync(IDerivedStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        byte[] bytes;
        long position;

        lock (_lock)
        {
            position = _position;
            bytes = ClassificationSnapshot.Write(Binding, _position, _floor, _generations, _sets, _rows, _changed);
        }

        BlobName name = new(ClassificationSnapshot.Name(Binding, position));
        await using (IBlobWriter writer = await store.CreateAsync(name, cancellationToken).ConfigureAwait(false))
        {
            await writer.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await writer.PublishAsync(cancellationToken).ConfigureAwait(false);
        }

        string directory = ClassificationSnapshot.Directory(Binding);
        foreach (BlobName existing in await store.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (existing.Value.StartsWith(directory, StringComparison.Ordinal) && existing != name)
            {
                await store.DeleteAsync(existing, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Reads the newest state <see cref="SaveAsync"/> wrote for this binding:
    /// whether one was read. A state that does not verify in full, names
    /// another binding or format, or lies above the head is a miss: the
    /// projection stays as it was, to be rebuilt (ADR 0233 point 6).
    /// </summary>
    public async ValueTask<bool> LoadAsync(IDerivedStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        string directory = ClassificationSnapshot.Directory(Binding);
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

        byte[] bytes;
        using (IReadableBlob blob = await store.OpenAsync(name, cancellationToken).ConfigureAwait(false))
        {
            bytes = new byte[checked((int)blob.Length.Value)];

            if (blob.Read(new ByteOffset(0), bytes) != bytes.Length)
            {
                return false;
            }
        }

        if (!ClassificationSnapshot.TryRead(bytes, Binding, out ClassificationSnapshot.State state) || state.Position > _dataset.Head.Value)
        {
            return false;
        }

        lock (_lock)
        {
            _rows.Clear();
            _changed.Clear();
            _generations.Clear();
            _generations.AddRange(state.Generations);
            _sets = state.Sets;

            foreach ((TermHandle node, Interval[] intervals) in state.Rows)
            {
                _rows[node] = intervals;
            }

            foreach ((long at, TermHandle[] nodes) in state.Changed)
            {
                _changed[at] = nodes;
            }

            Volatile.Write(ref _floor, state.Floor);
            Volatile.Write(ref _position, state.Position);
        }

        Advance(state.Position);
        return true;
    }

    // A data commit reclassifies the nodes its delta changed and everything
    // reaching them by links in either state (ADR 0232 point 5); a commit to
    // the shapes graph, or a configuration that did not load, is a generation.
    private void Apply(IQuadSource after, QuadDelta delta, long position)
    {
        Generation current = _generations[^1];
        Engine probe = new(after, Binding, current.Annotations ?? Annotations.Read(after, Binding));

        if (current.Annotations is null || current.Annotations.Problems.Count > 0 || Touches(probe, delta))
        {
            Full(after, position);
            return;
        }

        Engine post = probe;
        Engine pre = new(new QuadOverlay(after, delta.Inverse()), Binding, current.Annotations);
        HashSet<TermHandle> affected = [];

        foreach (Quad quad in delta.Asserted)
        {
            if (post.Reclassifies(in quad))
            {
                post.Reverse(quad.Subject, affected);
            }
        }

        foreach (Quad quad in delta.Retracted)
        {
            if (pre.Reclassifies(in quad))
            {
                pre.Reverse(quad.Subject, affected);
                post.Reverse(quad.Subject, affected);
            }
        }

        // An assertion's subject may have been reached before, too.
        foreach (TermHandle node in affected.ToArray())
        {
            pre.Reverse(node, affected);
        }

        List<TermHandle> changed = [];

        foreach (TermHandle node in affected)
        {
            if (Set(node, _sets.Intern(post.SubjectsOf(node)), position))
            {
                changed.Add(node);
            }
        }

        Record(position, changed);
    }

    // A generation: classification from scratch, replacing every open row.
    private void Full(IQuadSource state, long position)
    {
        Annotations annotations = Annotations.Read(state, Binding);
        _generations.Add(new Generation(position, annotations));
        Dictionary<TermHandle, TermHandle[]> scratch = annotations.Problems.Count > 0 ? [] : new Engine(state, Binding, annotations).Scratch();
        List<TermHandle> changed = [];

        foreach (TermHandle node in _rows.Keys.Union(scratch.Keys).ToArray())
        {
            SubjectSetId set = scratch.TryGetValue(node, out TermHandle[]? subjects) ? _sets.Intern(subjects) : SubjectSetId.None;

            if (Set(node, set, position))
            {
                changed.Add(node);
            }
        }

        Record(position, changed);
    }

    private static bool Touches(Engine engine, QuadDelta delta)
    {
        foreach (Quad quad in delta.Asserted)
        {
            if (engine.InShapesGraph(in quad))
            {
                return true;
            }
        }

        foreach (Quad quad in delta.Retracted)
        {
            if (engine.InShapesGraph(in quad))
            {
                return true;
            }
        }

        return false;
    }

    // Closes the node's open row at the position and opens one for its new
    // set: whether the set changed. An array is replaced whole, so a reader
    // at an earlier position sees either version and the same answer.
    private bool Set(TermHandle node, SubjectSetId set, long position)
    {
        Interval[] intervals = _rows.TryGetValue(node, out Interval[]? existing) ? existing : [];
        SubjectSetId open = intervals.Length > 0 && intervals[^1].End == long.MaxValue ? intervals[^1].Set : SubjectSetId.None;

        if (open == set)
        {
            return false;
        }

        List<Interval> next = [.. intervals];

        if (!open.IsNone)
        {
            next[^1] = next[^1] with { End = position };
        }

        if (!set.IsNone)
        {
            next.Add(new Interval(set, position, long.MaxValue));
        }

        _rows[node] = [.. next];
        return true;
    }

    private void Record(long position, List<TermHandle> changed)
    {
        if (changed.Count > 0)
        {
            _changed[position] = [.. changed];
        }
    }

    private void Advance(long position)
    {
        Volatile.Write(ref _position, Math.Max(Volatile.Read(ref _position), position));
        Interlocked.Exchange(ref _advanced, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }

    /// <summary>A node's set over <c>[Start, End)</c>; an open row ends at <see cref="long.MaxValue"/>.</summary>
    internal readonly record struct Interval(SubjectSetId Set, long Start, long End);

    /// <summary>A generation: where the configuration was read, and what it said; <see langword="null"/> before any.</summary>
    internal sealed record Generation(long Start, Annotations? Annotations);

    // The rows valid at one position.
    private sealed class AtPosition(ConcurrentDictionary<TermHandle, Interval[]> rows, long position) : INodeSets
    {
        [DesignDecision(typeof(TheClassificationProjection.ClassificationLookupsMemoisedPerHandle), Scope = ExceptionScope.HotPath)]
        public SubjectSetId Of(TermHandle node)
        {
            if (!rows.TryGetValue(node, out Interval[]? intervals))
            {
                return SubjectSetId.None;
            }

            for (int i = intervals.Length - 1; i >= 0; i--)
            {
                Interval interval = intervals[i];

                if (interval.Start <= position)
                {
                    return position < interval.End ? interval.Set : SubjectSetId.None;
                }
            }

            return SubjectSetId.None;
        }
    }
}
