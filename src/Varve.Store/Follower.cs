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

            if (_storage is ITailingStorage tailing && !await tailing.RefreshAsync(cancellationToken).ConfigureAwait(false))
            {
                return Head;
            }

            await AdoptClosedAsync(long.MaxValue, cancellationToken).ConfigureAwait(false);
            return Head;
        }
        finally
        {
            _sequencer.Release();
        }
    }

    /// <summary>
    /// Applies every commit the log has closed after the head, verified as an
    /// open verifies them (I6), to the commit index, the default projection
    /// and the graph versions; returns the scan, which says where the log's
    /// tail is. A follower calls it for its writer's commits; a writer whose
    /// append failed calls it for its own, which reached the log without being
    /// acknowledged (ADR 0214 point 2), and only up to <paramref name="upTo"/>,
    /// the last it flushed: a commit closed past it is not committed. Inside
    /// the sequencer.
    /// </summary>
    private async ValueTask<LogScan> AdoptClosedAsync(long upTo, CancellationToken cancellationToken)
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
            scan = await LogReader.ScanAsync(_storage.Log, Id, 0, Keep, _options.TornWindow, upTo, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            CommitEntry last = state.Commits.Entry(head);
            byte[] previous = head == 1 ? LogFormat.Genesis() : state.Commits.Entry(head - 1).HeaderHash();
            CommitLocation end = await LogReader.EndAsync(_storage.Log, Id, last.Location, head, previous, cancellationToken).ConfigureAwait(false);
            LogStart start = new(end.Segment, end.Offset, head, last.HeaderHash());
            scan = await LogReader.ScanFromAsync(_storage.Log, Id, start, head, Keep, _options.TornWindow, upTo, cancellationToken).ConfigureAwait(false);
        }

        if (closed.Count == 0)
        {
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
        Signal();
        return scan;
    }
}
