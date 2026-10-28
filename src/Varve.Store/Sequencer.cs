// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// The sequencer (spec T1, ADR 0011) with group commit (ADR 0124): requests
/// queue in arrival order; whoever holds the sequencer leads, staging each
/// queued request in turn against the state the ones before it produced, and
/// writing every commit it staged with one flush.
/// </summary>
/// <remarks>
/// <para>
/// **Staging is the unbatched computation, run earlier.** A member is
/// normalised, resolved, checked and validated against a <see cref="Staging"/>
/// that holds the head, the default projection, the dictionary's counters, the
/// timestamp and the header hash after every member before it; so it sees
/// exactly what it would have seen had each earlier member been written alone.
/// The default projection is applied as a member is staged, so that the next
/// one can read it, and published only after the batch is durable.
/// </para>
/// <para>
/// **Every answer waits for the write.** A member's outcome, committed or not,
/// was computed against staged state, which is real only once the write
/// returns. A failed write fails every member that was staged with it.
/// </para>
/// <para>
/// Discarding staged state is safe for the dictionary's caches: they remember
/// only ids found in runs, and the one way staged runs are discarded is a
/// failed write, after which the dataset refuses every commit until reopened.
/// </para>
/// </remarks>
public sealed partial class Dataset
{
    private readonly Lock _queueGate = new();
    private readonly Queue<PendingCommit> _queue = new();
    private PendingCommit? _deferred;
    private bool _leading;
    private long _writes;

    /// <summary>Test seam: how many writes of the log the sequencer has made, each one batch.</summary>
    internal long WritesForTests => Interlocked.Read(ref _writes);

    private async ValueTask<CommitResult> SequenceAsync(
        CommitKind kind,
        CommitRequestBuilder? request,
        CommitMetadata metadata,
        long? expected,
        byte[] kindPayload,
        CancellationToken cancellationToken)
    {
        if (IsFollower)
        {
            return CommitResult.Unavailable(_state.Head, "This dataset is a follower: its writer commits, and a follower only reads (ADR 0164).");
        }

        long started = _options.Clock.GetTimestamp();
        PendingCommit pending = new(kind, request, metadata, expected, kindPayload, request?.ExpectedGraphs);
        _metrics.Queued(1);

        bool lead;

        lock (_queueGate)
        {
            _queue.Enqueue(pending);
            lead = !_leading;
            _leading = true;
        }

        // The request that finds no leader leads: it takes the sequencer and
        // writes batches until its own is answered, then hands the rest to a
        // task that leads until the queue is empty. Every other request only
        // waits for its answer, and never touches the sequencer: a request a
        // leader wrote has nothing left to do there.
        if (lead)
        {
            await _sequencer.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            await LeadWhileQueuedAsync(pending).ConfigureAwait(false);
        }

        try
        {
            await pending.Answer.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!pending.Answer.Task.IsCompleted)
        {
            // Withdrawn while still queued, it is never written (ADR 0124
            // point 6). Taken already, it is written with its batch, and the
            // caller has only stopped waiting.
            if (pending.TryWithdraw())
            {
                _metrics.Queued(-1);
            }

            throw;
        }

        CommitResult result = await pending.Answer.Task.ConfigureAwait(false);

        _metrics.Committed(_options.Clock.GetElapsedTime(started), result.Outcome switch
        {
            CommitOutcome.Committed => "committed",
            CommitOutcome.NoChange => "no_change",
            CommitOutcome.Conflict => "conflict",
            CommitOutcome.Rejected => "rejected",
            _ => "unavailable",
        });
        _metrics.Observe(this);
        return result;
    }

    // Inside the sequencer: leads batches until `until` is answered — or, for
    // the task a leader hands over to, until the queue is empty — then lets
    // the sequencer go when nothing is queued, or hands it on when something
    // is. The sequencer is held across the hand-over, never released between.
    private async ValueTask LeadWhileQueuedAsync(PendingCommit? until)
    {
        try
        {
            while (true)
            {
                lock (_queueGate)
                {
                    if (_queue.Count == 0 && _deferred is null)
                    {
                        _leading = false;
                        _sequencer.Release();
                        return;
                    }
                }

                if (until is not null && until.Answer.Task.IsCompleted)
                {
                    _ = Task.Run(async () => await LeadWhileQueuedAsync(null).ConfigureAwait(false));
                    return;
                }

                await LeadAsync().ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            // A defect in the sequencer itself: nothing queued may wait for
            // ever. The dataset is broken, and every queued request fails.
            _broken = "The sequencer failed; reopen the dataset: " + error.Message;

            lock (_queueGate)
            {
                while (_queue.TryDequeue(out PendingCommit? queued))
                {
                    queued.Answer.TrySetException(error);
                }

                _deferred?.Answer.TrySetException(error);
                _deferred = null;
                _leading = false;
            }

            _sequencer.Release();
        }
    }

    // Takes queued requests in arrival order, stages each, writes the staged
    // commits as one batch, publishes, and answers. Inside the sequencer.
    private async ValueTask LeadAsync()
    {
        State state = _state;
        Staging staging = new(state);
        GroupCommitOptions bounds = _options.GroupCommit ?? GroupCommitOptions.For(Durability);
        List<Member> members = [];
        int staged = 0;
        long records = 0;
        long bytes = 0;

        while (members.Count == 0 || (records < bounds.MaxRecords && bytes < bounds.MaxBytes.Value && staging.Failed is null))
        {
            PendingCommit? next = TakeNext();

            if (next is null)
            {
                break;
            }

            _metrics.Queued(-1);
            Member member = new(next);
            members.Add(member);

            // Bytes of records a batch of two or more commits may still take:
            // never past MaxBytes, which is never past the write window the
            // reader allows for a torn batch (ADR 0124, I6).
            long budget = staged == 0 ? long.MaxValue : bounds.MaxBytes.Value - bytes;
            bool deferred;

            try
            {
                member.Result = Stage(staging, next, budget, out member.Commit, out deferred);
            }
            catch (Exception error)
            {
                // The request's own fault (a foreign handle, a throwing
                // validator): it is answered with it, and nothing was staged.
                member.Error = error;
                continue;
            }

            if (deferred)
            {
                // It would take the batch past its bytes: first in the next
                // batch, staged again there against the same state, before it
                // read the clock, so its timestamp is the unbatched one.
                members.RemoveAt(members.Count - 1);
                _metrics.Queued(1);

                lock (_queueGate)
                {
                    _deferred = next;
                }

                break;
            }

            if (member.Commit is { } commit)
            {
                staged++;
                records += _writer.RecordsOf(commit.Body.Length);
                bytes += RecordBytes(commit.Body.Length, commit.HeaderBytes.Length);
            }
        }

        if (members.Count == 0)
        {
            return;
        }

        CommitLocation[] locations = [];
        long[] lengths = [];

        if (staged > 0)
        {
            List<LogWriter.Append> appends = new(staged);

            foreach (Member member in members)
            {
                if (member.Commit is { } commit)
                {
                    appends.Add(new LogWriter.Append(commit.Kind, commit.Position, commit.Previous, commit.Body, commit.HeaderBytes));
                }
            }

            try
            {
                (locations, lengths) = await _writer.AppendBatchAsync(appends, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                // The records may be half written. The log is still readable —
                // an unclosed tail is ignored on open — but this writer's idea
                // of the active segment can no longer be trusted, and nothing
                // staged against it was ever real (ADR 0124 point 3).
                _broken = "An append failed; reopen the dataset to recover.";
                _appendFailed = error is not WriterSupersededException;

                foreach (Member member in members)
                {
                    member.Pending.Answer.TrySetException(member.Error ?? error);
                }

                return;
            }

            _metrics.Batched(staged);
            Interlocked.Increment(ref _writes);

            // Every staged commit is durable and closed: they stand, whatever follows.
            CommitIndex commits = state.Commits;
            long logBytes = state.LogBytesAt(state.Head);
            int at = 0;

            foreach (Member member in members)
            {
                if (member.Commit is not { } commit)
                {
                    continue;
                }

                DatasetSettings? changed = commit.Header.Kind == CommitKind.Settings
                    ? Fold(commits.SettingsAt(commit.Position - 1), commit.Header)
                    : null;
                logBytes += lengths[at];
                CommitEntry closed = new(commit.Header.TimestampTicks, commit.HeaderHash, locations[at], commit.Header.CanonicalCount, commit.Header.BlankCount, logBytes);
                commits = commits.Append(in closed, changed);
                at++;
            }

            _state = new State(staging.Head, commits, staging.Index, state.Checkpoints, staging.Failed, staging.Graphs);
            Signal();
            CheckSpillBound(staging.Index);

            if (staging.Failed is null && MaintenanceDue(_state))
            {
                StartMaintenance();
            }
        }

        foreach (Member member in members)
        {
            if (member.Error is not null)
            {
                member.Pending.Answer.TrySetException(member.Error);
            }
            else
            {
                member.Pending.Answer.TrySetResult(member.Result);
            }
        }
    }

    /// <summary>
    /// Test seam: holds the sequencer until disposed, so that requests made
    /// meanwhile queue, and the first to enter after it leads them all as one
    /// batch, in the order they were made.
    /// </summary>
    internal async ValueTask<IDisposable> HoldSequencerForTestsAsync(CancellationToken cancellationToken)
    {
        await _sequencer.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Held(_sequencer);
    }

    private sealed class Held(SemaphoreSlim sequencer) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                sequencer.Release();
            }
        }
    }

    private PendingCommit? TakeNext()
    {
        lock (_queueGate)
        {
            if (_deferred is { } deferred)
            {
                _deferred = null;
                return deferred;
            }

            while (_queue.TryDequeue(out PendingCommit? next))
            {
                if (next.TryTake())
                {
                    return next;
                }
            }
        }

        return null;
    }

    // Spec T1 steps 1 to 6 against the staged state, and step 7's encoding; on
    // a commit, advances the staging to it. The write is the batch's.
    private CommitResult Stage(Staging staging, PendingCommit pending, long budget, out StagedCommit? staged, out bool deferred)
    {
        staged = null;
        deferred = false;
        long head = staging.Head;
        CommitKind kind = pending.Kind;
        CommitRequestBuilder? request = pending.Request;
        CommitMetadata metadata = pending.Metadata;

        // A writer that stepped down appends nothing more (ADR 0179 point 6);
        // thrown, it answers this commit alone, with nothing staged.
        if (_steppedDown is { } reason)
        {
            throw new WriterSteppedDownException(reason, "The writer released the dataset for " + reason + " and commits nothing more; send the write again.");
        }

        if (_disposing)
        {
            return CommitResult.Unavailable(head, "The dataset is closing.");
        }

        if (_broken is not null)
        {
            return CommitResult.Unavailable(head, _broken);
        }

        if (staging.Failed is not null)
        {
            return CommitResult.Unavailable(head, staging.Failed);
        }

        if (staging.Index.Position != head)
        {
            return CommitResult.Unavailable(head, "The default projection is not at the readable head.");
        }

        TermView terms = new(_dictionary, staging.Index.Runs, staging.Canonical, staging.Blank);

        // Step 1: a commit after the expected position that touched a graph
        // of its scope conflicts; with no scope, any commit does (ADR 0125).
        if (pending.Expected is long position && (position > head || (pending.Graphs is null ? position != head : Touched(staging.Graphs, terms, pending.Graphs, position))))
        {
            return CommitResult.Conflict(head);
        }

        // Step 2.
        Resolver resolver = new(terms);
        ulong agent = resolver.Resolve(metadata.Agent);
        ulong cause = resolver.Resolve(metadata.Cause);
        ulong scope = resolver.Resolve(metadata.GraphScope);
        ulong[] hosted = new ulong[metadata.Attachments.Count];

        for (int i = 0; i < hosted.Length; i++)
        {
            hosted[i] = resolver.Resolve(metadata.Attachments[i]);
        }

        // Step 3: the operations in order over an overlay on the head; the net result.
        _pending.Clear();

        if (request is not null)
        {
            foreach (CommitRequestBuilder.Operation operation in request.Operations)
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
            bool present = staging.Index.Contains(in quad);

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

        HashSet<ulong> referenced = [agent, cause, scope, .. hosted];

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

        // Steps 5 and 6. The host's attachments come first (ADR 0165).
        List<ulong> attachments = new(hosted.Length);

        foreach (ulong id in hosted)
        {
            attachments.Add(map(id));
        }

        if (kind == CommitKind.Data && (_options.Validators.Count > 0 || request?.Validators.Count > 0))
        {
            QuadOverlay proposed = new(new PendingSource(staging.Index, terms, resolver.Allocations), delta);

            // The dataset's validators, then the request's (ADR 0058).
            if (!Validate(_options.Validators, proposed, delta, resolver, attachments, out ValidationVerdict rejected)
                || (request is not null && !Validate(request.Validators, proposed, delta, resolver, attachments, out rejected)))
            {
                return CommitResult.Rejected(head, rejected.Report);
            }
        }

        // Step 7, up to the write: the size first, so that a member the batch
        // has no room for is deferred before it reads the clock; then the
        // timestamp, taken now, at staging (ADR 0124 point 4), and the header
        // linking to the member before.
        long next = head + 1;
        Allocation[] allocations = [.. resolver.Allocations];
        byte[] body = LogFormat.EncodeBody(allocations, delta.Asserted, delta.Retracted);
        byte[] previous = staging.HeaderHash;
        byte[] content = SHA256.HashData(body);
        (long canonical, long blank) = Counters(terms, allocations);
        CommitHeader sized = new(kind, next, 0, agent, cause, scope, canonical, blank, 0, [.. attachments], pending.KindPayload, previous, content);

        if (RecordBytes(body.Length, LogFormat.EncodeHeader(in sized).Length) > budget)
        {
            deferred = true;
            return default;
        }

        long timestamp = Math.Max(_options.Clock.GetUtcNow().UtcTicks, staging.Timestamp);
        CommitHeader header = new(kind, next, timestamp, agent, cause, scope, canonical, blank, 0, [.. attachments], pending.KindPayload, previous, content);
        byte[] headerBytes = LogFormat.EncodeHeader(in header);
        byte[] headerHash = SHA256.HashData(headerBytes);

        // The default projection, so the next member reads this one. A
        // failure here is the projection's, not the log's: the commit is
        // written, and the dataset fails at it (§7, ADR 0124 point 5).
        IndexVersion index = staging.Index;
        string? failed = null;

        try
        {
            ThrowIfFaulted(next);
            index = index.Apply(delta.Asserted, delta.Retracted, TermSection.Of(allocations, terms.CanonicalCount), next);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            failed = "The default projection failed at position " + next + " and must be rebuilt: " + error.Message;
        }

        staged = new StagedCommit(kind, next, previous, body, header, headerBytes, headerHash);
        staging.Advance(next, index, canonical, blank, timestamp, headerHash, failed, staging.Graphs.Apply(kind, delta.Asserted, delta.Retracted, next));
        return CommitResult.Committed(next);
    }

    // Whether a commit after `since` touched a graph of the scope. A graph the
    // dictionary has never held was never touched, but by a commit that
    // touched every graph, which GraphVersions.All answers.
    private static bool Touched(GraphVersions graphs, TermView terms, IReadOnlyList<RequestTerm> scope, long since)
    {
        if (graphs.All > since)
        {
            return true;
        }

        foreach (RequestTerm graph in scope)
        {
            ulong id = graph.IsNone ? 0
                : graph.IsExisting ? graph.Handle.Value
                : terms.Dictionary.TryFind(terms.Runs, graph.Term!, terms.CanonicalCount, out ulong found) ? found
                : ulong.MaxValue;

            if (id != ulong.MaxValue && graphs.Touched([id], since))
            {
                return true;
            }
        }

        return false;
    }

    // The bytes of a commit's records in the log: record headers, body, and
    // the closing record's commit header and its length.
    private long RecordBytes(int bodyLength, int headerLength) =>
        ((long)_writer.RecordsOf(bodyLength) * LogFormat.RecordHeaderLength) + bodyLength + headerLength + 4;

    /// <summary>A request waiting for the sequencer, and its answer.</summary>
    private sealed class PendingCommit(CommitKind kind, CommitRequestBuilder? request, CommitMetadata metadata, long? expected, byte[] kindPayload, IReadOnlyList<RequestTerm>? graphs)
    {
        private const int Queued = 0;
        private const int Taken = 1;
        private const int Withdrawn = 2;

        private int _state;

        internal CommitKind Kind { get; } = kind;

        internal CommitRequestBuilder? Request { get; } = request;

        internal CommitMetadata Metadata { get; } = metadata;

        internal long? Expected { get; } = expected;

        internal byte[] KindPayload { get; } = kindPayload;

        /// <summary>The expected position's scope (ADR 0125); null is every graph.</summary>
        internal IReadOnlyList<RequestTerm>? Graphs { get; } = graphs;

        internal TaskCompletionSource<CommitResult> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>A leader takes it: from now on it is not withdrawn.</summary>
        internal bool TryTake() => Interlocked.CompareExchange(ref _state, Taken, Queued) == Queued;

        /// <summary>Its caller withdraws it: no leader will take it.</summary>
        internal bool TryWithdraw() => Interlocked.CompareExchange(ref _state, Withdrawn, Queued) == Queued;
    }

    /// <summary>One request of a batch: its outcome, and its commit when it made one.</summary>
    private sealed class Member(PendingCommit pending)
    {
        internal StagedCommit? Commit;

        internal PendingCommit Pending { get; } = pending;

        internal CommitResult Result { get; set; }

        internal Exception? Error { get; set; }
    }

    /// <summary>A commit staged and encoded, waiting for its batch's write.</summary>
    private sealed record StagedCommit(CommitKind Kind, long Position, byte[] Previous, byte[] Body, CommitHeader Header, byte[] HeaderBytes, byte[] HeaderHash);

    /// <summary>The head as the next member of a batch sees it.</summary>
    private sealed class Staging
    {
        internal Staging(State state)
        {
            Head = state.Head;
            Index = state.Index;
            Canonical = state.CanonicalAt(state.Head);
            Blank = state.BlankAt(state.Head);
            Timestamp = state.Head == 0 ? long.MinValue : state.Commits.Entry(state.Head).TimestampTicks;
            HeaderHash = state.Head == 0 ? LogFormat.Genesis() : state.Commits.Entry(state.Head).HeaderHash();
            Failed = state.Failed;
            Graphs = state.Graphs;
        }

        internal GraphVersions Graphs { get; private set; }

        internal long Head { get; private set; }

        internal IndexVersion Index { get; private set; }

        internal long Canonical { get; private set; }

        internal long Blank { get; private set; }

        internal long Timestamp { get; private set; }

        internal byte[] HeaderHash { get; private set; }

        internal string? Failed { get; private set; }

        internal void Advance(long head, IndexVersion index, long canonical, long blank, long timestamp, byte[] headerHash, string? failed, GraphVersions graphs)
        {
            Graphs = graphs;
            Head = head;
            Index = index;
            Canonical = canonical;
            Blank = blank;
            Timestamp = timestamp;
            HeaderHash = headerHash;
            Failed = failed;
        }
    }
}
