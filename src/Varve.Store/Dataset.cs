// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Store.Log;

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
    public ByteCount MaxRecordBytes { get; init; } = new(1 << 20);

    /// <summary>The size past which the active segment is sealed and a new one begun (ADR 0018).</summary>
    public ByteCount SegmentBytes { get; init; } = new(64L << 20);

    /// <summary>
    /// Pre-commit validators bound to the dataset: run on every <c>Data</c>
    /// commit, in order, before the request's own (ADR 0058). Each sees the
    /// same pair as a request validator — the proposed state and the delta —
    /// and may reject; every accepting validator's attachment is recorded.
    /// </summary>
    public IReadOnlyList<ICommitValidator> Validators { get; init; } = [];

    /// <summary>
    /// How many quads, asserted and retracted, the default projection holds in
    /// memory before maintenance writes them to <c>derived/</c> as a run
    /// (ADR 0070). Below it, opening replays them from the log.
    /// </summary>
    public QuadCount MemtableLimit { get; init; } = new(1_000_000);

    /// <summary>
    /// How many of the newest commits' entries the commit index holds in
    /// memory (ADR 0089): between this and twice this, once there are that
    /// many; maintenance writes the rest to <c>derived/</c>. 4,096 by default:
    /// 320 KiB to 640 KiB of entries, whatever the length of the log; enough
    /// that what reads near the head — the next commit, a subscriber keeping up,
    /// as-of reads within the soak's checkpoint interval of 1,000 — reads memory,
    /// and few enough that a blob of the oldest half is written once in every
    /// 4,096 commits.
    /// </summary>
    public int CommitCache { get; init; } = 4096;

    /// <summary>
    /// Whether the dataset runs its own maintenance — memtable flushes and
    /// disk merges — on a task it owns, off the sequencer (ADR 0070, ADR 0042's
    /// amendment). <see cref="MaintenanceMode.Background"/> by default, except
    /// in a browser, where it is <see cref="MaintenanceMode.Off"/> until 6c.
    /// With it off, <see cref="Dataset.MaintainAsync"/> runs it when the caller
    /// asks.
    /// </summary>
    public MaintenanceMode Maintenance { get; init; } = OperatingSystem.IsBrowser() ? MaintenanceMode.Off : MaintenanceMode.Background;

    /// <summary>
    /// When the dataset writes checkpoints on its own, as maintenance
    /// (ADR 0078). <see cref="CheckpointPolicy.Never"/> by default.
    /// </summary>
    public CheckpointPolicy Checkpoints { get; init; } = CheckpointPolicy.Never;

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
public sealed partial class Dataset : IAsyncDisposable
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
    private volatile bool _disposing;

    private Dataset(IStorage storage, DatasetId id, DatasetOptions options, TermDictionary dictionary, LogWriter writer, State state)
    {
        _storage = storage;
        Id = id;
        _options = options;
        _dictionary = dictionary;
        _writer = writer;
        _state = state;
    }

    /// <summary>The dataset's id, as its manifest records it (ADR 0072).</summary>
    public DatasetId Id { get; }

    /// <summary>The readable head: the position of the last closed commit.</summary>
    public Position Head => new(_state.Head);

    /// <summary>
    /// True in the failed state: the default projection could not reach the
    /// head. <see cref="Pin"/> throws and commits are unavailable until
    /// <see cref="RebuildDefaultProjectionAsync"/> succeeds (spec §7).
    /// </summary>
    public bool IsFailed => _state.Failed is not null;

    /// <summary>
    /// The position the default projection has applied (spec §7): the head,
    /// since a commit's records and its projection are one step of the
    /// sequencer, except where the projection failed and the head went on
    /// to be refused. The admin API reports it beside the head (ADR 0105).
    /// </summary>
    public Position ProjectionPosition => new(_state.Index.Position);

    /// <summary>What the storage promises once a commit returns.</summary>
    public Durability Durability => _storage.Log.Durability;

    /// <summary>The settings at the head.</summary>
    public DatasetSettings Settings => _state.SettingsAt(_state.Head);

    /// <summary>Test seam: the dictionary's counters at the head.</summary>
    internal (long Canonical, long Blank) CountersForTests() => (_state.CanonicalAt(_state.Head), _state.BlankAt(_state.Head));

    /// <summary>Test seam: the clock the dataset was opened with.</summary>
    internal TimeProvider ClockForTests() => _options.Clock;

    /// <summary>Test seam: the commit index of the current state (ADR 0089).</summary>
    internal CommitIndex CommitsForTests() => _state.Commits;

    /// <summary>Test seam: the storage the dataset was opened on.</summary>
    internal IStorage StorageForTests() => _storage;

    /// <summary>Test seam: the bytes of the log's records after <paramref name="from"/> up to <paramref name="to"/>.</summary>
    internal long LogBytesBetween(long from, long to) => _state.LogBytesAt(to) - _state.LogBytesAt(from);

    /// <summary>The positions that have a checkpoint, ascending.</summary>
    public IReadOnlyList<Position> Checkpoints
    {
        get
        {
            Checkpoint[] checkpoints = _state.Checkpoints;
            Position[] positions = new Position[checkpoints.Length];

            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = new Position(checkpoints[i].Position);
            }

            return positions;
        }
    }

    /// <summary>
    /// Creates a dataset in empty storage: writes its manifest, with the id
    /// the caller gives (ADR 0072), and opens it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The storage already holds a dataset, or part of one.</exception>
    public static async ValueTask<Dataset> CreateAsync(IStorage storage, DatasetId id, DatasetOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ValidateOptions(options);

        if (!(await storage.Log.ReadManifestAsync(cancellationToken).ConfigureAwait(false)).IsEmpty
            || (await storage.Log.ListSegmentsAsync(cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            throw new InvalidOperationException("The storage already holds a dataset; open it with Dataset.OpenAsync.");
        }

        await storage.Log.WriteManifestAsync(LogFormat.EncodeManifest(id), cancellationToken).ConfigureAwait(false);
        return await OpenAsync(storage, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a dataset: checks its manifest, verifies the log's chain (refusing
    /// one that does not verify), ignores a torn or unclosed tail, and builds
    /// the dictionary and the default projection from the newest valid
    /// checkpoint and the log after it (ADR 0072).
    /// </summary>
    /// <remarks>
    /// Every record and commit header is read; bodies are read only after the
    /// newest checkpoint that names this log, so opening costs the log since
    /// that checkpoint.
    /// </remarks>
    /// <exception cref="LogVerificationException">The log does not verify.</exception>
    /// <exception cref="UnsupportedFormatException">The log is of a format this build does not read.</exception>
    /// <exception cref="InvalidOperationException">The storage holds no dataset.</exception>
    public static async ValueTask<Dataset> OpenAsync(IStorage storage, DatasetOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ValidateOptions(options);

        ReadOnlyMemory<byte> manifest = await storage.Log.ReadManifestAsync(cancellationToken).ConfigureAwait(false);

        if (manifest.IsEmpty)
        {
            if ((await storage.Log.ListSegmentsAsync(cancellationToken).ConfigureAwait(false)).Count == 0)
            {
                throw new InvalidOperationException("The storage holds no dataset; create one with Dataset.CreateAsync.");
            }

            throw new LogVerificationException(0, "The log has segments and no manifest.");
        }

        DatasetId id = LogFormat.DecodeManifest(manifest.Span);
        long[] candidates = await CheckpointPositionsAsync(storage.Derived, cancellationToken).ConfigureAwait(false);

        // Bodies are read only after what derived/ claims to hold: the
        // projection's persisted state, or the newest checkpoint. A claim that
        // turns out stale reads the bodies it needs later, one by one.
        var claimed = await DerivedFormat.TryReadStateAsync(storage.Derived, StateName, id, cancellationToken).ConfigureAwait(false);
        long bodiesAfter = Math.Max(candidates.Length > 0 ? candidates[0] : 0, claimed?.Header.To ?? 0);

        // The commit index is read against the log as the scan passes it:
        // nothing the size of the log is held (ADR 0089). Its versions read
        // through to the dataset's current one once the dataset exists.
        Dataset? owner = null;
        Func<CommitIndex> current = () => owner!._state.Commits;
        CommitIndexOpener opener = await CommitIndexOpener.StartAsync(storage.Derived, id, options.CommitCache, current, cancellationToken).ConfigureAwait(false);
        LogScan scan = await LogReader.ScanAsync(
            storage.Log, id, bodiesAfter, commit => opener.AddAsync(commit, bodiesAfter, cancellationToken), cancellationToken).ConfigureAwait(false);
        long head = scan.Head;
        CommitIndex commits = await opener.FinishAsync(cancellationToken).ConfigureAwait(false);

        if (commits.Head != head)
        {
            throw new InvalidOperationException("The commit index was not brought to the head.");
        }

        LogWriter writer = await LogWriter.OpenAsync(storage.Log, id, scan, options.SegmentBytes.Value, (int)options.MaxRecordBytes.Value, cancellationToken).ConfigureAwait(false);
        TermDictionary dictionary = new();
        State partial = new(head, commits, IndexVersion.Empty, [], null);
        Dataset dataset = new(storage, id, options, dictionary, writer, partial);
        owner = dataset;
        dataset._commitSequence = opener.Sequence;

        Checkpoint[] checkpoints = await dataset.LoadCheckpointsAsync(partial, candidates, cancellationToken).ConfigureAwait(false);

        // The persisted projection, when it names this log; otherwise the
        // newest checkpoint, or nothing (ADR 0070). Its runs carry the
        // dictionary up to its position (ADR 0079), so only the log after it
        // is read: allocations and quads alike.
        IndexVersion index = await dataset.LoadIndexAsync(partial, checkpoints, cancellationToken).ConfigureAwait(false);

        for (long p = index.Position + 1; p <= head; p++)
        {
            LoggedCommit commit = opener.Bodies.TryGetValue(p, out LoggedCommit? read)
                ? read
                : await dataset.ReadLoggedAsync(partial, p, cancellationToken).ConfigureAwait(false);
            index = index.Apply(commit.Asserted, commit.Retracted, partial.TermsOf(commit, p), p);
        }

        if (index.Position != head)
        {
            throw new InvalidOperationException("The default projection was not brought to the head.");
        }

        dataset._state = new State(head, commits, index, checkpoints, null);
        return dataset;
    }

    private static void ValidateOptions(DatasetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Clock);
        ArgumentNullException.ThrowIfNull(options.Validators);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxRecordBytes.Value, 64L, nameof(options));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxRecordBytes.Value, int.MaxValue, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.SegmentBytes.Value, 1024L, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.CommitCache, 1, nameof(options));
        ArgumentNullException.ThrowIfNull(options.Checkpoints);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Checkpoints.EveryCommits ?? 1, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Checkpoints.EveryLogBytes?.Value ?? 1, 1L, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegative(options.Checkpoints.Keep, nameof(options));
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
        return await SequenceAsync(CommitKind.Data, request, request.Metadata, request.ExpectedPosition?.Value, [], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Spec T5. Appends a <see cref="CommitKind.Settings"/> commit, which needs
    /// an agent and a cause (ADR 0021).
    /// </summary>
    public async ValueTask<CommitResult> ChangeSettingsAsync(
        SettingsChange change, CommitMetadata metadata, Position? expectedPosition = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(metadata);

        if (metadata.Agent.IsNone || metadata.Cause.IsNone)
        {
            throw new ArgumentException("A settings change records who made it and why: give an agent and a cause.", nameof(metadata));
        }

        return await SequenceAsync(CommitKind.Settings, null, metadata, expectedPosition?.Value, LogFormat.EncodeSettings(change), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Test seam: an <see cref="CommitKind.Erasure"/> commit, before erasure mode can produce one.</summary>
    internal ValueTask<CommitResult> AppendErasureAsync(ulong keyId, CommitMetadata metadata, CancellationToken cancellationToken = default)
    {
        byte[] payload = new byte[LogFormat.KeyIdLength];
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

        // The version's blobs are held open until the view is disposed; one a
        // merge has just closed sends us back for the current version.
        while (!state.Index.TryAcquire())
        {
            state = _state;
        }

        IndexVersion held = state.Index;
        TermView terms = state.TermsAt(_dictionary, held.Runs, state.Head);
        return new DatasetView(state.Head, new IndexSource(held, terms), terms, held.Release);
    }

    /// <summary>
    /// R2: a view of any closed position, served as the nearest checkpoint at
    /// or below it with the log tail overlaid. Cost is the log distance, not
    /// the dataset's size.
    /// </summary>
    public ValueTask<DatasetView> AsOfAsync(Position position, CancellationToken cancellationToken = default) =>
        AsOfCoreAsync(position.Value, cancellationToken);

    private async ValueTask<DatasetView> AsOfCoreAsync(long position, CancellationToken cancellationToken)
    {
        State state = _state;
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, state.Head);

        Checkpoint? checkpoint = state.CheckpointAtOrBelow(position);

        // A checkpoint dropped since the state was read is no longer open.
        while (checkpoint is not null && !checkpoint.Run.Blob!.TryAcquire())
        {
            state = _state;
            checkpoint = state.CheckpointAtOrBelow(position);
        }

        try
        {
            long from = checkpoint?.Position ?? 0;
            IndexVersion baseIndex = checkpoint is null ? IndexVersion.Empty : IndexVersion.FromBase(from, checkpoint.Run);
            (QuadDelta tail, TermSection tailTerms) = await NetAsync(state, from, position, cancellationToken).ConfigureAwait(false);

            // The checkpoint's dictionary, and the entries the tail allocated.
            Run tailRun = Run.FromDelta([], [], tailTerms, from, position);
            TermView terms = state.TermsAt(_dictionary, checkpoint is null ? [tailRun] : [checkpoint.Run, tailRun], position);
            IQuadSource source = new IndexSource(baseIndex, terms);

            if (position > from)
            {
                source = new QuadOverlay(source, tail);
            }

            DatasetView view = new(position, source, terms, checkpoint is null ? null : checkpoint.Run.Blob!.Release);
            checkpoint = null;
            return view;
        }
        finally
        {
            checkpoint?.Run.Blob!.Release();
        }
    }

    /// <summary>I5: the view at the greatest position whose timestamp is at or before <paramref name="timestamp"/>.</summary>
    public ValueTask<DatasetView> AsOfTimestampAsync(CommitTimestamp timestamp, CancellationToken cancellationToken = default) =>
        AsOfAsync(PositionAt(timestamp), cancellationToken);

    /// <summary>The greatest position whose timestamp is at or before <paramref name="timestamp"/>; 0 when none is.</summary>
    public Position PositionAt(CommitTimestamp timestamp) => new(_state.Commits.PositionAt(timestamp.Value.UtcTicks));

    /// <summary>
    /// R3: <c>(G_to \ G_from, G_from \ G_to)</c>, computed from the log alone.
    /// When <paramref name="from"/> is after <paramref name="to"/> the answer is
    /// the same formula, which is the inverse of the forward diff.
    /// </summary>
    public ValueTask<QuadDelta> DiffAsync(Position from, Position to, CancellationToken cancellationToken = default) =>
        DiffCoreAsync(from.Value, to.Value, cancellationToken);

    private async ValueTask<QuadDelta> DiffCoreAsync(long from, long to, CancellationToken cancellationToken)
    {
        State state = _state;
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfNegative(to);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(from, state.Head);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(to, state.Head);

        return from <= to
            ? (await NetAsync(state, from, to, cancellationToken).ConfigureAwait(false)).Delta
            : (await NetAsync(state, to, from, cancellationToken).ConfigureAwait(false)).Delta.Inverse();
    }

    /// <summary>The settings at a closed position: the fold of the settings commits up to it.</summary>
    public ValueTask<DatasetSettings> SettingsAtAsync(Position position, CancellationToken cancellationToken = default)
    {
        State state = _state;
        return new ValueTask<DatasetSettings>(SettingsAt(state, position.Value));
    }

    private static DatasetSettings SettingsAt(State state, long position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, state.Head);
        return state.SettingsAt(position);
    }

    /// <summary>
    /// T2: materialises the state at a closed position as a checkpoint in the
    /// derived store. Derived data: dropping it loses nothing (ADR 0015).
    /// </summary>
    public ValueTask CheckpointAsync(Position position, CancellationToken cancellationToken = default) =>
        CheckpointCoreAsync(position.Value, cancellationToken);

    // Written off the sequencer, so that a checkpoint never blocks a commit
    // (ADR 0070); published under it. The checkpoint is a streaming merge of
    // the runs that make up the position — the projection's own when the
    // position is its head, else the nearest checkpoint and the log tail —
    // written straight to the blob: it holds a block per input section and the
    // writer's buffer, never the state it materialises (issue #61).
    private async ValueTask CheckpointCoreAsync(long position, CancellationToken cancellationToken)
    {
        State state = _state;
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, state.Head);

        if (position == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position 0 is the empty dataset; it needs no checkpoint.");
        }

        (Run[] runs, Action release) = await RunsAtAsync(position, cancellationToken).ConfigureAwait(false);
        CommitEntry at = state.Commits.Entry(position);
        BlobName name = Checkpoint.Name(position);

        try
        {
            await DerivedFormat.WriteRunAsync(
                _storage.Derived,
                name,
                DerivedFormat.KindCheckpoint,
                Id,
                0,
                position,
                at.HeaderHash(),
                DerivedFormat.MergeOf(runs, dropRetractions: true),
                TermsOf(runs),
                at.BlankCount,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            release();
        }

        LoadedRun loaded = await DerivedFormat.TryLoadAsync(_storage.Derived, name, Id, DerivedFormat.KindCheckpoint, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A checkpoint just written does not read back.");
        Checkpoint checkpoint = new(loaded);

        await EnterSequencerAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Checkpoint? replaced = Array.Find(_state.Checkpoints, c => c.Position == position);
            _state = _state.WithCheckpoints(Insert(_state.Checkpoints, checkpoint));
            replaced?.Run.Blob!.Release();
        }
        finally
        {
            _sequencer.Release();
        }
    }

    // The runs whose merge is the state at a closed position, held until the
    // release is called: the projection's version when it is at the position,
    // else the nearest checkpoint at or below it and the log tail as one run.
    private async ValueTask<(Run[] Runs, Action Release)> RunsAtAsync(long position, CancellationToken cancellationToken)
    {
        while (true)
        {
            State state = _state;
            IndexVersion index = state.Index;

            if (index.Position == position && state.Failed is null)
            {
                if (index.TryAcquire())
                {
                    return (index.Runs, index.Release);
                }

                continue;
            }

            Checkpoint? checkpoint = state.CheckpointAtOrBelow(position);

            if (checkpoint is not null && !checkpoint.Run.Blob!.TryAcquire())
            {
                continue;
            }

            try
            {
                long from = checkpoint?.Position ?? 0;
                (QuadDelta tail, TermSection tailTerms) = await NetAsync(state, from, position, cancellationToken).ConfigureAwait(false);
                Run tailRun = Run.FromDelta(tail.Asserted, tail.Retracted, tailTerms, from, position);
                Run[] runs = checkpoint is null ? [tailRun] : [checkpoint.Run, tailRun];
                Action release = checkpoint is null ? static () => { } : checkpoint.Run.Blob!.Release;
                checkpoint = null;
                return (runs, release);
            }
            finally
            {
                checkpoint?.Run.Blob!.Release();
            }
        }
    }

    /// <summary>Drops a checkpoint. Nothing is lost: an as-of read at that position is slower, never wrong.</summary>
    public async ValueTask DropCheckpointAsync(Position position, CancellationToken cancellationToken = default)
    {
        await EnterSequencerAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            long at = position.Value;
            await _storage.Derived.DeleteAsync(Checkpoint.Name(at), cancellationToken).ConfigureAwait(false);
            Checkpoint? dropped = Array.Find(_state.Checkpoints, c => c.Position == at);
            Checkpoint[] kept = Array.FindAll(_state.Checkpoints, c => c.Position != at);
            _state = _state.WithCheckpoints(kept);

            // Views reading it keep it open until they are disposed.
            dropped?.Run.Blob!.Release();
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
    public IAsyncEnumerable<Commit> Subscribe(Position from, SubscriptionFilter filter, CancellationToken cancellationToken = default) =>
        SubscribeCore(from.Value, filter, cancellationToken);

    private async IAsyncEnumerable<Commit> SubscribeCore(
        long from, SubscriptionFilter filter, [EnumeratorCancellation] CancellationToken cancellationToken)
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

        for (long p = projection.Position.Value + 1; p <= state.Head; p++)
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
            using DatasetView view = await AsOfCoreAsync(checkpoint.Position, cancellationToken).ConfigureAwait(false);
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
        await EnterSequencerAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            State state = _state;
            Checkpoint? checkpoint = state.CheckpointAtOrBelow(state.Head);
            IndexVersion index = IndexVersion.Empty;

            if (checkpoint is not null && checkpoint.Run.Blob!.TryAcquire())
            {
                index = IndexVersion.FromBase(checkpoint.Position, checkpoint.Run);
            }

            for (long p = index.Position + 1; p <= state.Head; p++)
            {
                LoggedCommit commit = await ReadLoggedAsync(state, p, cancellationToken).ConfigureAwait(false);
                ThrowIfFaulted(p);
                index = index.Apply(commit.Asserted, commit.Retracted, state.TermsOf(commit, p), p);
            }

            IndexVersion previous = state.Index;
            _state = state.WithIndex(index, failed: null);
            await RetireAsync(previous, index, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sequencer.Release();
        }
    }

    /// <summary>
    /// Stops maintenance the dataset is running — a round in progress is
    /// cancelled and leaves <c>derived/</c> as it was — then closes every
    /// derived blob it holds. The storage is the caller's, and stays open.
    /// </summary>
    /// <exception cref="AggregateException">Background maintenance failed; the log is unaffected.</exception>
    public async ValueTask DisposeAsync()
    {
        Exception? failure = await StopMaintenanceAsync().ConfigureAwait(false);

        // The drain (ADR 0101): a commit inside the sequencer finishes, durable
        // and closed, before anything is torn down. Every later entrant sees
        // the flag: a commit is Unavailable, anything else is disposed.
        _disposing = true;
        await _sequencer.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        State state = _state;
        state.Index.Release();
        state.Commits.Release();

        foreach (Checkpoint checkpoint in state.Checkpoints)
        {
            checkpoint.Run.Blob!.Release();
        }

        // Runs retired while a view still read them, closed since.
        await DeleteClosedRunsAsync(CancellationToken.None).ConfigureAwait(false);

        // Released, not disposed: a waiter queued behind the drain enters,
        // sees the flag and leaves. A disposed semaphore would never wake it.
        _sequencer.Release();

        if (failure is not null)
        {
            throw new AggregateException("Background maintenance failed; derived/ is behind, and the log is unaffected.", failure);
        }
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

            if (_disposing)
            {
                return CommitResult.Unavailable(head, "The dataset is closing.");
            }

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
            TermView terms = state.TermsAt(_dictionary, state.Index.Runs, head);
            Resolver resolver = new(terms);
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

            if (kind == CommitKind.Data && (_options.Validators.Count > 0 || request?.Validators.Count > 0))
            {
                QuadOverlay proposed = new(new PendingSource(state.Index, terms, resolver.Allocations), delta);

                // The dataset's validators, then the request's (ADR 0058).
                if (!Validate(_options.Validators, proposed, delta, resolver, attachments, out ValidationVerdict rejected)
                    || (request is not null && !Validate(request.Validators, proposed, delta, resolver, attachments, out rejected)))
                {
                    return CommitResult.Rejected(head, rejected.Report);
                }
            }

            // Step 7.
            long next = head + 1;
            long now = _options.Clock.GetUtcNow().UtcTicks;
            long last = head == 0 ? long.MinValue : state.Commits.Entry(head).TimestampTicks;
            long timestamp = Math.Max(now, last);

            Allocation[] allocations = [.. resolver.Allocations];
            byte[] body = LogFormat.EncodeBody(allocations, delta.Asserted, delta.Retracted);
            byte[] previous = head == 0 ? LogFormat.Genesis() : state.Commits.Entry(head).HeaderHash();
            (long canonical, long blank) = Counters(terms, allocations);
            CommitHeader header = new(kind, next, timestamp, agent, cause, scope, canonical, blank, 0, [.. attachments], kindPayload, previous, SHA256.HashData(body));
            byte[] headerBytes = LogFormat.EncodeHeader(in header);
            byte[] headerHash = SHA256.HashData(headerBytes);

            CommitLocation location;

            try
            {
                location = await _writer.AppendAsync(kind, next, previous, body, headerBytes, cancellationToken).ConfigureAwait(false);
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
            DatasetSettings? changed = header.Kind == CommitKind.Settings ? Fold(state.SettingsAt(head), header) : null;
            CommitEntry closed = new(header.TimestampTicks, headerHash, location, header.CanonicalCount, header.BlankCount, state.LogBytesAt(head) + _writer.LastCommitBytes);
            CommitIndex commits = state.Commits.Append(in closed, changed);

            IndexVersion index = state.Index;
            string? failed = null;

            try
            {
                ThrowIfFaulted(next);
                index = index.Apply(delta.Asserted, delta.Retracted, TermSection.Of(allocations, terms.CanonicalCount), next);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // A derived artefact cannot veto a durable fact (§7). The
                // projection is behind, and the dataset says so rather than wait.
                failed = "The default projection failed at position " + next + " and must be rebuilt: " + error.Message;
            }

            _state = new State(next, commits, index, state.Checkpoints, failed);
            Signal();

            if (failed is null && MaintenanceDue(_state))
            {
                StartMaintenance();
            }
            return CommitResult.Committed(next);
        }
        finally
        {
            _sequencer.Release();
        }
    }

    // Runs validators in order; false with the first rejection.
    private static bool Validate(
        IEnumerable<ICommitValidator> validators,
        IQuadSource proposed,
        QuadDelta delta,
        Resolver resolver,
        List<ulong> attachments,
        out ValidationVerdict rejected)
    {
        foreach (ICommitValidator validator in validators)
        {
            ValidationVerdict verdict = validator.Validate(proposed, delta);

            if (!verdict.IsAccepted)
            {
                rejected = verdict;
                return false;
            }

            if (verdict.Attachment is not null)
            {
                attachments.Add(resolver.ResolveFinal(verdict.Attachment));
            }
        }

        rejected = default;
        return true;
    }

    private void ThrowIfFaulted(long position)
    {
        if (_options.DefaultProjectionFault?.Invoke(position) == true)
        {
            throw new InvalidOperationException("Injected default projection fault at position " + position + ".");
        }
    }

    /// <summary>The quads a subscription's filter passes, copied in order; how many.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    private static int Keep(ReadOnlySpan<Quad> quads, in SubscriptionFilter filter, Span<Quad> kept)
    {
        int count = 0;

        for (int i = 0; i < quads.Length; i++)
        {
            if (filter.Matches(in quads[i]))
            {
                kept[count++] = quads[i];
            }
        }

        return count;
    }

    // The dictionary's counters after a commit's allocations, for its header.
    private static (long Canonical, long Blank) Counters(TermView terms, Allocation[] allocations)
    {
        long canonical = terms.CanonicalCount;
        long blank = terms.BlankCount;

        foreach (Allocation allocation in allocations)
        {
            if (TermIds.ClassOf(allocation.Id) == IdClass.Blank)
            {
                blank++;
            }
            else
            {
                canonical++;
            }
        }

        return (canonical, blank);
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

    private static DatasetSettings Fold(DatasetSettings before, in CommitHeader header) =>
        header.Kind == CommitKind.Settings ? LogFormat.ApplySettings(before, header.KindPayload, header.Position) : before;

    private static Checkpoint[] Insert(Checkpoint[] checkpoints, Checkpoint checkpoint)
    {
        List<Checkpoint> list = [.. Array.FindAll(checkpoints, c => c.Position != checkpoint.Position), checkpoint];
        list.Sort((a, b) => a.Position.CompareTo(b.Position));
        return [.. list];
    }

    // The positions of the checkpoint blobs, newest first, by name alone.
    private static async ValueTask<long[]> CheckpointPositionsAsync(IDerivedStore derived, CancellationToken cancellationToken)
    {
        List<long> positions = [];

        foreach (BlobName name in await derived.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Checkpoint.TryParseName(name, out long position))
            {
                positions.Add(position);
            }
        }

        positions.Sort((a, b) => b.CompareTo(a));
        return [.. positions];
    }

    // The checkpoints that name this log, oldest first.
    private async ValueTask<Checkpoint[]> LoadCheckpointsAsync(State state, long[] candidates, CancellationToken cancellationToken)
    {
        List<Checkpoint> loaded = [];

        foreach (long position in candidates)
        {
            if (position < 1 || position > state.Head)
            {
                continue;
            }

            LoadedRun? run = await DerivedFormat.TryLoadAsync(_storage.Derived, Checkpoint.Name(position), Id, DerivedFormat.KindCheckpoint, cancellationToken).ConfigureAwait(false);

            if (run is null)
            {
                continue;
            }

            CommitEntry at = state.Commits.Entry(position);

            // A checkpoint names the commit it materialises. One copied beside a
            // different log is a cache miss, never a wrong answer (ADR 0041).
            if (run.Header.From != 0
                || run.Header.To != position
                || !at.HashEquals(run.Header.ToHash)
                || run.CanonicalCount != at.CanonicalCount
                || run.BlankCount != at.BlankCount
                || run.Run.HasRetractions)
            {
                run.Run.Blob!.Release();
                continue;
            }

            loaded.Add(new Checkpoint(run));
        }

        loaded.Reverse();
        return [.. loaded];
    }

    private static TermSection[] TermsOf(Run[] runs)
    {
        TermSection[] terms = new TermSection[runs.Length];

        for (int i = 0; i < runs.Length; i++)
        {
            terms[i] = runs[i].Terms;
        }

        return terms;
    }

    // The net delta of the commits after from up to to, and the dictionary
    // entries they allocated.
    private async ValueTask<(QuadDelta Delta, TermSection Terms)> NetAsync(State state, long from, long to, CancellationToken cancellationToken)
    {
        DeltaChain chain = new();
        List<Allocation> allocations = [];

        for (long p = from + 1; p <= to; p++)
        {
            LoggedCommit commit = await ReadLoggedAsync(state, p, cancellationToken).ConfigureAwait(false);
            chain.Add(commit.Asserted, commit.Retracted);
            allocations.AddRange(commit.Allocations);
        }

        TermSection terms = TermSection.Of(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(allocations), state.CanonicalAt(from));

        if (terms.To != state.CanonicalAt(to))
        {
            throw new LogVerificationException(to, "The dictionary's counters after position " + to + " disagree with its header.");
        }

        return (chain.ToDelta(), terms);
    }

    private ValueTask<LoggedCommit> ReadLoggedAsync(State state, long position, CancellationToken cancellationToken)
    {
        CommitLocation location = state.Commits.Entry(position).Location;
        byte[] previous = position == 1 ? LogFormat.Genesis() : state.Commits.Entry(position - 1).HeaderHash();
        return LogReader.ReadAsync(_storage.Log, Id, location, position, previous, cancellationToken);
    }

    private async ValueTask<Commit?> ReadCommitAsync(State state, long position, SubscriptionFilter filter, CancellationToken cancellationToken)
    {
        LoggedCommit logged = await ReadLoggedAsync(state, position, cancellationToken).ConfigureAwait(false);
        CommitHeader header = logged.Header;
        Quad[] assertedKept = new Quad[logged.Asserted.Length];
        Quad[] retractedKept = new Quad[logged.Retracted.Length];
        ReadOnlySpan<Quad> asserted = assertedKept.AsSpan(0, Keep(logged.Asserted, filter, assertedKept));
        ReadOnlySpan<Quad> retracted = retractedKept.AsSpan(0, Keep(logged.Retracted, filter, retractedKept));

        if (header.Kind == CommitKind.Data && asserted.IsEmpty && retracted.IsEmpty)
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

            allocations.Add(new TermAllocation(new TermHandle(allocation.Id), allocation.Term ?? TermAtHead(allocation.Id)));
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
            QuadDelta.Create(asserted, retracted),
            allocations.ToArray(),
            this);
    }

    // The term of an id in D_head: a blank node's label, an inline value, or
    // a canonical entry (a triple term materialised from its components'),
    // read from the projection's current runs. The dictionary is append-only,
    // so any id a closed commit carries resolves here (ADR 0042, amended
    // 2026-10-07).
    internal RdfTerm TermAtHead(ulong id)
    {
        if (TermIds.ClassOf(id) != IdClass.Canonical)
        {
            return _dictionary.Term([], id);
        }

        while (true)
        {
            IndexVersion index = _state.Index;

            if (index.TryAcquire())
            {
                try
                {
                    return _dictionary.Term(index.Runs, id);
                }
                finally
                {
                    index.Release();
                }
            }
        }
    }

    // Enters the sequencer for work other than a commit; after DisposeAsync
    // has begun, it leaves at once and reports the dataset disposed.
    private async ValueTask EnterSequencerAsync(CancellationToken cancellationToken)
    {
        await _sequencer.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (_disposing)
        {
            _sequencer.Release();
            throw new ObjectDisposedException(nameof(Dataset));
        }
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

    /// <summary>Everything a read needs, published as one reference.</summary>
    private sealed class State
    {
        internal State(long head, CommitIndex commits, IndexVersion index, Checkpoint[] checkpoints, string? failed)
        {
            Head = head;
            Commits = commits;
            Index = index;
            Checkpoints = checkpoints;
            Failed = failed;
        }

        internal long Head { get; }

        /// <summary>The commit index at <see cref="Head"/> (ADR 0089).</summary>
        internal CommitIndex Commits { get; }

        internal IndexVersion Index { get; }

        internal Checkpoint[] Checkpoints { get; }

        internal string? Failed { get; }

        internal long LogBytesAt(long position) => position == 0 ? 0 : Commits.Entry(position).LogBytes;

        internal long CanonicalAt(long position) => position == 0 ? 0 : Commits.Entry(position).CanonicalCount;

        internal long BlankAt(long position) => position == 0 ? 0 : Commits.Entry(position).BlankCount;

        internal TermView TermsAt(TermDictionary dictionary, Run[] runs, long position) =>
            new(dictionary, runs, CanonicalAt(position), BlankAt(position));

        /// <summary>
        /// A commit read from the log's dictionary entries as a term section,
        /// checked against the counters its header and the one before it name.
        /// </summary>
        internal TermSection TermsOf(LoggedCommit commit, long position)
        {
            long blanks = 0;

            foreach (Allocation allocation in commit.Allocations)
            {
                blanks += TermIds.ClassOf(allocation.Id) == IdClass.Blank ? 1 : 0;
            }

            try
            {
                TermSection terms = TermSection.Of(commit.Allocations, CanonicalAt(position - 1));

                if (terms.To == CanonicalAt(position) && BlankAt(position - 1) + blanks == BlankAt(position))
                {
                    return terms;
                }
            }
            catch (InvalidOperationException)
            {
            }

            throw new LogVerificationException(position, "The dictionary's counters after position " + position + " disagree with its header.");
        }

        internal DatasetSettings SettingsAt(long position) => Commits.SettingsAt(position);

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

        internal State WithCommits(CommitIndex commits) => new(Head, commits, Index, Checkpoints, Failed);
    }
}
