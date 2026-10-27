// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>The tier-1 follower: a dataset that tails the log another process writes (ADR 0164).</summary>
public sealed partial class Dataset
{
    /// <summary>
    /// Whether this dataset was opened as a follower: it reads what its
    /// writer appends, through <see cref="AdvanceAsync"/>, and commits nothing.
    /// </summary>
    public bool IsFollower { get; private set; }

    // The quads in the memtable when the follower opened: what its open
    // replayed from the log above the catalog, before any advance.
    private long _memtableAtOpen;

    // Whether chunks the storage adopted may not have been applied yet:
    // set from before a refresh until a scan completes (ADR 0164 point 2).
    private bool _scanOwed;

    /// <summary>
    /// Whether this follower's memtable has grown by more than
    /// <see cref="DatasetOptions.MemtableLimit"/> since it opened, by the
    /// measure the writer's own flush compares with the limit (ADR 0164 point
    /// 5): its host then opens it again from the writer's newest catalog, so
    /// that a long-lived follower's memory stays bounded. Reads
    /// pinned to this dataset keep it until they end. Counted from the open,
    /// so a follower opened again on the same catalog is not due again until
    /// it has applied as much once more.
    /// </summary>
    public bool ReopenDue => IsFollower && _state.Index.MemtableCount - _memtableAtOpen > _options.MemtableLimit.Value;

    /// <summary>
    /// Opens a dataset another process writes, as a follower (ADR 0164): an
    /// ordinary dataset at the head its storage holds now, whose
    /// <see cref="AdvanceAsync"/> reads what the writer has closed since.
    /// Every write is refused with <see cref="CommitOutcome.Unavailable"/>,
    /// and a checkpoint with <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <exception cref="LogVerificationException">The log does not verify.</exception>
    /// <exception cref="InvalidOperationException">The storage holds no dataset.</exception>
    public static async ValueTask<Dataset> OpenFollowerAsync(ITailingStorage storage, DatasetOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        Dataset dataset = await OpenAsync(storage, options, cancellationToken).ConfigureAwait(false);
        dataset.IsFollower = true;
        dataset._memtableAtOpen = dataset._state.Index.MemtableCount;
        return dataset;
    }

    /// <summary>
    /// Refreshes the storage and applies every commit the writer has closed
    /// since the head, verified as an open verifies them (I6): to the commit
    /// index, the default projection and the graph versions. A commit not yet
    /// closed is left for a later advance. Returns the head after.
    /// </summary>
    /// <exception cref="InvalidOperationException">The dataset is not a follower.</exception>
    /// <exception cref="LogVerificationException">What the writer appended does not verify.</exception>
    public async ValueTask<Position> AdvanceAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFollower)
        {
            throw new InvalidOperationException("Only a follower advances; a writer's head moves by its own commits.");
        }

        await _sequencer.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposing, this);

            // A refresh that adopted chunks and then failed, or a scan that
            // failed, leaves them held by the storage and not yet applied: the
            // next advance scans even when its refresh finds nothing new, or the
            // follower would stay behind until the writer appended again.
            bool owed = _scanOwed;
            _scanOwed = true;

            if (_storage is ITailingStorage tailing && !await tailing.RefreshAsync(cancellationToken).ConfigureAwait(false) && !owed)
            {
                _scanOwed = false;
                return Head;
            }

            await ApplyClosedAsync(cancellationToken).ConfigureAwait(false);
            return Head;
        }
        finally
        {
            _sequencer.Release();
        }
    }

    /// <summary>
    /// A follower becomes its dataset's writer in place (ADR 0179 point 8).
    /// - It applies what the writer it followed closed.
    /// - <paramref name="claim"/> claims the epoch on the storage and fences the
    ///   log. It is the host's, which knows the backend.
    /// - It applies what the claim adopted, and opens the log for appends at
    ///   the head.
    ///
    /// Its projection, graph versions, commit index and cache stay what they
    /// are, so it commits at once. False when <paramref name="claim"/> lost;
    /// the dataset is then still a follower, but its storage may no longer
    /// refresh, and the host opens it again.
    /// </summary>
    /// <exception cref="InvalidOperationException">The dataset is not a follower.</exception>
    /// <exception cref="LogVerificationException">What the writer appended does not verify.</exception>
    public async ValueTask<bool> TakeWriteAsync(Func<CancellationToken, ValueTask<bool>> claim, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);

        if (!IsFollower)
        {
            throw new InvalidOperationException("Only a follower takes its dataset's writing over; a writer writes already.");
        }

        await _sequencer.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposing, this);

            if (_storage is ITailingStorage tailing)
            {
                await tailing.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }

            await ApplyClosedAsync(cancellationToken).ConfigureAwait(false);

            if (!await claim(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            // What the claim adopted, then the log opened at its end as a writer's open opens it.
            LogScan scan = await ApplyClosedAsync(cancellationToken).ConfigureAwait(false);
            _writer = await LogWriter.OpenAsync(_storage.Log, Id, scan, _options.SegmentBytes.Value, (int)_options.MaxRecordBytes.Value, cancellationToken).ConfigureAwait(false);
            IsFollower = false;
        }
        finally
        {
            _sequencer.Release();
        }

        // The writer's open is an event of the deletion rule (ADR 0160), and so is this one.
        if (_storage is IExtentReaders { DeletesExtents: true })
        {
            await DeleteUnnamedSetsAsync(poll: true, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    // Applies every commit the storage holds closed above the head, as an
    // open verifies them (I6), and returns the scan of the log's end. Inside
    // the sequencer.
    private async ValueTask<LogScan> ApplyClosedAsync(CancellationToken cancellationToken)
    {
        State state = _state;
        long head = state.Head;
        List<ScannedCommit> closed = [];
        ValueTask Keep(ScannedCommit commit)
        {
            closed.Add(commit);
            return ValueTask.CompletedTask;
        }

        LogScan scan;

        if (head == 0)
        {
            scan = await LogReader.ScanAsync(_storage.Log, Id, 0, Keep, _options.TornWindow, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            CommitEntry last = state.Commits.Entry(head);
            byte[] previous = head == 1 ? LogFormat.Genesis() : state.Commits.Entry(head - 1).HeaderHash();
            CommitLocation end = await LogReader.EndAsync(_storage.Log, Id, last.Location, head, previous, cancellationToken).ConfigureAwait(false);
            LogStart start = new(end.Segment, end.Offset, head, last.HeaderHash());
            scan = await LogReader.ScanFromAsync(_storage.Log, Id, start, head, Keep, _options.TornWindow, cancellationToken).ConfigureAwait(false);
        }

        if (closed.Count == 0)
        {
            _scanOwed = false;
            return scan;
        }

        CommitIndex commits = state.Commits;
        long logBytes = state.LogBytesAt(head);
        long p = head;

        foreach (ScannedCommit commit in closed)
        {
            p++;

            if (commit.Header.Kind == CommitKind.Erasure)
            {
                throw new LogVerificationException(p, "Position " + p + " is an erasure commit, which needs erasure mode, and this version of Varve does not support erasure mode.");
            }

            DatasetSettings? changed = commit.Header.Kind == CommitKind.Settings ? Fold(commits.SettingsAt(p - 1), commit.Header) : null;
            logBytes += commit.Bytes;
            CommitEntry entry = new(commit.Header.TimestampTicks, commit.HeaderHash, commit.Location, commit.Header.CanonicalCount, commit.Header.BlankCount, logBytes);
            commits = commits.Append(in entry, changed);
        }

        State partial = new(p, commits, state.Index, state.Checkpoints, null, state.Graphs);
        IndexVersion index = state.Index;
        GraphVersions graphs = state.Graphs;
        p = head;

        foreach (ScannedCommit commit in closed)
        {
            p++;
            LoggedCommit full = commit.Full ?? await ReadLoggedAsync(partial, p, cancellationToken).ConfigureAwait(false);
            index = index.Apply(full.Asserted, full.Retracted, partial.TermsOf(full, p), p);
            graphs = graphs.Apply(full.Header.Kind, full.Asserted, full.Retracted, p);
        }

        _state = new State(p, commits, index, state.Checkpoints, null, graphs);
        _scanOwed = false;
        Signal();
        return scan;
    }
}
