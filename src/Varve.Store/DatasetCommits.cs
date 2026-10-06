// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>The commit index: opened against the log, paged out and merged as maintenance (ADR 0085).</summary>
public sealed partial class Dataset
{
    private long _commitSequence;

    // Whether the commit index holds twice its cache in memory.
    private bool CommitPagingDue(State state) => state.Commits.InMemory >= 2L * _options.CommitCache;

    private static bool CommitMergeDue(CommitIndex commits) =>
        commits.Segments.Length >= 2 && commits.Segments[^1].Count >= commits.Segments[^2].Count;

    // Writes the oldest entries in memory as a blob, keeping the cache's worth,
    // and swaps it in under the sequencer. True when it did.
    private async ValueTask<bool> PageCommitsAsync(CancellationToken cancellationToken)
    {
        CommitIndex commits = _state.Commits;
        long from = commits.PagedTo;
        long to = commits.Head - _options.CommitCache;

        if (to <= from)
        {
            return false;
        }

        if (await WriteCommitSegmentAsync(from, to, commits.Entry, cancellationToken).ConfigureAwait(false) is not { } segment)
        {
            return false;
        }

        await _sequencer.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _state = _state.WithCommits(_state.Commits.WithPaged(segment));
        }
        finally
        {
            _sequencer.Release();
        }

        return true;
    }

    // Merges the newest two blobs into one, swaps it in, and retires them.
    private async ValueTask<bool> MergeCommitsAsync(CancellationToken cancellationToken)
    {
        CommitIndex commits = _state.Commits;

        if (!CommitMergeDue(commits) || !commits.TryAcquire())
        {
            return false;
        }

        CommitSegment older = commits.Segments[^2];
        CommitSegment newer = commits.Segments[^1];
        CommitSegment? merged;

        try
        {
            merged = await WriteCommitSegmentAsync(older.From, newer.To, p => (p <= older.To ? older : newer).Read(p), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            commits.Release();
        }

        if (merged is null)
        {
            return false;
        }

        await _sequencer.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _state = _state.WithCommits(_state.Commits.WithMerged(merged));
        }
        finally
        {
            _sequencer.Release();
        }

        older.Blob.Retire(_closedRuns.Enqueue);
        newer.Blob.Retire(_closedRuns.Enqueue);
        await DeleteClosedRunsAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    // The blob written and loaded; null when it is gone before it could be
    // opened again — another dataset opened on the same storage cleared it —
    // and the round is then done again later rather than failed.
    private async ValueTask<CommitSegment?> WriteCommitSegmentAsync(long from, long to, Func<long, CommitEntry> entryAt, CancellationToken cancellationToken)
    {
        BlobName name = CommitIndexFormat.Name(from, to, Interlocked.Increment(ref _commitSequence));
        await CommitIndexFormat.WriteAsync(_storage.Derived, name, Id, from, to, entryAt, cancellationToken).ConfigureAwait(false);
        return await CommitIndexFormat.TryLoadAsync(_storage.Derived, name, Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the commit index as the open scan passes each closed commit: the
    /// blobs in <c>derived/</c> that continue the chain from position 0 are
    /// checked entry by entry against the log and kept while they agree; from
    /// the first that does not — missing, stale, damaged, or another log's —
    /// the entries are rebuilt from the log, written out as they reach twice
    /// the cache, so that opening holds no more of the index than a running
    /// dataset does (ADR 0085).
    /// </summary>
    private sealed class CommitIndexOpener
    {
        private readonly IDerivedStore _store;
        private readonly DatasetId _dataset;
        private readonly int _cache;
        private readonly Func<CommitIndex> _current;
        private readonly Dictionary<long, List<CommitSegment>> _candidates = [];
        private readonly List<CommitSegment> _chain = [];
        private readonly List<CommitSegment> _rejected = [];
        private readonly List<SettingsPoint> _settings = [];
        private readonly HashSet<string> _listed = new(StringComparer.Ordinal);
        private readonly HashSet<string> _ahead = new(StringComparer.Ordinal);
        private CommitSegment? _verifying;
        private CommitIndex? _building;
        private DatasetSettings _folded = DatasetSettings.Default;
        private long _logBytes;
        private long _position;

        private CommitIndexOpener(IDerivedStore store, DatasetId dataset, int cache, Func<CommitIndex> current)
        {
            _store = store;
            _dataset = dataset;
            _cache = cache;
            _current = current;
        }

        /// <summary>The bodies the scan read whole, for replay, by position.</summary>
        internal Dictionary<long, LoggedCommit> Bodies { get; } = [];

        internal long Sequence { get; private set; }

        /// <summary>Loads every commit index blob that reads as one of this dataset's.</summary>
        internal static async ValueTask<CommitIndexOpener> StartAsync(IDerivedStore store, DatasetId dataset, int cache, Func<CommitIndex> current, CancellationToken cancellationToken)
        {
            CommitIndexOpener opener = new(store, dataset, cache, current);

            foreach (BlobName name in await store.ListAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!name.Value.StartsWith(CommitIndexFormat.Prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                opener._listed.Add(name.Value);
                int dot = name.Value.LastIndexOf('.');

                if (dot > 0 && long.TryParse(name.Value.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out long sequence))
                {
                    opener.Sequence = Math.Max(opener.Sequence, sequence);
                }

                CommitSegment? segment = await CommitIndexFormat.TryLoadAsync(store, name, dataset, cancellationToken).ConfigureAwait(false);

                if (segment is not null)
                {
                    if (!opener._candidates.TryGetValue(segment.From, out List<CommitSegment>? from))
                    {
                        from = [];
                        opener._candidates[segment.From] = from;
                    }

                    from.Add(segment);
                }
            }

            foreach (List<CommitSegment> from in opener._candidates.Values)
            {
                // The furthest-reaching first.
                from.Sort((a, b) => b.To.CompareTo(a.To));
            }

            return opener;
        }

        /// <summary>The next closed commit of the scan.</summary>
        internal async ValueTask AddAsync(ScannedCommit commit, long bodiesAfter, CancellationToken cancellationToken)
        {
            long p = ++_position;

            if (commit.Header.Kind == CommitKind.Erasure)
            {
                throw new LogVerificationException(p, "Position " + p + " is an erasure commit, which needs erasure mode, and this version of Varve does not support erasure mode.");
            }

            if (commit.Header.Kind == CommitKind.Settings)
            {
                _folded = Fold(_folded, commit.Header);
                _settings.Add(new SettingsPoint(p, _folded));
            }

            if (p > bodiesAfter && commit.Full is { } full)
            {
                Bodies[p] = full;
            }

            _logBytes += commit.Bytes;
            CommitEntry entry = new(commit.Header.TimestampTicks, commit.HeaderHash, commit.Location, commit.Header.CanonicalCount, commit.Header.BlankCount, _logBytes);

            if (_building is null && _verifying is null && _candidates.TryGetValue(p - 1, out List<CommitSegment>? next) && next.Count > 0)
            {
                _verifying = next[0];
                next.RemoveAt(0);
            }

            if (_building is null && _verifying is not null)
            {
                if (_verifying.Read(p).SameAs(in entry))
                {
                    if (p == _verifying.To)
                    {
                        _chain.Add(_verifying);
                        _verifying = null;
                    }

                    return;
                }

                await RejectVerifyingAsync(p - 1, cancellationToken).ConfigureAwait(false);
            }

            _building ??= CommitIndex.Of([.. _chain], [], _current);
            await AppendAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The index at the scan's head, the blobs no longer named deleted.</summary>
        internal async ValueTask<CommitIndex> FinishAsync(CancellationToken cancellationToken)
        {
            if (_verifying is not null)
            {
                // The log ends inside a blob: the blob is another history's.
                await RejectVerifyingAsync(_position, cancellationToken).ConfigureAwait(false);
            }

            CommitIndex index = (_building ?? CommitIndex.Of([.. _chain], [], _current)).WithSettings([.. _settings]);
            HashSet<string> kept = new(StringComparer.Ordinal);

            foreach (CommitSegment segment in index.Segments)
            {
                kept.Add(segment.Blob.Name.Value);
            }

            foreach (List<CommitSegment> unused in _candidates.Values)
            {
                _rejected.AddRange(unused);
            }

            foreach (CommitSegment segment in _rejected)
            {
                // A blob reaching past the head is a newer writer's, or another
                // history's that a longer log will show to be stale: kept.
                if (segment.To > _position)
                {
                    _ahead.Add(segment.Blob.Name.Value);
                }

                segment.Blob.Release();
            }

            // Only what this open listed and found of no use: a blob written
            // since is not this open's to judge (ADR 0085).
            foreach (string name in _listed)
            {
                if (!kept.Contains(name) && !_ahead.Contains(name))
                {
                    await _store.DeleteAsync(new BlobName(name), cancellationToken).ConfigureAwait(false);
                }
            }

            return index;
        }

        // The blob being checked disagrees with the log after `verified`: the
        // entries it agreed on are taken from it into the rebuilt index, and
        // everything after is rebuilt from the log.
        private async ValueTask RejectVerifyingAsync(long verified, CancellationToken cancellationToken)
        {
            CommitSegment rejected = _verifying!;
            _verifying = null;
            _rejected.Add(rejected);
            _building = CommitIndex.Of([.. _chain], [], _current);

            for (long q = rejected.From + 1; q <= verified; q++)
            {
                await AppendAsync(rejected.Read(q), cancellationToken).ConfigureAwait(false);
            }
        }

        // Appends to the index being rebuilt, writing its oldest entries out
        // when it holds twice the cache.
        private async ValueTask AppendAsync(CommitEntry entry, CancellationToken cancellationToken)
        {
            CommitIndex building = _building!.Append(in entry, null);

            if (building.InMemory >= 2L * _cache)
            {
                long to = building.Head - _cache;
                BlobName name = CommitIndexFormat.Name(building.PagedTo, to, ++Sequence);
                await CommitIndexFormat.WriteAsync(_store, name, _dataset, building.PagedTo, to, building.Entry, cancellationToken).ConfigureAwait(false);
                // Gone already: another dataset on the same storage, numbering
                // its blobs from the same sequence, retired one of this name.
                // The entries stay in memory, and the next round pages them.
                if (await CommitIndexFormat.TryLoadAsync(_store, name, _dataset, cancellationToken).ConfigureAwait(false) is not { } segment)
                {
                    _building = building;
                    return;
                }

                building = building.WithPaged(segment);

                // Merged as maintenance merges, so that the blobs stay few.
                while (CommitMergeDue(building))
                {
                    CommitSegment older = building.Segments[^2];
                    CommitSegment newer = building.Segments[^1];
                    BlobName mergedName = CommitIndexFormat.Name(older.From, newer.To, ++Sequence);
                    await CommitIndexFormat.WriteAsync(_store, mergedName, _dataset, older.From, newer.To, q => (q <= older.To ? older : newer).Read(q), cancellationToken).ConfigureAwait(false);
                    if (await CommitIndexFormat.TryLoadAsync(_store, mergedName, _dataset, cancellationToken).ConfigureAwait(false) is not { } merged)
                    {
                        break;
                    }

                    building = building.WithMerged(merged);

                    // Only the opener holds them: the dataset does not exist yet,
                    // and a reader another dataset has of the same name keeps
                    // reading what it has open (the storage contract).
                    foreach (CommitSegment replaced in new[] { older, newer })
                    {
                        replaced.Blob.Release();
                        await _store.DeleteAsync(replaced.Blob.Name, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            _building = building;
        }
    }
}
