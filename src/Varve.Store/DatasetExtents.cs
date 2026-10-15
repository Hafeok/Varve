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

/// <summary>
/// Checkpoints as extent sets (ADR 0138, ADR 0139). A checkpoint at a
/// position is the chain of sets from 0 to it, each a window's intervals; the
/// catalog names the chain. A new checkpoint above the newest adds a set for
/// the window after it; one inside a set splits that set; dropping one merges
/// its set into the next, or drops the newest set. Every change writes its
/// sets, then swaps the catalog, then retires what the catalog no longer
/// names — so no catalog ever names a set that is not whole.
/// </summary>
public sealed partial class Dataset
{
    private readonly SemaphoreSlim _checkpointGate = new(1, 1);
    private long _extentSequence;

    // The full checkpoints a format-3 directory holds, and the catalog's
    // chain: a full one at a position a set ends at gives way to the set.
    private async ValueTask<Checkpoint[]> WithExtentsAsync(State state, Checkpoint[] full, CancellationToken cancellationToken)
    {
        Checkpoint[] extents = await LoadExtentsAsync(state, cancellationToken).ConfigureAwait(false);
        List<Checkpoint> all = [.. extents];

        foreach (Checkpoint checkpoint in full)
        {
            if (Array.Exists(extents, e => e.Position == checkpoint.Position))
            {
                checkpoint.Owned.Release();
            }
            else
            {
                all.Add(checkpoint);
            }
        }

        all.Sort((a, b) => a.Position.CompareTo(b.Position));
        return [.. all];
    }

    // The catalog's chain at open, each set checked against the log: a set
    // that is missing, foreign or stale ends the chain there, a cache miss
    // like any derived file's (ADR 0072). Sets nothing names are deleted —
    // on a store with followers, the backend keeps them for Extent
    // maintenance (ADR 0139).
    private async ValueTask<Checkpoint[]> LoadExtentsAsync(State state, CancellationToken cancellationToken)
    {
        List<ExtentSet> chain = [];
        HashSet<string> named = new(StringComparer.Ordinal);
        var catalog = await ExtentCatalog.TryReadAsync(_storage.Derived, Id, cancellationToken).ConfigureAwait(false);

        if (catalog is { } read)
        {
            _extentSequence = read.Sequence;

            foreach (CatalogEntry entry in read.Sets)
            {
                if (entry.To > state.Head)
                {
                    break;
                }

                ExtentSet? set = await ExtentSet.TryLoadAsync(_storage.Derived, entry.Name, Id, entry.From, entry.To, cancellationToken).ConfigureAwait(false);

                if (set is null
                    || !state.Commits.Entry(entry.To).HashEquals(set.ToHash)
                    || set.Run.Terms.From != state.CanonicalAt(entry.From)
                    || set.Run.Terms.To != state.CanonicalAt(entry.To)
                    || set.BlankCount != state.BlankAt(entry.To))
                {
                    set?.Run.Blob!.Release();
                    break;
                }

                chain.Add(set);
                named.Add(entry.Name);
            }
        }

        foreach (BlobName name in await _storage.Derived.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            long sequence = ExtentSet.SequenceOf(name.Value);

            if (sequence < 0)
            {
                continue;
            }

            _extentSequence = Math.Max(_extentSequence, sequence + 1);
            string set = name.Value[..(name.Value.IndexOf('/', ExtentSet.Prefix.Length) + 1)];

            if (!named.Contains(set))
            {
                await _storage.Derived.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
            }
        }

        return Chains(chain);
    }

    // A checkpoint per set: the chain up to and including it.
    private static Checkpoint[] Chains(List<ExtentSet> sets)
    {
        Checkpoint[] checkpoints = new Checkpoint[sets.Count];

        for (int i = 0; i < sets.Count; i++)
        {
            checkpoints[i] = new Checkpoint([.. sets.GetRange(0, i + 1)]);
        }

        return checkpoints;
    }

    // The extent sets of the current state, oldest first.
    private static List<ExtentSet> SetsOf(State state)
    {
        for (int i = state.Checkpoints.Length - 1; i >= 0; i--)
        {
            if (state.Checkpoints[i].Chain is { } chain)
            {
                return [.. chain];
            }
        }

        return [];
    }

    private string NextSetName(long from, long to) => ExtentSet.NameOf(from, to, Interlocked.Increment(ref _extentSequence) - 1);

    /// <summary>
    /// A set for the window <c>(from, to]</c> from the log: each commit's
    /// delta as events, paired into intervals (ADR 0138), and the entries the
    /// window allocated as its dictionary; then its graph-version index.
    /// </summary>
    private async ValueTask<ExtentSet> WriteSetFromLogAsync(State state, long from, long to, CancellationToken cancellationToken)
    {
        string name = NextSetName(from, to);
        // A quad's event takes at least a few bytes of log: the window's
        // bytes bound its events, and so what the sorts need to hold.
        long expected = (state.LogBytesAt(to) - state.LogBytesAt(from)) / 4;
        ExtentRowBuilder rows = new(_storage.Derived, "extents-" + (Interlocked.Read(ref _extentSequence) - 1).ToString(CultureInfo.InvariantCulture), expected);
        List<Allocation> allocations = [];

        try
        {
            for (long p = from + 1; p <= to; p++)
            {
                LoggedCommit commit = await ReadLoggedAsync(state, p, cancellationToken).ConfigureAwait(false);
                await rows.AddAsync(commit.Asserted, commit.Retracted, p, cancellationToken).ConfigureAwait(false);
                allocations.AddRange(commit.Allocations);
            }

            await rows.FinishAsync(cancellationToken).ConfigureAwait(false);
            TermSection terms = TermSection.Of(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(allocations), state.CanonicalAt(from));

            if (terms.To != state.CanonicalAt(to))
            {
                throw new LogVerificationException(to, "The dictionary's counters after position " + to + " disagree with its header.");
            }

            CommitEntry at = state.Commits.Entry(to);
            await ExtentSet.WriteAsync(_storage.Derived, name, Id, from, to, at.HeaderHash(), at.BlankCount, rows.RowsAsync, [terms], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await rows.DropAsync(cancellationToken).ConfigureAwait(false);
        }

        GraphVersions graphs = await GraphsAtAsync(state, to, cancellationToken).ConfigureAwait(false);
        await graphs.WriteAsync(_storage.Derived, ExtentSet.GraphsOf(name), Id, state.Commits.Entry(to).HeaderHash(), cancellationToken).ConfigureAwait(false);
        return await LoadSetAsync(name, from, to, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Two adjacent sets as one, <c>(older.From, newer.To]</c>, from their
    /// extents alone (ADR 0138): the older set's open intervals and the
    /// newer's closing ones meet. Its graph-version index is the newer's.
    /// </summary>
    private async ValueTask<ExtentSet> MergeSetsAsync(State state, ExtentSet older, ExtentSet newer, CancellationToken cancellationToken)
    {
        string name = NextSetName(older.From, newer.To);

        ValueTask<IRowSource> Rows(IndexOrder order, long section, CancellationToken token) =>
            new(new SectionRows(new ClassifiedRows(new AdjacentRows(older.Rows(order), newer.Rows(order))), section));

        await ExtentSet.WriteAsync(_storage.Derived, name, Id, older.From, newer.To, newer.ToHash, newer.BlankCount, Rows, [older.Run.Terms, newer.Run.Terms], cancellationToken).ConfigureAwait(false);

        if (await GraphVersions.TryReadAsync(_storage.Derived, newer.Graphs, Id, cancellationToken).ConfigureAwait(false) is { } graphs)
        {
            await graphs.Versions.WriteAsync(_storage.Derived, ExtentSet.GraphsOf(name), Id, graphs.Hash, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            GraphVersions rebuilt = await GraphsAtAsync(state, newer.To, cancellationToken).ConfigureAwait(false);
            await rebuilt.WriteAsync(_storage.Derived, ExtentSet.GraphsOf(name), Id, newer.ToHash, cancellationToken).ConfigureAwait(false);
        }

        return await LoadSetAsync(name, older.From, newer.To, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ExtentSet> LoadSetAsync(string name, long from, long to, CancellationToken cancellationToken) =>
        await ExtentSet.TryLoadAsync(_storage.Derived, name, Id, from, to, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("An extent set just written does not read back.");

    /// <summary>
    /// The checkpoint at a position as extents: a set for the window after
    /// the newest set, or the set that holds the position split in two.
    /// Written off the sequencer and published under it, like every
    /// checkpoint (ADR 0070).
    /// </summary>
    private async ValueTask CheckpointExtentsAsync(long position, CancellationToken cancellationToken)
    {
        await _checkpointGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            State state = _state;
            List<ExtentSet> sets = SetsOf(state);
            long end = sets.Count > 0 ? sets[^1].To : 0;
            List<ExtentSet> written = [];
            List<ExtentSet> replaced = [];

            if (position == end || sets.Exists(s => s.To == position))
            {
                return;
            }

            if (position > end)
            {
                ExtentSet set = await WriteSetFromLogAsync(state, end, position, cancellationToken).ConfigureAwait(false);
                written.Add(set);
                sets.Add(set);
            }
            else
            {
                int k = sets.FindIndex(s => s.From < position && position < s.To);
                ExtentSet holding = sets[k];
                ExtentSet first = await WriteSetFromLogAsync(state, holding.From, position, cancellationToken).ConfigureAwait(false);
                written.Add(first);
                ExtentSet second = await WriteSetFromLogAsync(state, position, holding.To, cancellationToken).ConfigureAwait(false);
                written.Add(second);
                sets[k] = first;
                sets.Insert(k + 1, second);
                replaced.Add(holding);
            }

            await PublishSetsAsync(sets, written, replaced, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _checkpointGate.Release();
        }
    }

    /// <summary>Drops an extent checkpoint: its set merged into the next, or, the newest, dropped.</summary>
    private async ValueTask DropExtentCheckpointAsync(long position, CancellationToken cancellationToken)
    {
        await _checkpointGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            State state = _state;
            List<ExtentSet> sets = SetsOf(state);
            int k = sets.FindIndex(s => s.To == position);

            if (k < 0)
            {
                return;
            }

            List<ExtentSet> written = [];
            List<ExtentSet> replaced = [sets[k]];

            if (k == sets.Count - 1)
            {
                sets.RemoveAt(k);
            }
            else
            {
                ExtentSet merged = await MergeSetsAsync(state, sets[k], sets[k + 1], cancellationToken).ConfigureAwait(false);
                written.Add(merged);
                replaced.Add(sets[k + 1]);
                sets[k] = merged;
                sets.RemoveAt(k + 1);
            }

            await PublishSetsAsync(sets, written, replaced, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _checkpointGate.Release();
        }
    }

    // Swaps the catalog to the new chain, then the state's checkpoints under
    // the sequencer, then retires the sets the chain no longer holds. A swap
    // that fails — a superseded writer's (ADR 0136) — leaves the catalog and
    // the state as they were, and closes what was written.
    private async ValueTask PublishSetsAsync(List<ExtentSet> sets, List<ExtentSet> written, List<ExtentSet> replaced, CancellationToken cancellationToken)
    {
        CatalogEntry[] entries = new CatalogEntry[sets.Count];

        for (int i = 0; i < sets.Count; i++)
        {
            entries[i] = new CatalogEntry(sets[i].Name, sets[i].From, sets[i].To);
        }

        long position = sets.Count > 0 ? sets[^1].To : 0;
        byte[] hash = sets.Count > 0 ? sets[^1].ToHash : LogFormat.Genesis();

        try
        {
            await ExtentCatalog.WriteAsync(_storage.Derived, Id, position, hash, Interlocked.Read(ref _extentSequence), entries, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            foreach (ExtentSet set in written)
            {
                set.Run.Blob!.Retire(_closedRuns.Enqueue);
            }

            throw;
        }

        await EnterSequencerAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            List<Checkpoint> checkpoints = [.. Array.FindAll(_state.Checkpoints, c => c.Chain is null && !sets.Exists(s => s.To == c.Position))];
            checkpoints.AddRange(Chains(sets));
            checkpoints.Sort((a, b) => a.Position.CompareTo(b.Position));

            // A full checkpoint at a position a set now ends at gives way to it.
            foreach (Checkpoint full in Array.FindAll(_state.Checkpoints, c => c.Chain is null && sets.Exists(s => s.To == c.Position)))
            {
                full.Owned.Release();
            }

            _state = _state.WithCheckpoints([.. checkpoints]);
        }
        finally
        {
            _sequencer.Release();
        }

        foreach (ExtentSet set in replaced)
        {
            set.Run.Blob!.Retire(_closedRuns.Enqueue);
        }

        await DeleteClosedRunsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The view of a position strictly inside a set's window: the sets before
    /// it, and the set narrowed to <c>(From, position]</c> as a run (ADR 0138),
    /// written beside the projection's runs and deleted once the view lets go.
    /// The set's whole dictionary is read through the position's counters.
    /// </summary>
    private async ValueTask<(Run[] Index, Run[] Terms, Action Release)?> InsideSetAsync(State state, long position, CancellationToken cancellationToken)
    {
        while (true)
        {
            List<ExtentSet> sets = SetsOf(state);
            int k = sets.FindIndex(s => s.From < position && position < s.To);

            if (k < 0)
            {
                return null;
            }

            Checkpoint chain = new([.. sets.GetRange(0, k + 1)]);

            if (!chain.TryAcquire())
            {
                state = _state;
                continue;
            }

            try
            {
                ExtentSet holding = sets[k];
                BlobName name = new(RunPrefix + "asof-" + position.ToString("D20", CultureInfo.InvariantCulture) + "." + Interlocked.Increment(ref _runSequence).ToString(CultureInfo.InvariantCulture));

                ValueTask<IKeySource> Section(int section, CancellationToken token) =>
                    new(new RowKeySource(new SectionRows(
                        new ClassifiedRows(new RestrictedRows(holding.Rows((IndexOrder)(section / 2)), holding.From, position)),
                        section % 2 == 0 ? ExtentSection.Open : ExtentSection.Close)));

                await DerivedFormat.WriteRunAsync(
                    _storage.Derived, name, DerivedFormat.KindRun, Id, holding.From, position, state.Commits.Entry(position).HeaderHash(),
                    Section, [TermSection.Empty(holding.Run.Terms.From)], state.BlankAt(position), cancellationToken).ConfigureAwait(false);

                LoadedRun narrowed = await DerivedFormat.TryLoadAsync(_storage.Derived, name, Id, DerivedFormat.KindRun, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("A narrowed extent just written does not read back.");

                Run[] index = new Run[k + 1];
                Array.Copy(chain.Runs, index, k);
                index[k] = narrowed.Run;

                void Release()
                {
                    narrowed.Run.Blob!.Retire(_closedRuns.Enqueue);
                    chain.Release();
                }

                return (index, chain.Runs, Release);
            }
            catch
            {
                chain.Release();
                throw;
            }
        }
    }

    // On dispose: the catalog's reference to each set.
    private static void ReleaseOwned(Checkpoint[] checkpoints)
    {
        foreach (Checkpoint checkpoint in checkpoints)
        {
            checkpoint.Owned.Release();
        }
    }
}
