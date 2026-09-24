// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>How a <see cref="Dataset"/> is opened.</summary>
public sealed class DatasetOptions
{
    /// <summary>
    /// The clock the sequencer reads. Required: the store never reads the
    /// machine's clock itself, which is what lets two machines fed the same
    /// requests produce the same log bytes (ADR 0011).
    /// </summary>
    public required TimeProvider Clock { get; init; }

    /// <summary>The most body bytes one record carries; a larger commit is several records (ADR 0013).</summary>
    public int MaxRecordBytes { get; init; } = 1 << 20;

    /// <summary>The size past which the active segment is sealed and a new one begun (ADR 0018).</summary>
    public long SegmentBytes { get; init; } = 64L << 20;

    /// <summary>Test seam: throws from the default projection at the positions it returns true for.</summary>
    internal Func<long, bool>? DefaultProjectionFault { get; set; }
}

/// <summary>
/// An event-sourced dataset: an append-only log of commits, a default quad
/// projection kept at the head, and reads at any closed position.
/// </summary>
/// <remarks>
/// <para>
/// **The log is the source of truth** (spec §3). A commit is appended, made
/// durable and closed before anything else happens to it; the default
/// projection is then brought to it before the call returns, so
/// <see cref="Pin"/> directly after <see cref="CommitOutcome.Committed"/>
/// observes the commit (§7, ADR 0016).
/// </para>
/// <para>
/// **One sequencer** takes requests one at a time (ADR 0011). Reads never wait
/// for it: a read captures an immutable version and holds it.
/// </para>
/// </remarks>
public sealed class Dataset : IAsyncDisposable
{
    private readonly IStorage _storage;
    private readonly DatasetOptions _options;
    private readonly TermDictionary _dictionary;
    private readonly LogWriter _writer;
    private readonly SemaphoreSlim _sequencer = new(1, 1);
    private readonly Lock _signalGate = new();
    private readonly Dictionary<Quad, bool> _pending = [];
    private TaskCompletionSource _headAdvanced = NewSignal();
    private volatile State _state;
    private string? _broken;

    private Dataset(IStorage storage, DatasetOptions options, TermDictionary dictionary, LogWriter writer, State state)
    {
        _storage = storage;
        _options = options;
        _dictionary = dictionary;
        _writer = writer;
        _state = state;
    }

    /// <summary>The readable head: the position of the last closed commit.</summary>
    public long Head => _state.Head;

    /// <summary>
    /// True in the failed state: the default projection could not reach the
    /// head. <see cref="Pin"/> throws and commits are unavailable until
    /// <see cref="RebuildDefaultProjectionAsync"/> succeeds (spec §7).
    /// </summary>
    public bool IsFailed => _state.Failed is not null;

    /// <summary>What the storage promises once a commit returns.</summary>
    public Durability Durability => _storage.Log.Durability;

    /// <summary>The settings at the head.</summary>
    public DatasetSettings Settings => _state.SettingsAt(_state.Head);

    /// <summary>The positions that have a checkpoint, ascending.</summary>
    public IReadOnlyList<long> Checkpoints
    {
        get
        {
            Checkpoint[] checkpoints = _state.Checkpoints;
            long[] positions = new long[checkpoints.Length];

            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = checkpoints[i].Position;
            }

            return positions;
        }
    }

    /// <summary>
    /// Opens a dataset: verifies the log (refusing one whose chain does not
    /// verify), ignores an unclosed or torn tail, and builds the default
    /// projection from the newest valid checkpoint and the log after it.
    /// </summary>
    /// <exception cref="LogVerificationException">The log does not verify.</exception>
    public static async ValueTask<Dataset> OpenAsync(IStorage storage, DatasetOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Clock);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxRecordBytes, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.SegmentBytes, 1024L);

        LogScan scan = await LogReader.ScanAsync(storage.Log, cancellationToken).ConfigureAwait(false);
        LogWriter writer = await LogWriter.OpenAsync(storage.Log, scan, options.SegmentBytes, options.MaxRecordBytes, cancellationToken).ConfigureAwait(false);
        TermDictionary dictionary = new();

        CommitInfo[] commits = new CommitInfo[Math.Max(16, scan.Commits.Count)];
        DatasetSettings settings = DatasetSettings.Default;

        for (int i = 0; i < scan.Commits.Count; i++)
        {
            LoggedCommit commit = scan.Commits[i];
            dictionary.Publish(commit.Allocations);
            settings = Fold(settings, commit.Header);
            commits[i] = new CommitInfo(commit, dictionary.CanonicalCount, dictionary.BlankCount, settings);
        }

        long head = scan.Commits.Count;
        State partial = new(head, commits, IndexVersion.Empty, [], null);
        Dataset dataset = new(storage, options, dictionary, writer, partial);

        Checkpoint[] checkpoints = await dataset.LoadCheckpointsAsync(partial, cancellationToken).ConfigureAwait(false);
        Checkpoint? newest = checkpoints.Length > 0 ? checkpoints[^1] : null;
        IndexVersion index = newest is null ? IndexVersion.Empty : IndexVersion.FromCheckpoint(newest.Position, newest.Run);

        for (long p = index.Position + 1; p <= head; p++)
        {
            LoggedCommit commit = scan.Commits[(int)(p - 1)];
            index = index.Apply(commit.Asserted, commit.Retracted, p);
        }

        dataset._state = new State(head, commits, index, checkpoints, null);
        return dataset;
    }

    /// <summary>
    /// Spec T1. Normalises the request against the head, runs its validators
    /// over <c>Overlay(G_head, δ)</c>, and appends, makes durable and closes the
    /// commit — or answers without changing anything.
    /// </summary>
    /// <exception cref="ArgumentException">A handle in the request was not issued by this dataset.</exception>
    public async ValueTask<CommitResult> CommitAsync(CommitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await SequenceAsync(CommitKind.Data, request, request.Metadata, request.ExpectedPosition, [], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Spec T5. Appends a <see cref="CommitKind.Settings"/> commit, which needs
    /// an agent and a cause (ADR 0021).
    /// </summary>
    public async ValueTask<CommitResult> ChangeSettingsAsync(
        SettingsChange change, CommitMetadata metadata, long? expectedPosition = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(metadata);

        if (metadata.Agent.IsNone || metadata.Cause.IsNone)
        {
            throw new ArgumentException("A settings change records who made it and why: give an agent and a cause.", nameof(metadata));
        }

        return await SequenceAsync(CommitKind.Settings, null, metadata, expectedPosition, LogFormat.EncodeSettings(change), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Test seam: an <see cref="CommitKind.Erasure"/> commit, before erasure mode can produce one.</summary>
    internal ValueTask<CommitResult> AppendErasureAsync(ulong keyId, CommitMetadata metadata, CancellationToken cancellationToken = default)
    {
        byte[] payload = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(payload, keyId);
        return SequenceAsync(CommitKind.Erasure, null, metadata, null, payload, cancellationToken);
    }

    /// <summary>
    /// R1: a view of the readable head, stable until disposed whatever commits
    /// follow. An engine snapshot for one operation, not time travel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// **A pinned read lives for one query execution** (ADR 0052). The caller
    /// takes the pin before evaluation begins and disposes it after the last
    /// result has been consumed or the consumer has stopped, whichever comes
    /// first. It is not held across queries, not cached, and not shared
    /// between requests: while it is open the engine cannot release the index
    /// version and checkpoint it reads (ADR 0015, 0041).
    /// </para>
    /// <para>
    /// The evaluator never sees this method. It receives the view as an
    /// <see cref="IQuadSource"/> it does not own, never disposes it, and never
    /// learns that it was a pin (ADR 0005). Disposing the view while results
    /// are still being pulled is a caller error, and the
    /// <see cref="ObjectDisposedException"/> every member then throws is the
    /// honest report of it. The bound on how long a query may run is the
    /// host's: a <see cref="CancellationToken"/> handed to the evaluator, which
    /// the server enforces per request and the embedded caller sets for
    /// itself, if at all.
    /// </para>
    /// </remarks>
    /// <exception cref="DatasetUnavailableException">The dataset is in the failed state.</exception>
    public DatasetView Pin()
    {
        State state = _state;

        if (state.Failed is not null)
        {
            throw new DatasetUnavailableException(state.Failed);
        }

        TermView terms = state.TermsAt(_dictionary, state.Head);
        return new DatasetView(state.Head, new IndexSource(state.Index, terms), terms);
    }

    /// <summary>
    /// R2: a view of any closed position, served as the nearest checkpoint at
    /// or below it with the log tail overlaid. Cost is the log distance, not
    /// the dataset's size.
    /// </summary>
    public async ValueTask<DatasetView> AsOfAsync(long position, CancellationToken cancellationToken = default)
    {
        State state = _state;
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, state.Head);

        Checkpoint? checkpoint = state.CheckpointAtOrBelow(position);
        long from = checkpoint?.Position ?? 0;
        IndexVersion baseIndex = checkpoint is null ? IndexVersion.Empty : IndexVersion.FromCheckpoint(from, checkpoint.Run);
        TermView terms = state.TermsAt(_dictionary, position);
        IQuadSource source = new IndexSource(baseIndex, terms);

        if (position > from)
        {
            QuadDelta tail = await NetAsync(state, from, position, cancellationToken).ConfigureAwait(false);
            source = new QuadOverlay(source, tail);
        }

        return new DatasetView(position, source, terms);
    }

    /// <summary>I5: the view at the greatest position whose timestamp is at or before <paramref name="timestamp"/>.</summary>
    public ValueTask<DatasetView> AsOfTimestampAsync(DateTimeOffset timestamp, CancellationToken cancellationToken = default) =>
        AsOfAsync(PositionAt(timestamp), cancellationToken);

    /// <summary>The greatest position whose timestamp is at or before <paramref name="timestamp"/>; 0 when none is.</summary>
    public long PositionAt(DateTimeOffset timestamp)
    {
        State state = _state;
        long ticks = timestamp.UtcTicks;
        long low = 1;
        long high = state.Head;
        long found = 0;

        while (low <= high)
        {
            long middle = low + ((high - low) / 2);

            if (state.Commits[middle - 1].TimestampTicks <= ticks)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    /// <summary>
    /// R3: <c>(G_to \ G_from, G_from \ G_to)</c>, computed from the log alone.
    /// When <paramref name="from"/> is after <paramref name="to"/> the answer is
    /// the same formula, which is the inverse of the forward diff.
    /// </summary>
    public async ValueTask<QuadDelta> DiffAsync(long from, long to, CancellationToken cancellationToken = default)
    {
        State state = _state;
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfNegative(to);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(from, state.Head);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(to, state.Head);

        return from <= to
            ? await NetAsync(state, from, to, cancellationToken).ConfigureAwait(false)
            : (await NetAsync(state, to, from, cancellationToken).ConfigureAwait(false)).Inverse();
    }

    /// <summary>The settings at a closed position: the fold of the settings commits up to it.</summary>
    public ValueTask<DatasetSettings> SettingsAtAsync(long position, CancellationToken cancellationToken = default)
    {
        State state = _state;
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, state.Head);
        return new ValueTask<DatasetSettings>(state.SettingsAt(position));
    }

    /// <summary>
    /// T2: materialises the state at a closed position as a checkpoint in the
    /// derived store. Derived data: dropping it loses nothing (ADR 0015).
    /// </summary>
    public async ValueTask CheckpointAsync(long position, CancellationToken cancellationToken = default)
    {
        await _sequencer.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            State state = _state;
            ArgumentOutOfRangeException.ThrowIfNegative(position);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(position, state.Head);

            if (position == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(position), "Position 0 is the empty dataset; it needs no checkpoint.");
            }

            List<Quad> quads = [];

            using (DatasetView view = await AsOfAsync(position, cancellationToken).ConfigureAwait(false))
            using (IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any))
            {
                while (cursor.MoveNext())
                {
                    quads.Add(cursor.Current);
                }
            }

            CommitInfo at = state.Commits[position - 1];
            Run run = Run.FromDelta(CollectionsMarshal.AsSpan(quads), []);
            byte[] blob = CheckpointFormat.Encode(position, at.HeaderHash, at.CanonicalCount, at.BlankCount, run, _dictionary);
            string name = Checkpoint.Name(position);

            await _storage.Derived.PutAsync(name, blob, cancellationToken).ConfigureAwait(false);
            ReadOnlyMemory<byte> stored = await _storage.Derived.GetRangeAsync(name, 0, blob.Length, cancellationToken).ConfigureAwait(false);
            Checkpoint checkpoint = CheckpointFormat.TryDecode(stored, _dictionary)
                ?? throw new InvalidOperationException("A checkpoint just written does not read back.");

            _state = _state.WithCheckpoints(Insert(_state.Checkpoints, checkpoint));
        }
        finally
        {
            _sequencer.Release();
        }
    }

    /// <summary>Drops a checkpoint. Nothing is lost: an as-of read at that position is slower, never wrong.</summary>
    public async ValueTask DropCheckpointAsync(long position, CancellationToken cancellationToken = default)
    {
        await _sequencer.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _storage.Derived.DeleteAsync(Checkpoint.Name(position), cancellationToken).ConfigureAwait(false);
            Checkpoint[] kept = Array.FindAll(_state.Checkpoints, c => c.Position != position);
            _state = _state.WithCheckpoints(kept);
        }
        finally
        {
            _sequencer.Release();
        }
    }

    /// <summary>
    /// §8: closed commits after <paramref name="from"/>, in order, at least
    /// once, each with its delta restricted by the filter. A data commit whose
    /// filtered delta is empty is skipped; settings and erasure commits are
    /// always delivered (ADR 0046). The consumer owns its position (ADR 0042).
    /// </summary>
    public async IAsyncEnumerable<Commit> Subscribe(
        long from, SubscriptionFilter filter, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        long next = from + 1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task advanced = CurrentSignal();
            State state = _state;

            while (next <= state.Head)
            {
                Commit? commit = await ReadCommitAsync(state, next, filter, cancellationToken).ConfigureAwait(false);
                next++;

                if (commit is not null)
                {
                    yield return commit;
                }
            }

            await advanced.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>§7: applies every closed commit after the projection's position, unfiltered.</summary>
    public async ValueTask CatchUpAsync(IProjection projection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        State state = _state;

        for (long p = projection.Position + 1; p <= state.Head; p++)
        {
            Commit commit = (await ReadCommitAsync(state, p, SubscriptionFilter.All, cancellationToken).ConfigureAwait(false))!;
            await projection.ApplyAsync(commit, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// §7: drops a projection's state and replays it — from position 0, or
    /// from the newest checkpoint — up to the head.
    /// </summary>
    public async ValueTask RebuildAsync(IProjection projection, bool fromCheckpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);
        Checkpoint? checkpoint = fromCheckpoint ? _state.CheckpointAtOrBelow(_state.Head) : null;

        if (checkpoint is null)
        {
            await projection.ResetAsync(null, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            using DatasetView view = await AsOfAsync(checkpoint.Position, cancellationToken).ConfigureAwait(false);
            await projection.ResetAsync(view, cancellationToken).ConfigureAwait(false);
        }

        await CatchUpAsync(projection, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rebuilds the default projection from the newest checkpoint and the log,
    /// which is how the failed state is left (§7).
    /// </summary>
    public async ValueTask RebuildDefaultProjectionAsync(CancellationToken cancellationToken = default)
    {
        await _sequencer.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            State state = _state;
            Checkpoint? checkpoint = state.CheckpointAtOrBelow(state.Head);
            IndexVersion index = checkpoint is null ? IndexVersion.Empty : IndexVersion.FromCheckpoint(checkpoint.Position, checkpoint.Run);

            for (long p = index.Position + 1; p <= state.Head; p++)
            {
                LoggedCommit commit = await ReadLoggedAsync(state, p, cancellationToken).ConfigureAwait(false);
                ThrowIfFaulted(p);
                index = index.Apply(commit.Asserted, commit.Retracted, p);
            }

            _state = state.WithIndex(index, failed: null);
        }
        finally
        {
            _sequencer.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _sequencer.Dispose();
        return ValueTask.CompletedTask;
    }

    private async ValueTask<CommitResult> SequenceAsync(
        CommitKind kind,
        CommitRequest? request,
        CommitMetadata metadata,
        long? expected,
        byte[] kindPayload,
        CancellationToken cancellationToken)
    {
        await _sequencer.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            State state = _state;
            long head = state.Head;

            if (_broken is not null)
            {
                return CommitResult.Unavailable(head, _broken);
            }

            if (state.Failed is not null)
            {
                return CommitResult.Unavailable(head, state.Failed);
            }

            if (state.Index.Position != head)
            {
                return CommitResult.Unavailable(head, "The default projection is not at the readable head.");
            }

            // Step 1.
            if (expected is long position && position != head)
            {
                return CommitResult.Conflict(head);
            }

            // Step 2.
            TermView terms = state.TermsAt(_dictionary, head);
            Resolver resolver = new(_dictionary, terms.CanonicalCount, terms.BlankCount);
            ulong agent = resolver.Resolve(metadata.Agent);
            ulong cause = resolver.Resolve(metadata.Cause);
            ulong scope = resolver.Resolve(metadata.GraphScope);

            // Step 3: the operations in order over an overlay on the head; the net result.
            _pending.Clear();

            if (request is not null)
            {
                foreach (CommitRequest.Operation operation in request.Operations)
                {
                    Quad quad = new(
                        new TermHandle(resolver.Resolve(operation.Subject)),
                        new TermHandle(resolver.Resolve(operation.Predicate)),
                        new TermHandle(resolver.Resolve(operation.Object)),
                        new TermHandle(resolver.Resolve(operation.Graph)));
                    _pending[quad] = operation.IsAssert;
                }
            }

            List<Quad> asserted = [];
            List<Quad> retracted = [];

            foreach (KeyValuePair<Quad, bool> entry in _pending)
            {
                Quad quad = entry.Key;
                bool present = state.Index.Contains(in quad);

                if (entry.Value && !present)
                {
                    asserted.Add(quad);
                }
                else if (!entry.Value && present)
                {
                    retracted.Add(quad);
                }
            }

            _pending.Clear();

            // Step 4.
            if (kind == CommitKind.Data && asserted.Count == 0 && retracted.Count == 0)
            {
                return CommitResult.NoChange(head);
            }

            HashSet<ulong> referenced = [agent, cause, scope];

            foreach (Quad quad in asserted)
            {
                AddIds(referenced, in quad);
            }

            foreach (Quad quad in retracted)
            {
                AddIds(referenced, in quad);
            }

            Func<ulong, ulong> map = resolver.Finalise(referenced);
            QuadDelta delta = QuadDelta.Create(Remap(asserted, map), Remap(retracted, map));
            agent = map(agent);
            cause = map(cause);
            scope = map(scope);

            // Steps 5 and 6.
            List<ulong> attachments = [];

            if (request is not null && request.Validators.Count > 0)
            {
                QuadOverlay proposed = new(new PendingSource(state.Index, terms, resolver.Allocations), delta);

                foreach (ICommitValidator validator in request.Validators)
                {
                    ValidationVerdict verdict = validator.Validate(proposed, delta);

                    if (!verdict.IsAccepted)
                    {
                        return CommitResult.Rejected(head, verdict.Report);
                    }

                    if (verdict.Attachment is not null)
                    {
                        attachments.Add(resolver.ResolveFinal(verdict.Attachment));
                    }
                }
            }

            // Step 7.
            long next = head + 1;
            long now = _options.Clock.GetUtcNow().UtcTicks;
            long last = head == 0 ? long.MinValue : state.Commits[head - 1].TimestampTicks;
            long timestamp = Math.Max(now, last);

            Allocation[] allocations = [.. resolver.Allocations];
            byte[] body = LogFormat.EncodeBody(allocations, delta.Asserted, delta.Retracted);
            byte[] previous = head == 0 ? LogFormat.Genesis : state.Commits[head - 1].HeaderHash;
            CommitHeader header = new(kind, next, timestamp, agent, cause, scope, [.. attachments], kindPayload, previous, SHA256.HashData(body));
            byte[] headerBytes = LogFormat.EncodeHeader(in header);
            byte[] headerHash = SHA256.HashData(headerBytes);

            CommitLocation location;

            try
            {
                location = await _writer.AppendAsync(kind, next, body, headerBytes, headerHash, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The records may be half written. The log is still readable — an
                // unclosed tail is ignored on open — but this writer's idea of
                // the active segment can no longer be trusted.
                _broken = "An append failed; reopen the dataset to recover.";
                throw;
            }

            // The commit is durable and closed: it stands, whatever follows.
            _dictionary.Publish(allocations);
            DatasetSettings settings = Fold(state.SettingsAt(head), header);
            CommitInfo info = new(header, headerHash, location, _dictionary.CanonicalCount, _dictionary.BlankCount, settings);
            CommitInfo[] commits = Append(state.Commits, head, info);

            IndexVersion index = state.Index;
            string? failed = null;

            try
            {
                ThrowIfFaulted(next);
                index = index.Apply(delta.Asserted, delta.Retracted, next);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // A derived artefact cannot veto a durable fact (§7). The
                // projection is behind, and the dataset says so rather than wait.
                failed = "The default projection failed at position " + next + " and must be rebuilt: " + error.Message;
            }

            _state = new State(next, commits, index, state.Checkpoints, failed);
            Signal();
            return CommitResult.Committed(next);
        }
        finally
        {
            _sequencer.Release();
        }
    }

    private void ThrowIfFaulted(long position)
    {
        if (_options.DefaultProjectionFault?.Invoke(position) == true)
        {
            throw new InvalidOperationException("Injected default projection fault at position " + position + ".");
        }
    }

    private static void AddIds(HashSet<ulong> ids, in Quad quad)
    {
        ids.Add(quad.Subject.Value);
        ids.Add(quad.Predicate.Value);
        ids.Add(quad.Object.Value);
        ids.Add(quad.Graph.Value);
    }

    private static Quad[] Remap(List<Quad> quads, Func<ulong, ulong> map)
    {
        Quad[] result = new Quad[quads.Count];

        for (int i = 0; i < result.Length; i++)
        {
            Quad q = quads[i];
            result[i] = new Quad(
                new TermHandle(map(q.Subject.Value)),
                new TermHandle(map(q.Predicate.Value)),
                new TermHandle(map(q.Object.Value)),
                new TermHandle(map(q.Graph.Value)));
        }

        return result;
    }

    private static CommitInfo[] Append(CommitInfo[] commits, long head, CommitInfo info)
    {
        CommitInfo[] target = commits;

        if (head >= commits.Length)
        {
            // Readers holding the old array still see every commit it had.
            target = new CommitInfo[commits.Length * 2];
            Array.Copy(commits, target, head);
        }

        target[head] = info;
        return target;
    }

    private static DatasetSettings Fold(DatasetSettings before, in CommitHeader header) =>
        header.Kind == CommitKind.Settings ? LogFormat.ApplySettings(before, header.KindPayload, header.Position) : before;

    private static Checkpoint[] Insert(Checkpoint[] checkpoints, Checkpoint checkpoint)
    {
        List<Checkpoint> list = [.. Array.FindAll(checkpoints, c => c.Position != checkpoint.Position), checkpoint];
        list.Sort((a, b) => a.Position.CompareTo(b.Position));
        return [.. list];
    }

    private async ValueTask<Checkpoint[]> LoadCheckpointsAsync(State state, CancellationToken cancellationToken)
    {
        List<Checkpoint> loaded = [];

        foreach (string name in await _storage.Derived.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!name.StartsWith(Checkpoint.Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            ReadOnlyMemory<byte> blob = await _storage.Derived.GetRangeAsync(name, 0, int.MaxValue, cancellationToken).ConfigureAwait(false);
            Checkpoint? checkpoint = CheckpointFormat.TryDecode(blob, _dictionary);

            // A checkpoint names the commit it materialises. One copied beside a
            // different log is a cache miss, never a wrong answer (ADR 0041).
            if (checkpoint is null
                || checkpoint.Position < 1
                || checkpoint.Position > state.Head
                || !checkpoint.HeaderHash.AsSpan().SequenceEqual(state.Commits[checkpoint.Position - 1].HeaderHash)
                || checkpoint.CanonicalCount != state.Commits[checkpoint.Position - 1].CanonicalCount
                || checkpoint.BlankCount != state.Commits[checkpoint.Position - 1].BlankCount)
            {
                continue;
            }

            loaded.Add(checkpoint);
        }

        loaded.Sort((a, b) => a.Position.CompareTo(b.Position));
        return [.. loaded];
    }

    private async ValueTask<QuadDelta> NetAsync(State state, long from, long to, CancellationToken cancellationToken)
    {
        DeltaChain chain = new();

        for (long p = from + 1; p <= to; p++)
        {
            LoggedCommit commit = await ReadLoggedAsync(state, p, cancellationToken).ConfigureAwait(false);
            chain.Add(commit.Asserted, commit.Retracted);
        }

        return chain.ToDelta();
    }

    private ValueTask<LoggedCommit> ReadLoggedAsync(State state, long position, CancellationToken cancellationToken)
    {
        CommitInfo info = state.Commits[position - 1];
        byte[] previous = position == 1 ? LogFormat.Genesis : state.Commits[position - 2].HeaderHash;
        return LogReader.ReadAsync(_storage.Log, info.Location, position, previous, cancellationToken);
    }

    private async ValueTask<Commit?> ReadCommitAsync(State state, long position, SubscriptionFilter filter, CancellationToken cancellationToken)
    {
        LoggedCommit logged = await ReadLoggedAsync(state, position, cancellationToken).ConfigureAwait(false);
        CommitHeader header = logged.Header;
        List<Quad> asserted = [];
        List<Quad> retracted = [];

        foreach (Quad quad in logged.Asserted)
        {
            if (filter.Matches(in quad))
            {
                asserted.Add(quad);
            }
        }

        foreach (Quad quad in logged.Retracted)
        {
            if (filter.Matches(in quad))
            {
                retracted.Add(quad);
            }
        }

        if (header.Kind == CommitKind.Data && asserted.Count == 0 && retracted.Count == 0)
        {
            return null;
        }

        HashSet<ulong> mentioned = [.. header.MetadataIds()];

        foreach (Quad quad in asserted)
        {
            AddIds(mentioned, in quad);
        }

        foreach (Quad quad in retracted)
        {
            AddIds(mentioned, in quad);
        }

        List<TermAllocation> allocations = [];

        for (int i = logged.Allocations.Length - 1; i >= 0; i--)
        {
            Allocation allocation = logged.Allocations[i];

            if (!mentioned.Contains(allocation.Id))
            {
                continue;
            }

            if (allocation.IsTriple)
            {
                mentioned.Add(allocation.Subject);
                mentioned.Add(allocation.Predicate);
                mentioned.Add(allocation.Object);
            }

            allocations.Add(new TermAllocation(new TermHandle(allocation.Id), _dictionary.Term(allocation.Id)));
        }

        allocations.Reverse();
        TermHandle[] attachments = new TermHandle[header.Attachments.Length];

        for (int i = 0; i < attachments.Length; i++)
        {
            attachments[i] = new TermHandle(header.Attachments[i]);
        }

        return new Commit(
            position,
            header.Kind,
            new DateTimeOffset(header.TimestampTicks, TimeSpan.Zero),
            new TermHandle(header.Agent),
            new TermHandle(header.Cause),
            new TermHandle(header.GraphScope),
            attachments,
            QuadDelta.Create(CollectionsMarshal.AsSpan(asserted), CollectionsMarshal.AsSpan(retracted)),
            allocations.ToArray());
    }

    private Task CurrentSignal()
    {
        lock (_signalGate)
        {
            return _headAdvanced.Task;
        }
    }

    private void Signal()
    {
        TaskCompletionSource advanced;

        lock (_signalGate)
        {
            advanced = _headAdvanced;
            _headAdvanced = NewSignal();
        }

        advanced.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>What the store keeps per closed commit, beside the log.</summary>
    private sealed class CommitInfo
    {
        internal CommitInfo(LoggedCommit commit, long canonicalCount, long blankCount, DatasetSettings settings)
            : this(commit.Header, commit.HeaderHash, commit.Location, canonicalCount, blankCount, settings)
        {
        }

        internal CommitInfo(CommitHeader header, byte[] headerHash, CommitLocation location, long canonicalCount, long blankCount, DatasetSettings settings)
        {
            TimestampTicks = header.TimestampTicks;
            HeaderHash = headerHash;
            Location = location;
            CanonicalCount = canonicalCount;
            BlankCount = blankCount;
            Settings = settings;
        }

        internal long TimestampTicks { get; }

        internal byte[] HeaderHash { get; }

        internal CommitLocation Location { get; }

        internal long CanonicalCount { get; }

        internal long BlankCount { get; }

        internal DatasetSettings Settings { get; }
    }

    /// <summary>Everything a read needs, published as one reference.</summary>
    private sealed class State
    {
        internal State(long head, CommitInfo[] commits, IndexVersion index, Checkpoint[] checkpoints, string? failed)
        {
            Head = head;
            Commits = commits;
            Index = index;
            Checkpoints = checkpoints;
            Failed = failed;
        }

        internal long Head { get; }

        internal CommitInfo[] Commits { get; }

        internal IndexVersion Index { get; }

        internal Checkpoint[] Checkpoints { get; }

        internal string? Failed { get; }

        internal TermView TermsAt(TermDictionary dictionary, long position) =>
            position == 0
                ? new TermView(dictionary, 0, 0)
                : new TermView(dictionary, Commits[position - 1].CanonicalCount, Commits[position - 1].BlankCount);

        internal DatasetSettings SettingsAt(long position) =>
            position == 0 ? DatasetSettings.Default : Commits[position - 1].Settings;

        internal Checkpoint? CheckpointAtOrBelow(long position)
        {
            for (int i = Checkpoints.Length - 1; i >= 0; i--)
            {
                if (Checkpoints[i].Position <= position)
                {
                    return Checkpoints[i];
                }
            }

            return null;
        }

        internal State WithCheckpoints(Checkpoint[] checkpoints) => new(Head, Commits, Index, checkpoints, Failed);

        internal State WithIndex(IndexVersion index, string? failed) => new(Head, Commits, index, Checkpoints, failed);
    }
}
