// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// Checkpoints as extent sets (ADR 0138, ADR 0139). A checkpoint at a
/// position is the chain of sets from 0 to it, each a window's intervals; the
/// catalog names the chain. A new checkpoint above the newest adds a set for
/// the window after it; one inside a set splits that set; dropping one merges
/// its set into the next, or drops the newest set; the writer's maintenance
/// merges adjacent sets of one size (ADR 0158, ADR 0159). Every change writes
/// its sets, then swaps the catalog, then retires what the catalog no longer
/// names — so no catalog ever names a set that is not whole — and a retired
/// set is deleted once no reader record holds it (ADR 0160).
/// </summary>
public sealed partial class Dataset
{
    // Attempts at a set's name before a taken name is taken for a fault.
    private const int NameAttempts = 8;

    // Maintenance rounds between readings of the reader records while sets
    // wait for deletion: a poll, which decides how soon, never whether (ADR 0160).
    private const int RoundsBetweenPolls = 16;

    private readonly SemaphoreSlim _checkpointGate = new(1, 1);
    private readonly SemaphoreSlim _announceGate = new(1, 1);
    private readonly Lock _extentLock = new();

    // Sets the catalog no longer names, by name: retired with a view still
    // reading them (their sequence), and the version that stopped naming them.
    private readonly Dictionary<string, long> _openRetired = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _removedAt = new(StringComparer.Ordinal);

    // Sets nothing in this process reads and no catalog names, waiting for no
    // reader record to hold them: their sequence and the version that removed
    // them, long.MaxValue while a catalog in the store still names them.
    private readonly Dictionary<string, (long Sequence, long RemovedAt)> _unnamed = new(StringComparer.Ordinal);

    // Merges refused because their output could pass the store's single
    // request, by the first input's name: not tried again on the same inputs.
    private readonly HashSet<string> _refusedMerges = new(StringComparer.Ordinal);

    private long _extentSequence;
    private long _catalogVersion;
    private long _announcingNext;
    private (long Oldest, long Newest)? _announced;
    private bool _deletionDue;
    private int _roundsSincePoll;
    private volatile bool _merges = true;

    // Whether a present reader record reads no more than derived format 4
    // (ADR 0178): while one does, format-4 sets are not rewritten. Unknown —
    // so held — until the writer first reads the records; a storage with one
    // reader has no records, and this build reads format 5.
    private volatile bool _olderReaders = true;

    // The pins of the catalog the open began at, or last wrote (ADR 0139).
    private Dictionary<long, CatalogPin> _catalogPins = [];

    /// <summary>
    /// The catalog version this dataset read at open or last swapped in (ADR
    /// 0160): its next set sequence, which every swap advances.
    /// </summary>
    public CatalogVersion CatalogVersion => new(Interlocked.Read(ref _catalogVersion));

    /// <summary>
    /// What the store keeps for readers (ADR 0160, ADR 0162), as of the last
    /// time the writer read the reader records; none on a storage with one
    /// reader, or before the first reading.
    /// </summary>
    public ExtentRetention? Retention { get; private set; }

    /// <summary>Test seam: the extent sets of the current state, oldest first, as (from, to, bytes).</summary>
    internal (long From, long To, long Bytes)[] SetsForTests()
    {
        List<ExtentSet> sets = SetsOf(_state);
        (long, long, long)[] windows = new (long, long, long)[sets.Count];

        for (int i = 0; i < windows.Length; i++)
        {
            windows[i] = (sets[i].From, sets[i].To, sets[i].Bytes);
        }

        return windows;
    }

    /// <summary>Test seam: one background merge, if the policy has one due; true when it swapped one in.</summary>
    internal ValueTask<bool> MergeOnceForTestsAsync(CancellationToken cancellationToken = default) => MergeExtentsAsync(cancellationToken);

    /// <summary>Test seam: the reader records read, and every set no record holds deleted.</summary>
    internal async ValueTask DeleteUnnamedForTestsAsync(CancellationToken cancellationToken = default)
    {
        await DeleteClosedRunsAsync(cancellationToken).ConfigureAwait(false);
        await DeleteUnnamedSetsAsync(poll: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Test seam: whether each extent set of the current state, oldest first, is sought inside its window (ADR 0177).</summary>
    internal bool[] SeekableSetsForTests() => [.. SetsOf(_state).ConvertAll(s => s.Seekable)];

    /// <summary>Test seam: the extent sets of the current state, oldest first, for the layout statistics of ADR 0177.</summary>
    internal IReadOnlyList<ExtentSet> ExtentSetsForTests() => SetsOf(_state);

    /// <summary>Test seam: the names of the extent sets of the current state, oldest first.</summary>
    internal string[] SetNamesForTests() => [.. SetsOf(_state).ConvertAll(s => s.Name)];

    /// <summary>Test seam: the sets waiting for deletion.</summary>
    internal int UnnamedSetsForTests()
    {
        lock (_extentLock)
        {
            return _unnamed.Count;
        }
    }

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
    // like any derived file's (ADR 0072). On a storage with reader records the
    // catalog's version is announced first, and the catalog read again: no
    // extent of a version is read until the catalog read is the one announced
    // (ADR 0160). Sets nothing names are deleted at once on a storage with one
    // reader, and otherwise wait for no record to hold them.
    [DesignDecision(typeof(ExtentDeletionByReaderAnnouncements.AnnounceThenConfirm), Scope = ExceptionScope.Boundary)]
    private async ValueTask<Checkpoint[]> LoadExtentsAsync(State state, CancellationToken cancellationToken)
    {
        List<ExtentSet> chain = [];
        HashSet<string> named = new(StringComparer.Ordinal);
        HashSet<string> listed = new(StringComparer.Ordinal);
        var catalog = await ExtentCatalog.TryReadAsync(_storage.Derived, Id, cancellationToken).ConfigureAwait(false);

        if (_storage is IExtentReaders readers)
        {
            while (true)
            {
                long version = catalog?.Sequence ?? 0;
                await readers.AnnounceAsync(new CatalogVersion(version), new CatalogVersion(version), cancellationToken).ConfigureAwait(false);
                _announced = (version, version);
                var again = await ExtentCatalog.TryReadAsync(_storage.Derived, Id, cancellationToken).ConfigureAwait(false);

                if (SameCatalog(catalog, again))
                {
                    break;
                }

                catalog = again;
            }
        }

        if (catalog is { } read)
        {
            _extentSequence = read.Sequence;
            _catalogVersion = read.Sequence;

            foreach (CatalogEntry entry in read.Sets)
            {
                listed.Add(entry.Name);
            }

            foreach (CatalogEntry entry in read.Sets)
            {
                if (entry.To > state.Head)
                {
                    break;
                }

                ExtentSet? set = await ExtentSet.TryLoadAsync(_storage.Derived, entry.Name, Id, entry.From, entry.To, cancellationToken).ConfigureAwait(false);

                if (set is null
                    || !state.Commits.Knows(entry.To) || !state.Commits.Knows(entry.From)
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

        HashSet<string> unnamed = new(StringComparer.Ordinal);

        foreach (BlobName name in await _storage.Derived.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            long sequence = ExtentSet.SequenceOf(name.Value);

            if (sequence < 0)
            {
                continue;
            }

            _extentSequence = Math.Max(_extentSequence, sequence + 1);
            string set = name.Value[..(name.Value.IndexOf('/', ExtentSet.Prefix.Length) + 1)];

            if (named.Contains(set))
            {
                continue;
            }

            if (_storage is not IExtentReaders)
            {
                await _storage.Derived.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
            }
            else if (unnamed.Add(set))
            {
                // A set the store's catalog still names — past where this
                // log's chain ends — is removed by this writer's next swap.
                lock (_extentLock)
                {
                    _unnamed[set] = (sequence, listed.Contains(set) ? long.MaxValue : _catalogVersion);
                    _deletionDue = true;
                }
            }
        }

        return Chains(chain);
    }

    // Whether two reads of the catalog are one version: the same sequence,
    // position and sets.
    private static bool SameCatalog(
        (DerivedHeader Header, long Sequence, CatalogEntry[] Sets, CatalogCursor? Cursor)? first,
        (DerivedHeader Header, long Sequence, CatalogEntry[] Sets, CatalogCursor? Cursor)? second)
    {
        if (first is not { } a || second is not { } b)
        {
            return first is null && second is null;
        }

        if (a.Sequence != b.Sequence || a.Header.To != b.Header.To || a.Sets.Length != b.Sets.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Sets.Length; i++)
        {
            if (a.Sets[i] != b.Sets[i])
            {
                return false;
            }
        }

        return true;
    }

    // Closes sets written for a catalog that was never swapped in, and has
    // them deleted once nothing reads them: no catalog names them, so no
    // record holds them either.
    private void RetireUnpublished(List<ExtentSet> written)
    {
        foreach (ExtentSet set in written)
        {
            RetireSet(set, set.Sequence + 1);
        }
    }

    // A set the catalog stopped naming at removedAt: kept while a view of this
    // process reads it, then waiting for no reader record to hold it.
    private void RetireSet(ExtentSet set, long removedAt)
    {
        lock (_extentLock)
        {
            _openRetired[set.Name] = set.Sequence;
            _removedAt[set.Name] = removedAt;
        }

        set.Run.Blob!.Retire(_closedRuns.Enqueue);
    }

    // A set name closed since it was retired: no view of this process reads
    // it, and it waits for the deletion rule.
    private void SetClosed(string name)
    {
        lock (_extentLock)
        {
            _openRetired.Remove(name);
            long removedAt = _removedAt.Remove(name, out long at) ? at : long.MaxValue;
            _unnamed[name] = (ExtentSet.SequenceOf(name), removedAt);
            _deletionDue = true;
        }
    }

    /// <summary>
    /// Announces the catalog versions this process holds (ADR 0160): from the
    /// oldest a retired set still read could be named by, to the newest
    /// swapped in, or about to be. Written only when the interval changes.
    /// </summary>
    private async ValueTask AnnounceHeldAsync(CancellationToken cancellationToken)
    {
        if (_storage is not IExtentReaders readers)
        {
            return;
        }

        await _announceGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            long newest = Math.Max(Interlocked.Read(ref _catalogVersion), Interlocked.Read(ref _announcingNext));
            long oldest = Interlocked.Read(ref _catalogVersion);

            lock (_extentLock)
            {
                foreach (long sequence in _openRetired.Values)
                {
                    oldest = Math.Min(oldest, sequence + 1);
                }
            }

            if (_announced == (oldest, newest))
            {
                return;
            }

            try
            {
                await readers.AnnounceAsync(new CatalogVersion(oldest), new CatalogVersion(newest), cancellationToken).ConfigureAwait(false);
            }
            catch (ReaderForgottenException)
            {
                // The writer's record was forgotten: the writer reads only
                // what its own catalog names, so it announces again at once,
                // as a fresh record.
                await readers.AnnounceAsync(new CatalogVersion(oldest), new CatalogVersion(newest), cancellationToken).ConfigureAwait(false);
            }

            _announced = (oldest, newest);
        }
        finally
        {
            _announceGate.Release();
        }
    }

    /// <summary>
    /// Deletes the sets no catalog names that no reader record holds (ADR
    /// 0160): after the swap that removed them, the records are read, and a set
    /// is deleted only when no record's interval meets the versions that named
    /// it. On a storage with one reader, at once. A delete that fails is tried
    /// again at the next round.
    /// </summary>
    [DesignDecision(typeof(ExtentDeletionByReaderAnnouncements.SwapThenReadRecordsThenDelete), Scope = ExceptionScope.Boundary)]
    private async ValueTask DeleteUnnamedSetsAsync(bool poll, CancellationToken cancellationToken)
    {
        KeyValuePair<string, (long Sequence, long RemovedAt)>[] pending;

        lock (_extentLock)
        {
            if (_unnamed.Count == 0 || (!_deletionDue && !poll))
            {
                return;
            }

            _deletionDue = false;
            pending = [.. _unnamed];
        }

        IReadOnlyList<ReaderAnnouncement> records = [];
        IExtentReaders? readers = _storage as IExtentReaders;

        if (readers is not null)
        {
            if (!readers.DeletesExtents)
            {
                return;
            }

            // This process's own record first, so what it no longer reads is
            // not held back by it. A store that cannot be reached now only
            // postpones the deletion to a later round; it never fails the
            // swap that came before.
            try
            {
                await AnnounceHeldAsync(cancellationToken).ConfigureAwait(false);
                records = await readers.ReadAnnouncementsAsync(cancellationToken).ConfigureAwait(false);
                NoteOlderReaders(records);
            }
            catch (System.IO.IOException)
            {
                lock (_extentLock)
                {
                    _deletionDue = true;
                }

                return;
            }

            _roundsSincePoll = 0;
        }

        List<(string Name, long Sequence, long RemovedAt)> held = [];

        foreach (KeyValuePair<string, (long Sequence, long RemovedAt)> set in pending)
        {
            if (HeldBy(records, set.Value.Sequence, set.Value.RemovedAt))
            {
                held.Add((set.Key, set.Value.Sequence, set.Value.RemovedAt));
                continue;
            }

            try
            {
                foreach (BlobName part in ExtentSet.BlobsOf(set.Key))
                {
                    await _storage.Derived.DeleteAsync(part, cancellationToken).ConfigureAwait(false);
                }

                lock (_extentLock)
                {
                    _unnamed.Remove(set.Key);
                }
            }
            catch (System.IO.IOException)
            {
                lock (_extentLock)
                {
                    _deletionDue = true;
                }
            }
        }

        if (readers is not null)
        {
            await MeasureRetentionAsync(readers, records, held, cancellationToken).ConfigureAwait(false);
        }
    }

    [DesignDecision(typeof(ExtentDeletionByReaderAnnouncements.DeleteWhenNoRecordOverlaps), Scope = ExceptionScope.Boundary)]
    private static bool HeldBy(IReadOnlyList<ReaderAnnouncement> records, long sequence, long removedAt)
    {
        foreach (ReaderAnnouncement record in records)
        {
            if (record.Holds(new CatalogVersion(sequence), new CatalogVersion(removedAt)))
            {
                return true;
            }
        }

        return false;
    }

    // The figures readiness reports (ADR 0162): what the store still holds of
    // sets no catalog names, and what each record holds of it.
    private async ValueTask MeasureRetentionAsync(
        IExtentReaders readers, IReadOnlyList<ReaderAnnouncement> records, List<(string Name, long Sequence, long RemovedAt)> held, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, long> bytes = held.Count == 0
            ? new Dictionary<string, long>()
            : await readers.ExtentBytesAsync(cancellationToken).ConfigureAwait(false);
        long total = 0;

        foreach ((string name, _, _) in held)
        {
            total += bytes.TryGetValue(name, out long b) ? b : 0;
        }

        ReaderRetention[] retention = new ReaderRetention[records.Count];

        for (int i = 0; i < records.Count; i++)
        {
            long retained = 0;

            foreach ((string name, long sequence, long removedAt) in held)
            {
                if (records[i].Holds(new CatalogVersion(sequence), new CatalogVersion(removedAt)))
                {
                    retained += bytes.TryGetValue(name, out long b) ? b : 0;
                }
            }

            retention[i] = new ReaderRetention(records[i], retained);
        }

        Retention = new ExtentRetention(CatalogVersion, SetsOf(_state).Count, held.Count, total, retention);
    }

    // Once per round of maintenance: the record brought up to date, and the
    // sets waiting for deletion tried again when they are due, or every few
    // rounds while any wait — a poll (ADR 0160).
    [DesignDecision(typeof(ExtentDeletionByReaderAnnouncements.MaintenanceMayPoll), Scope = ExceptionScope.Boundary)]
    private async ValueTask MaintainExtentsAsync(CancellationToken cancellationToken)
    {
        if (_storage is not IExtentReaders { DeletesExtents: true })
        {
            return;
        }

        // A record that cannot be written now only delays a deletion: it is
        // written again next round, and the deletion waits for it. Before a
        // swap, by contrast, a refused announcement refuses the swap.
        try
        {
            await AnnounceHeldAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (System.IO.IOException)
        {
            return;
        }

        bool waiting;

        lock (_extentLock)
        {
            waiting = _unnamed.Count > 0;
        }

        await DeleteUnnamedSetsAsync(waiting && ++_roundsSincePoll >= RoundsBetweenPolls, cancellationToken).ConfigureAwait(false);
    }

    // The catalog's cursor (ADR 0139, CatalogRecordsTheLogCursor): where the
    // log continues after the newest set's end and the backend's slot there,
    // every set's end's commit index entry, and the settings commits up to
    // it. A pin the open read from the previous catalog is reused, so a
    // dataset opened at its cursor does not read the log below it to write
    // the next one.
    private async ValueTask<CatalogCursor?> CursorAsync(State state, List<ExtentSet> sets, CancellationToken cancellationToken)
    {
        if (sets.Count == 0)
        {
            return null;
        }

        CatalogPin[] pins = new CatalogPin[sets.Count];

        for (int i = 0; i < sets.Count; i++)
        {
            pins[i] = await PinAsync(state, sets[i].To, cancellationToken).ConfigureAwait(false);
        }

        CatalogPin last = pins[^1];
        long slot = _storage.Log is ISegmentCursor parts ? parts.SlotOf(new SegmentId(last.EndSegment), new ByteOffset(last.EndOffset)).Value : 0;
        SettingsPoint[] settings = Array.FindAll(state.Commits.Settings, point => point.Position <= last.Position);
        return new CatalogCursor(last.EndSegment, last.EndOffset, slot, pins, settings);
    }

    private async ValueTask<CatalogPin> PinAsync(State state, long position, CancellationToken cancellationToken)
    {
        if (_catalogPins.TryGetValue(position, out CatalogPin known))
        {
            return known;
        }

        CommitEntry entry = state.Commits.Entry(position);
        CommitLocation end = position < state.Head
            ? state.Commits.Entry(position + 1).Location
            : await LogReader.EndAsync(_storage.Log, Id, entry.Location, position, position == 1 ? LogFormat.Genesis() : state.Commits.Entry(position - 1).HeaderHash(), cancellationToken).ConfigureAwait(false);
        return new CatalogPin(position, entry, end.Segment, end.Offset);
    }

    private static Dictionary<long, CatalogPin> PinsOf(CatalogCursor cursor)
    {
        Dictionary<long, CatalogPin> pins = [];

        foreach (CatalogPin pin in cursor.Pins)
        {
            pins[pin.Position] = pin;
        }

        return pins;
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
    /// Writes a set under a fresh name, and again under the next when a
    /// create finds the name taken with other bytes — a superseded writer's
    /// (ADR 0158): names are never reused. What a taken attempt left is never
    /// named, and the deletion rule takes it.
    /// </summary>
    [DesignDecision(typeof(BackgroundExtentMerges.ATakenNameTakesTheNextSequence), Scope = ExceptionScope.Boundary)]
    private async ValueTask<string> WriteUnderFreshNameAsync(long from, long to, Func<string, ValueTask> write)
    {
        for (int attempt = 1; ; attempt++)
        {
            string name = NextSetName(from, to);

            try
            {
                await write(name).ConfigureAwait(false);
                return name;
            }
            catch (DerivedNameTakenException) when (attempt < NameAttempts)
            {
                lock (_extentLock)
                {
                    _unnamed[name] = (ExtentSet.SequenceOf(name), long.MaxValue);
                }
            }
        }
    }

    /// <summary>
    /// A set for the window <c>(from, to]</c> from the log: each commit's
    /// delta as events, paired into intervals (ADR 0138), and the entries the
    /// window allocated as its dictionary; then its graph-version index and
    /// its window's commit index (ADR 0161).
    /// </summary>
    private async ValueTask<ExtentSet> WriteSetFromLogAsync(State state, long from, long to, ushort version, CancellationToken cancellationToken)
    {
        // A quad's event takes at least a few bytes of log: the window's
        // bytes bound its events, and so what the sorts need to hold.
        long expected = (state.LogBytesAt(to) - state.LogBytesAt(from)) / 4;
        ExtentRowBuilder rows = new(_storage.Derived, "extents-" + Interlocked.Read(ref _extentSequence).ToString(CultureInfo.InvariantCulture), expected);
        List<Allocation> allocations = [];
        string name;

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
            GraphVersions graphs = await GraphsAtAsync(state, to, cancellationToken).ConfigureAwait(false);

            name = await WriteUnderFreshNameAsync(from, to, async attempt =>
            {
                await ExtentSet.WriteAsync(_storage.Derived, attempt, Id, from, to, at.HeaderHash(), at.BlankCount, rows.RowsAsync, [terms], cancellationToken, version).ConfigureAwait(false);
                await graphs.WriteAsync(_storage.Derived, ExtentSet.GraphsOf(attempt), Id, at.HeaderHash(), cancellationToken).ConfigureAwait(false);
                await CommitIndexFormat.WriteAsync(_storage.Derived, ExtentSet.CommitsOf(attempt), Id, from, to, state.Commits.Entry, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        finally
        {
            await rows.DropAsync(cancellationToken).ConfigureAwait(false);
        }

        return await LoadSetAsync(name, from, to, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adjacent sets as one, <c>(first.From, last.To]</c>, from their extents
    /// alone (ADR 0138, ADR 0158): each older window's open intervals and the
    /// next one's closing ones meet, and every other interval is kept as it
    /// is — a pure function of the inputs, which coalesces no history. Its
    /// graph-version index is the newest input's, and its commit index the
    /// inputs' end to end.
    /// </summary>
    [DesignDecision(typeof(BackgroundExtentMerges.MergeIsAPureFunctionOfItsInputs), Scope = ExceptionScope.Boundary)]
    private async ValueTask<ExtentSet> MergeSetsAsync(State state, List<ExtentSet> inputs, CancellationToken cancellationToken)
    {
        ushort version = VersionReplacing(inputs);
        ExtentSet first = inputs[0];
        ExtentSet newest = inputs[^1];

        IRowSource Combined(IndexOrder order)
        {
            IRowSource rows = inputs[0].Rows(order);

            for (int i = 1; i < inputs.Count; i++)
            {
                rows = new AdjacentRows(rows, inputs[i].Rows(order));
            }

            return rows;
        }

        ValueTask<IRowSource> Rows(IndexOrder order, long section, CancellationToken token) =>
            new(new SectionRows(new ClassifiedRows(Combined(order)), section));

        TermSection[] terms = new TermSection[inputs.Count];

        for (int i = 0; i < terms.Length; i++)
        {
            terms[i] = inputs[i].Run.Terms;
        }

        var graphs = await GraphVersions.TryReadAsync(_storage.Derived, newest.Graphs, Id, cancellationToken).ConfigureAwait(false);
        GraphVersions? rebuilt = graphs is null ? await GraphsAtAsync(state, newest.To, cancellationToken).ConfigureAwait(false) : null;
        List<CommitSegment> commits = await InputCommitsAsync(inputs, cancellationToken).ConfigureAwait(false);

        try
        {
            CommitEntry EntryAt(long position)
            {
                foreach (CommitSegment segment in commits)
                {
                    if (segment.From < position && position <= segment.To)
                    {
                        return segment.Read(position);
                    }
                }

                return state.Commits.Entry(position);
            }

            string name = await WriteUnderFreshNameAsync(first.From, newest.To, async attempt =>
            {
                await ExtentSet.WriteAsync(_storage.Derived, attempt, Id, first.From, newest.To, newest.ToHash, newest.BlankCount, Rows, terms, cancellationToken, version).ConfigureAwait(false);

                if (graphs is { } read)
                {
                    await read.Versions.WriteAsync(_storage.Derived, ExtentSet.GraphsOf(attempt), Id, read.Hash, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await rebuilt!.WriteAsync(_storage.Derived, ExtentSet.GraphsOf(attempt), Id, newest.ToHash, cancellationToken).ConfigureAwait(false);
                }

                await CommitIndexFormat.WriteAsync(_storage.Derived, ExtentSet.CommitsOf(attempt), Id, first.From, newest.To, EntryAt, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);

            return await LoadSetAsync(name, first.From, newest.To, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (CommitSegment segment in commits)
            {
                segment.Blob.Release();
            }
        }
    }

    // The inputs' own commit index blobs, each for its window; a set written
    // before them has none, and its entries come from the commit index.
    private async ValueTask<List<CommitSegment>> InputCommitsAsync(List<ExtentSet> inputs, CancellationToken cancellationToken)
    {
        List<CommitSegment> commits = [];

        foreach (ExtentSet input in inputs)
        {
            CommitSegment? segment = await CommitIndexFormat.TryLoadAsync(_storage.Derived, ExtentSet.CommitsOf(input.Name), Id, cancellationToken).ConfigureAwait(false);

            if (segment is null)
            {
                continue;
            }

            if (segment.From == input.From && segment.To == input.To)
            {
                commits.Add(segment);
            }
            else
            {
                segment.Blob.Release();
            }
        }

        return commits;
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
        await NoteOlderReadersAsync(cancellationToken).ConfigureAwait(false);
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

            try
            {
                if (position > end)
                {
                    ExtentSet set = await WriteSetFromLogAsync(state, end, position, _options.ExtentVersion, cancellationToken).ConfigureAwait(false);
                    written.Add(set);
                    sets.Add(set);
                }
                else
                {
                    int k = sets.FindIndex(s => s.From < position && position < s.To);
                    ExtentSet holding = sets[k];
                    ushort version = VersionReplacing([holding]);
                    ExtentSet first = await WriteSetFromLogAsync(state, holding.From, position, version, cancellationToken).ConfigureAwait(false);
                    written.Add(first);
                    ExtentSet second = await WriteSetFromLogAsync(state, position, holding.To, version, cancellationToken).ConfigureAwait(false);
                    written.Add(second);
                    sets[k] = first;
                    sets.Insert(k + 1, second);
                    replaced.Add(holding);
                }
            }
            catch
            {
                RetireUnpublished(written);
                throw;
            }

            await PublishSetsAsync(sets, written, replaced, replaced, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _checkpointGate.Release();
        }
    }

    /// <summary>Drops an extent checkpoint: its set merged into the next, or, the newest, dropped.</summary>
    private async ValueTask DropExtentCheckpointAsync(long position, CancellationToken cancellationToken)
    {
        await NoteOlderReadersAsync(cancellationToken).ConfigureAwait(false);
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
                ExtentSet merged = await MergeSetsAsync(state, [sets[k], sets[k + 1]], cancellationToken).ConfigureAwait(false);
                written.Add(merged);
                replaced.Add(sets[k + 1]);
                sets[k] = merged;
                sets.RemoveAt(k + 1);
            }

            await PublishSetsAsync(sets, written, replaced, written.Count > 0 ? replaced : null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _checkpointGate.Release();
        }
    }

    // Whether the policy has sets to merge now (ADR 0159): only the writer
    // merges, and only what the catalog it last swapped names.
    private bool ExtentMergeDue(State state) => _merges && _options.ExtentMerges.Enabled && NextMerge(SetsOf(state)) is not null;

    private (int First, int Count)? NextMerge(List<ExtentSet> sets)
    {
        if (sets.Count == 0 || _storage is IExtentReaders { DeletesExtents: false })
        {
            return null;
        }

        long[] bytes = new long[sets.Count];

        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = sets[i].Bytes;
        }

        // When the policy has nothing to merge, the oldest set an earlier
        // release wrote in format 4 is rewritten alone, as format 5: a merge
        // of one input, a pure function of it like any (ADR 0178).
        (int First, int Count)? chosen = (sets.Count >= 2 ? _options.ExtentMerges.Choose(bytes) : null) ?? Format4Set(sets);
        return chosen is { } c && _refusedMerges.Contains(sets[c.First].Name + "+" + c.Count.ToString(CultureInfo.InvariantCulture)) ? null : chosen;
    }

    // The version a set written in place of these is written in (ADR 0178):
    // format 4 while a present reader reads no more and one of them is
    // format 4 — a merge, a drop's merge or a split of the oldest sets keeps
    // their format, so such a reader's chain ends only at the newest sets —
    // and otherwise what this build writes.
    [DesignDecision(typeof(TheFormat5UpgradeAndTheSetCap.OlderReadersDecideRewrites), Scope = ExceptionScope.Boundary)]
    private ushort VersionReplacing(List<ExtentSet> replaced) =>
        _storage is IExtentReaders && _olderReaders && !replaced.TrueForAll(s => s.Seekable) ? DerivedFormat.Version : _options.ExtentVersion;

    // The oldest set not in the version this build writes, when this build
    // writes 5 and no reader that reads only format 4 is present (ADR 0178):
    // its chain would end at the rewritten set, the oldest, and it would
    // replay the whole log. New checkpoints and merges still write format 5,
    // so such a reader's chain ends only at the newest sets.
    [DesignDecision(typeof(TheFormat5UpgradeAndTheSetCap.MaintenanceRewritesFormat4Sets), Scope = ExceptionScope.Boundary)]
    private (int First, int Count)? Format4Set(List<ExtentSet> sets)
    {
        if (_options.ExtentVersion < DerivedFormat.ExtentVersion || (_storage is IExtentReaders && _olderReaders))
        {
            return null;
        }

        int k = sets.FindIndex(s => !s.Seekable);
        return k < 0 ? null : (k, 1);
    }

    /// <summary>
    /// Reads the reader records, when a format-4 set waits to be rewritten,
    /// and notes whether any present one reads no more than format 4 (ADR
    /// 0178). A store that cannot be reached leaves the note as it was.
    /// </summary>
    private async ValueTask NoteOlderReadersAsync(CancellationToken cancellationToken)
    {
        if (_storage is not IExtentReaders readers || SetsOf(_state).TrueForAll(s => s.Seekable))
        {
            return;
        }

        try
        {
            NoteOlderReaders(await readers.ReadAnnouncementsAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (System.IO.IOException)
        {
        }
    }

    private void NoteOlderReaders(IReadOnlyList<ReaderAnnouncement> records)
    {
        bool older = false;

        foreach (ReaderAnnouncement record in records)
        {
            older |= record.MaxDerived.Value < DerivedFormat.ExtentVersion;
        }

        _olderReaders = older;
    }

    /// <summary>
    /// One background merge (ADR 0158): the policy's adjacent sets, read and
    /// written holding neither the sequencer nor the checkpoint gate, then
    /// swapped in under the gate — only if every input is still in the chain,
    /// contiguous and in order, keeping every set added since. Otherwise it is
    /// abandoned, and what it wrote, never named, waits for the deletion rule.
    /// </summary>
    [DesignDecision(typeof(BackgroundExtentMerges.MergeRebasesOnTheCatalog), Scope = ExceptionScope.Boundary)]
    private async ValueTask<bool> MergeExtentsAsync(CancellationToken cancellationToken)
    {
        await NoteOlderReadersAsync(cancellationToken).ConfigureAwait(false);
        State state = _state;
        List<ExtentSet> sets = SetsOf(state);

        if (NextMerge(sets) is not { } chosen)
        {
            return false;
        }

        List<ExtentSet> inputs = sets.GetRange(chosen.First, chosen.Count);

        if (!FitsOneRequest(inputs))
        {
            _refusedMerges.Add(inputs[0].Name + "+" + inputs.Count.ToString(CultureInfo.InvariantCulture));
            return false;
        }

        // The inputs are held open while they are read: a drop meanwhile
        // retires them, and the merge then finds them gone from the chain.
        int acquired = 0;

        while (acquired < inputs.Count && inputs[acquired].Run.Blob!.TryAcquire())
        {
            acquired++;
        }

        ExtentSet merged;

        try
        {
            if (acquired < inputs.Count)
            {
                return false;
            }

            merged = await MergeSetsAsync(state, inputs, cancellationToken).ConfigureAwait(false);
        }
        catch (WriterSupersededException)
        {
            // Not the writer: a follower, or a superseded one. It never merges again.
            _merges = false;
            return false;
        }
        finally
        {
            for (int i = 0; i < acquired; i++)
            {
                inputs[i].Run.Blob!.Release();
            }
        }

        if (_options.BeforeMergeSwap is { } pause)
        {
            await pause(cancellationToken).ConfigureAwait(false);
        }

        await _checkpointGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            List<ExtentSet> current = SetsOf(_state);
            int k = current.FindIndex(s => ReferenceEquals(s, inputs[0]));
            bool same = k >= 0 && k + inputs.Count <= current.Count;

            for (int i = 0; same && i < inputs.Count; i++)
            {
                same = ReferenceEquals(current[k + i], inputs[i]);
            }

            if (!same)
            {
                RetireUnpublished([merged]);
                await DeleteClosedRunsAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            current.RemoveRange(k, inputs.Count);
            current.Insert(k, merged);

            try
            {
                await PublishSetsAsync(current, [merged], inputs, inputs, cancellationToken).ConfigureAwait(false);
            }
            catch (WriterSupersededException)
            {
                _merges = false;
                throw;
            }

            return true;
        }
        finally
        {
            _checkpointGate.Release();
        }
    }

    // Whether every blob of the merge of these sets fits one request of the
    // store's (ADR 0158): an order's merged extent is no larger than that
    // order's inputs together, nor its dictionary than theirs.
    [DesignDecision(typeof(BackgroundExtentMerges.MergeRefusedPastTheSinglePutLimit), Scope = ExceptionScope.Boundary)]
    private bool FitsOneRequest(List<ExtentSet> inputs)
    {
        if (_storage.Derived is not IDerivedStoreLimits limits)
        {
            return true;
        }

        // Each blob's own header and directory, and the rows a merge writes
        // where an open and a closing interval meet: a margin of a page each.
        const long margin = 1L << 20;

        for (int o = 0; o <= Orders.Count; o++)
        {
            long total = margin;

            foreach (ExtentSet input in inputs)
            {
                total += o < Orders.Count ? input.Orders[o].Run.Blob!.Blob.Length.Value : input.Terms.Run.Blob!.Blob.Length.Value;
            }

            if (total > limits.MaxBlobBytes.Value)
            {
                return false;
            }
        }

        return true;
    }

    // Swaps the catalog to the new chain, then the state's checkpoints under
    // the sequencer — and the projection's base runs, when they are the sets
    // replaced — then retires the sets the chain no longer holds. The
    // writer announces the version before swapping to it (ADR 0160). A swap
    // that fails — a superseded writer's (ADR 0136) — leaves the catalog and
    // the state as they were, and closes what was written.
    [DesignDecision(typeof(BackgroundExtentMerges.MergeWritesThenSwapsOnce), Scope = ExceptionScope.Boundary)]
    private async ValueTask PublishSetsAsync(List<ExtentSet> sets, List<ExtentSet> written, List<ExtentSet> replaced, List<ExtentSet>? rebase, CancellationToken cancellationToken)
    {
        CatalogEntry[] entries = new CatalogEntry[sets.Count];

        for (int i = 0; i < sets.Count; i++)
        {
            entries[i] = new CatalogEntry(sets[i].Name, sets[i].From, sets[i].To);
        }

        long position = sets.Count > 0 ? sets[^1].To : 0;
        byte[] hash = sets.Count > 0 ? sets[^1].ToHash : LogFormat.Genesis();
        CatalogCursor? cursor;

        // The catalog's version: past every set it names (ADR 0160).
        long version = Interlocked.Increment(ref _extentSequence);

        try
        {
            cursor = await CursorAsync(_state, sets, cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _announcingNext, version);
            await AnnounceHeldAsync(cancellationToken).ConfigureAwait(false);
            await ExtentCatalog.WriteAsync(_storage.Derived, Id, position, hash, version, entries, cursor, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            RetireUnpublished(written);
            throw;
        }

        Interlocked.Exchange(ref _catalogVersion, version);
        _catalogPins = cursor is null ? [] : PinsOf(cursor);

        lock (_extentLock)
        {
            // What the store's previous catalog named past this log's chain
            // is named by no version from this one on.
            foreach (KeyValuePair<string, (long Sequence, long RemovedAt)> set in new List<KeyValuePair<string, (long Sequence, long RemovedAt)>>(_unnamed))
            {
                if (set.Value.RemovedAt == long.MaxValue && !written.Exists(w => w.Name == set.Key))
                {
                    _unnamed[set.Key] = (set.Value.Sequence, version);
                    _deletionDue = true;
                }
            }
        }

        try
        {
            await EnterSequencerAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The catalog names them now, so they are closed and kept: the
            // next open reads them from the catalog.
            foreach (ExtentSet set in written)
            {
                set.Run.Blob!.Release();
            }

            throw;
        }

        IndexVersion? rebased = null;
        Run[] unbased = [];

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

            if (rebase is not null && Rebase(_state.Index, rebase, written) is { } swapped)
            {
                rebased = swapped.Index;
                unbased = swapped.Replaced;
                _state = _state.WithIndex(swapped.Index, _state.Failed);
            }
        }
        finally
        {
            _sequencer.Release();
        }

        foreach (ExtentSet set in replaced)
        {
            RetireSet(set, version);
        }

        if (rebased is not null)
        {
            await PersistStateAsync(rebased, cancellationToken).ConfigureAwait(false);

            foreach (Run run in unbased)
            {
                await RetireRunAsync(run, cancellationToken).ConfigureAwait(false);
            }
        }

        await DeleteClosedRunsAsync(cancellationToken).ConfigureAwait(false);
        StartMaintenance();
    }

    // The projection's runs with the sets a swap replaced, where they stand
    // side by side as its runs, replaced by the sets written in their place
    // over the same window (ADR 0158): a merge's set for its inputs, a split's
    // two sets for the one, a drop's merged set for its two. A read of the
    // present then merges the runs the catalog names at once, not after the
    // next open, and a replaced set is no longer held by the projection.
    [DesignDecision(typeof(BackgroundExtentMerges.MergeReplacesThePrefixOfTheProjection), Scope = ExceptionScope.Boundary)]
    private static (IndexVersion Index, Run[] Replaced)? Rebase(IndexVersion index, List<ExtentSet> replaced, List<ExtentSet> written)
    {
        if (replaced.Count == 0 || written.Count == 0 || replaced[0].From != written[0].From || replaced[^1].To != written[^1].To)
        {
            return null;
        }

        int first = Array.FindIndex(index.Runs, r => ReferenceEquals(r, replaced[0].Run));

        if (first < 0 || first + replaced.Count > index.Frozen)
        {
            return null;
        }

        for (int i = 1; i < replaced.Count; i++)
        {
            if (!ReferenceEquals(index.Runs[first + i], replaced[i].Run))
            {
                return null;
            }
        }

        int acquired = 0;

        while (acquired < written.Count && written[acquired].Run.Blob!.TryAcquire())
        {
            acquired++;
        }

        if (acquired < written.Count)
        {
            for (int i = 0; i < acquired; i++)
            {
                written[i].Run.Blob!.Release();
            }

            return null;
        }

        int end = first + replaced.Count;
        Run[] runs = new Run[index.Runs.Length - replaced.Count + written.Count];
        Array.Copy(index.Runs, runs, first);

        for (int i = 0; i < written.Count; i++)
        {
            runs[first + i] = written[i].Run;
        }

        Array.Copy(index.Runs, end, runs, first + written.Count, index.Runs.Length - end);
        int frozen = Math.Max(first + written.Count, index.Frozen - replaced.Count + written.Count);
        return (index.With(runs, frozen), index.Runs[first..end]);
    }

    /// <summary>
    /// The view of a position strictly inside a set's window: the sets before
    /// it, and the set read at the position. A format-5 set is sought there
    /// (ADR 0177): nothing is written, and a read costs its matches. A
    /// format-4 set, written by an earlier release and not yet rewritten by
    /// maintenance, is narrowed to <c>(From, position]</c> as a run (ADR
    /// 0138), written beside the projection's runs and deleted once the view
    /// lets go. The set's whole dictionary is read through the position's
    /// counters.
    /// </summary>
    [DesignDecision(typeof(TheFormat5UpgradeAndTheSetCap.AFormat4SetIsNarrowedNeverFailed), Scope = ExceptionScope.Boundary)]
    private async ValueTask<(Run[] Index, ExtentSeek? Seek, Run[] Terms, Action Release)?> InsideSetAsync(State state, long position, CancellationToken cancellationToken)
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

                if (holding.Seekable)
                {
                    return (chain.Runs[..k], new ExtentSeek(holding, position), chain.Runs, chain.Release);
                }

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

                return (index, null, chain.Runs, Release);
            }
            catch
            {
                chain.Release();
                throw;
            }
        }
    }

    /// <summary>
    /// The commits an as-of read at <paramref name="position"/> replays from
    /// the log (R2, ADR 0138): none at or below the newest extent set's end,
    /// where the intervals answer; otherwise those above the nearest
    /// checkpoint at or below it, or above position 0 when there is none.
    /// </summary>
    public long AsOfLogDistance(Position position)
    {
        State state = _state;
        List<ExtentSet> sets = SetsOf(state);

        if (sets.Count > 0 && position.Value <= sets[^1].To)
        {
            return 0;
        }

        return position.Value - (state.CheckpointAtOrBelow(position.Value)?.Position ?? 0);
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
