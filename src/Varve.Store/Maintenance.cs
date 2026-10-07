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
/// When a dataset writes checkpoints on its own (ADR 0078): every so many
/// commits, or every so many bytes of log, since the newest checkpoint,
/// whichever comes first, as part of its maintenance — so only when
/// <see cref="DatasetOptions.Maintenance"/> runs it, or the caller runs
/// <see cref="Dataset.MaintainAsync"/>. A checkpoint bounds what an as-of read
/// and an open replay: the log distance to the nearest one (R2).
/// </summary>
public sealed class CheckpointPolicy
{
    /// <summary>No checkpoint is written but those asked for. The default.</summary>
    public static CheckpointPolicy Never { get; } = new();

    /// <summary>A checkpoint at the head once it is this many commits past the newest.</summary>
    public int? EveryCommits { get; init; }

    /// <summary>A checkpoint at the head once this many bytes of log follow the newest.</summary>
    public ByteCount? EveryLogBytes { get; init; }

    /// <summary>How many checkpoints the policy keeps, dropping the oldest; zero keeps all.</summary>
    public int Keep { get; init; }
}

/// <summary>Who runs a dataset's maintenance on <c>derived/</c> (ADR 0070, ADR 0042's amendment).</summary>
public enum MaintenanceMode
{
    /// <summary>The caller does, through <see cref="Dataset.MaintainAsync"/>; nothing runs on its own.</summary>
    Off,

    /// <summary>The dataset does, on a task it owns, off the sequencer; a commit never waits for it.</summary>
    Background,
}

/// <summary>
/// Maintenance of the default projection's persisted state: flushing the
/// memtable to a disk run, merging disk runs in tiers, and loading what was
/// persisted when the dataset opens (ADR 0070).
/// </summary>
/// <remarks>
/// <para>
/// **Off the sequencer, never blocking a commit.** A round reads the current
/// version, does its I/O with no lock held, and takes the sequencer only to
/// publish: a flush first freezes the memtable runs it will write, so the
/// commits that arrive meanwhile merge only newer runs; a disk merge replaces
/// runs only maintenance changes. A result whose runs are no longer current —
/// the projection was rebuilt meanwhile — is discarded.
/// </para>
/// <para>
/// **The persisted state is one blob**, <c>index/state</c>, naming the disk
/// runs and the position they reach, written after a new run is in place and
/// before the runs it replaced are deleted (ADR 0072). A crash between any two
/// steps leaves either the old state, whose runs still exist, or the new one;
/// runs no state names are deleted on open.
/// </para>
/// </remarks>
[DesignDecision(typeof(TheStorageEngineIsOurOwn.MaintenanceOffTheSequencer), Scope = ExceptionScope.Boundary)]
public sealed partial class Dataset
{
    private const string RunPrefix = "index/runs/";

    private static readonly BlobName StateName = new("index/state");

    private readonly SemaphoreSlim _maintenanceGate = new(1, 1);
    private readonly Lock _maintenanceLock = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<BlobName> _closedRuns = new();
    private Task? _maintenanceTask;
    private bool _maintenanceAgain;
    private Exception? _maintenanceFailure;
    private long _runSequence;

    /// <summary>
    /// Runs maintenance until none is due: flushes the memtable to
    /// <c>derived/</c> when it holds more than <see cref="DatasetOptions.MemtableLimit"/>
    /// quads, and merges disk runs in tiers. For hosts whose
    /// <see cref="DatasetOptions.Maintenance"/> is <see cref="MaintenanceMode.Off"/>;
    /// with it on, this waits for the background round and runs another.
    /// </summary>
    public async ValueTask MaintainAsync(CancellationToken cancellationToken = default)
    {
        await _maintenanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            while (await MaintainOnceAsync(cancellationToken).ConfigureAwait(false))
            {
            }
        }
        finally
        {
            _maintenanceGate.Release();
        }
    }

    private bool MaintenanceDue(State state) =>
        state.Index.MemtableCount >= _options.MemtableLimit.Value && state.Index.Runs.Length > LeadingBlobRuns(state.Index)
        || DiskMergeDue(state.Index)
        || CheckpointDue(state)
        || CommitPagingDue(state)
        || CommitMergeDue(state.Commits);

    private static int LeadingBlobRuns(IndexVersion index)
    {
        int count = 0;

        while (count < index.Runs.Length && !index.Runs[count].InMemory)
        {
            count++;
        }

        return count;
    }

    private static bool DiskMergeDue(IndexVersion index)
    {
        int disk = LeadingBlobRuns(index);
        return disk >= 2 && index.Runs[disk - 1].Size * 4 >= index.Runs[disk - 2].Size;
    }

    private void StartMaintenance()
    {
        if (_options.Maintenance != MaintenanceMode.Background)
        {
            return;
        }

        lock (_maintenanceLock)
        {
            if (_closing.IsCancellationRequested)
            {
                return;
            }

            if (_maintenanceTask is { IsCompleted: false })
            {
                _maintenanceAgain = true;
                return;
            }

            _maintenanceTask = Task.Run(RunMaintenanceAsync);
        }
    }

    private async Task RunMaintenanceAsync()
    {
        try
        {
            bool again;

            do
            {
                await MaintainAsync(_closing.Token).ConfigureAwait(false);

                lock (_maintenanceLock)
                {
                    again = _maintenanceAgain && !_closing.IsCancellationRequested;
                    _maintenanceAgain = false;
                }
            }
            while (again);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            // The log is untouched; derived/ is behind, and opening replays.
            // DisposeAsync reports it.
            _maintenanceFailure = error;
        }
    }

    private async ValueTask<Exception?> StopMaintenanceAsync()
    {
        Task? running;

        lock (_maintenanceLock)
        {
            _closing.Cancel();
            running = _maintenanceTask;
        }

        if (running is not null)
        {
            await running.ConfigureAwait(false);
        }

        return _maintenanceFailure;
    }

    // One round: a flush if the memtable is full, else a disk merge if one is
    // due. True when it did something.
    private async ValueTask<bool> MaintainOnceAsync(CancellationToken cancellationToken)
    {
        await DeleteClosedRunsAsync(cancellationToken).ConfigureAwait(false);
        State state = _state;

        if (state.Failed is not null || _broken is not null)
        {
            return false;
        }

        IndexVersion index = state.Index;
        int disk = LeadingBlobRuns(index);

        if (index.MemtableCount >= _options.MemtableLimit.Value && index.Runs.Length > disk)
        {
            return await FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (DiskMergeDue(index))
        {
            return await MergeAsync(index, disk, cancellationToken).ConfigureAwait(false);
        }

        if (CommitPagingDue(state) && await PageCommitsAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        if (CommitMergeDue(state.Commits) && await MergeCommitsAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        if (CheckpointDue(state))
        {
            await CheckpointCoreAsync(state.Head, cancellationToken).ConfigureAwait(false);

            while (_options.Checkpoints.Keep > 0 && _state.Checkpoints.Length > _options.Checkpoints.Keep)
            {
                await DropCheckpointAsync(new Position(_state.Checkpoints[0].Position), cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        return false;
    }

    // Whether the policy asks for a checkpoint at the head: the newest is
    // that many commits, or that many bytes of log, behind it (ADR 0078).
    private bool CheckpointDue(State state)
    {
        CheckpointPolicy policy = _options.Checkpoints;
        long newest = state.Checkpoints.Length > 0 ? state.Checkpoints[^1].Position : 0;

        if (state.Head == newest || state.Head == 0)
        {
            return false;
        }

        return (policy.EveryCommits is int commits && state.Head - newest >= commits)
            || (policy.EveryLogBytes is ByteCount bytes && state.LogBytesAt(state.Head) - state.LogBytesAt(newest) >= bytes.Value);
    }

    // Freezes the memtable, writes it as one run, and swaps the run in.
    private async ValueTask<bool> FlushAsync(CancellationToken cancellationToken)
    {
        IndexVersion frozen;
        int disk;

        await EnterSequencerAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            frozen = _state.Index;
            disk = LeadingBlobRuns(frozen);

            if (_state.Failed is not null || frozen.Runs.Length == disk)
            {
                return false;
            }

            frozen = frozen.With(frozen.Runs, frozen.Runs.Length);
            _state = _state.WithIndex(frozen, _state.Failed);
        }
        finally
        {
            _sequencer.Release();
        }

        // Streamed from the frozen runs: a flush holds no merged copy of them.
        Run[] memtable = frozen.Runs[disk..];
        LoadedRun written = await WriteRunAsync(DerivedFormat.MergeOf(memtable, dropRetractions: disk == 0), memtable, cancellationToken).ConfigureAwait(false);
        return await SwapAsync(frozen.Runs, disk, frozen.Runs.Length, written, cancellationToken).ConfigureAwait(false);
    }

    // Merges the newest two disk runs into one on disk, streaming.
    private async ValueTask<bool> MergeAsync(IndexVersion index, int disk, CancellationToken cancellationToken)
    {
        Run older = index.Runs[disk - 2];
        Run newer = index.Runs[disk - 1];
        LoadedRun written = await WriteRunAsync(DerivedFormat.MergeOf([older, newer], dropRetractions: disk == 2), [older, newer], cancellationToken).ConfigureAwait(false);
        return await SwapAsync(index.Runs, disk - 2, disk, written, cancellationToken).ConfigureAwait(false);
    }

    // Writes the merge of adjacent runs, oldest first, keys and dictionary entries.
    private async ValueTask<LoadedRun> WriteRunAsync(IKeySource[] sources, Run[] runs, CancellationToken cancellationToken)
    {
        long from = runs[0].From;
        long to = runs[^1].To;
        BlobName name = new(RunPrefix + from.ToString("D20", CultureInfo.InvariantCulture) + "-" + to.ToString("D20", CultureInfo.InvariantCulture)
            + "." + Interlocked.Increment(ref _runSequence).ToString(CultureInfo.InvariantCulture));
        byte[] hash = _state.Commits.Entry(to).HeaderHash();

        await DerivedFormat.WriteRunAsync(_storage.Derived, name, DerivedFormat.KindRun, Id, from, to, hash, sources, TermsOf(runs), _state.Commits.Entry(to).BlankCount, cancellationToken).ConfigureAwait(false);

        return await DerivedFormat.TryLoadAsync(_storage.Derived, name, Id, DerivedFormat.KindRun, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A run just written to derived/ does not read back.");
    }

    // Replaces runs [first, end) of the version they were read from with the
    // written run, if they are still the current version's; persists the state;
    // then retires what was replaced.
    private async ValueTask<bool> SwapAsync(Run[] from, int first, int end, LoadedRun written, CancellationToken cancellationToken)
    {
        IndexVersion swapped;

        await EnterSequencerAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IndexVersion current = _state.Index;
            bool same = current.Runs.Length >= end;

            for (int i = first; same && i < end; i++)
            {
                same = ReferenceEquals(current.Runs[i], from[i]);
            }

            if (!same)
            {
                written.Run.Blob!.Release();
                await _storage.Derived.DeleteAsync(written.Run.Blob.Name, cancellationToken).ConfigureAwait(false);
                return false;
            }

            Run[] runs = new Run[current.Runs.Length - (end - first) + 1];
            Array.Copy(current.Runs, runs, first);
            runs[first] = written.Run;
            Array.Copy(current.Runs, end, runs, first + 1, current.Runs.Length - end);
            int frozen = Math.Max(first + 1, current.Frozen - (end - first) + 1);
            swapped = current.With(runs, frozen);
            _state = _state.WithIndex(swapped, _state.Failed);
        }
        finally
        {
            _sequencer.Release();
        }

        await PersistStateAsync(swapped, cancellationToken).ConfigureAwait(false);

        for (int i = first; i < end; i++)
        {
            await RetireRunAsync(from[i], cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    // A run the live projection no longer holds: its reference released, and
    // its blob deleted, if it was the projection's own (a checkpoint's stays),
    // once nothing reads it — at once if no view holds it, else when the last
    // view that does is disposed and the next round runs. Run names carry a
    // sequence number and are never reused, so a deferred delete cannot
    // remove a newer run.
    private async ValueTask RetireRunAsync(Run run, CancellationToken cancellationToken)
    {
        if (run.Blob is not { } blob)
        {
            return;
        }

        if (blob.Name.Value.StartsWith(RunPrefix, StringComparison.Ordinal))
        {
            blob.Retire(_closedRuns.Enqueue);
        }
        else
        {
            blob.Release();
        }

        await DeleteClosedRunsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the retired runs whose last reader has let go.</summary>
    private async ValueTask DeleteClosedRunsAsync(CancellationToken cancellationToken)
    {
        while (_closedRuns.TryDequeue(out BlobName name))
        {
            await _storage.Derived.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>After the projection is rebuilt: the old version's runs retired, and the state rewritten.</summary>
    private async ValueTask RetireAsync(IndexVersion previous, IndexVersion current, CancellationToken cancellationToken)
    {
        await PersistStateAsync(current, cancellationToken).ConfigureAwait(false);

        foreach (Run run in previous.Runs)
        {
            await RetireRunAsync(run, cancellationToken).ConfigureAwait(false);
        }
    }

    // The state names the version's leading runs on blobs; with none, there is
    // no persisted state.
    private async ValueTask PersistStateAsync(IndexVersion index, CancellationToken cancellationToken)
    {
        int disk = LeadingBlobRuns(index);

        if (disk == 0)
        {
            await _storage.Derived.DeleteAsync(StateName, cancellationToken).ConfigureAwait(false);
            return;
        }

        List<StateEntry> runs = [];

        for (int i = 0; i < disk; i++)
        {
            runs.Add(new StateEntry(index.Runs[i].Blob!.Name, index.Runs[i].From, index.Runs[i].To));
        }

        long position = index.Runs[disk - 1].To;
        await DerivedFormat.WriteStateAsync(
            _storage.Derived, StateName, Id, position, _state.Commits.Entry(position).HeaderHash(), Interlocked.Read(ref _runSequence) + 1, runs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The default projection's base on open: the persisted state when it names
    /// this log and is no older than the newest checkpoint, else the newest
    /// checkpoint, else nothing. Runs no chosen state names are deleted.
    /// </summary>
    private async ValueTask<IndexVersion> LoadIndexAsync(State state, Checkpoint[] checkpoints, CancellationToken cancellationToken)
    {
        Checkpoint? newest = checkpoints.Length > 0 ? checkpoints[^1] : null;
        long floor = newest?.Position ?? 0;
        HashSet<string> kept = new(StringComparer.Ordinal);
        IndexVersion? loaded = null;

        var persisted = await DerivedFormat.TryReadStateAsync(_storage.Derived, StateName, Id, cancellationToken).ConfigureAwait(false);

        if (persisted is { } read
            && read.Header.To >= Math.Max(1, floor)
            && read.Header.To <= state.Head
            && state.Commits.Entry(read.Header.To).HashEquals(read.Header.ToHash))
        {
            _runSequence = Math.Max(_runSequence, read.Sequence);
            loaded = await LoadRunsAsync(state, checkpoints, read.Runs, read.Header.To, cancellationToken).ConfigureAwait(false);

            if (loaded is not null)
            {
                foreach (StateEntry entry in read.Runs)
                {
                    kept.Add(entry.Name.Value);
                }
            }
        }

        if (loaded is null)
        {
            loaded = newest is not null && newest.Run.Blob!.TryAcquire()
                ? IndexVersion.FromBase(newest.Position, newest.Run)
                : IndexVersion.Empty;

            await _storage.Derived.DeleteAsync(StateName, cancellationToken).ConfigureAwait(false);
        }

        loaded = await AdoptRunsAsync(state, loaded, kept, cancellationToken).ConfigureAwait(false);

        foreach (BlobName name in await _storage.Derived.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            // A bulk load's spills, left by a crash during the load (ADR 0081).
            if (name.Value.StartsWith(SpillSpace.Prefix, StringComparison.Ordinal))
            {
                await _storage.Derived.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (name.Value.StartsWith(RunPrefix, StringComparison.Ordinal))
            {
                int dot = name.Value.LastIndexOf('.');

                if (dot > 0 && long.TryParse(name.Value.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out long sequence))
                {
                    _runSequence = Math.Max(_runSequence, sequence);
                }

                if (!kept.Contains(name.Value))
                {
                    await _storage.Derived.DeleteAsync(name, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return loaded;
    }

    // Runs that continue what was loaded, written before a crash took the
    // state that would have named them: a bulk load's delta run, published
    // before its commit and named in the state after it. A run whose header
    // starts where the index ends and whose end hash is the log's header there
    // is that commit's projection, as a state's run would be; adopting it is
    // what keeps a crash in that window from replaying the load in memory
    // (ADR 0081). The furthest-reaching such run is taken, until none is left.
    private async ValueTask<IndexVersion> AdoptRunsAsync(State state, IndexVersion loaded, HashSet<string> kept, CancellationToken cancellationToken)
    {
        IReadOnlyList<BlobName> names = await _storage.Derived.ListAsync(cancellationToken).ConfigureAwait(false);

        while (loaded.Position < state.Head)
        {
            string from = RunPrefix + loaded.Position.ToString("D20", CultureInfo.InvariantCulture) + "-";
            Run? adopted = null;
            string? adoptedName = null;

            List<string> candidates = [];

            foreach (BlobName name in names)
            {
                if (name.Value.StartsWith(from, StringComparison.Ordinal))
                {
                    candidates.Add(name.Value);
                }
            }

            candidates.Sort(StringComparer.Ordinal);
            candidates.Reverse();

            foreach (string candidate in candidates)
            {
                BlobName name = new(candidate);
                LoadedRun? run = await DerivedFormat.TryLoadAsync(_storage.Derived, name, Id, DerivedFormat.KindRun, cancellationToken).ConfigureAwait(false);

                if (run is not null
                    && run.Header.From == loaded.Position
                    && run.Header.To > run.Header.From
                    && run.Header.To <= state.Head
                    && state.Commits.Entry(run.Header.To).HashEquals(run.Header.ToHash)
                    && run.Run.Terms.From == state.CanonicalAt(run.Header.From)
                    && run.Run.Terms.To == state.CanonicalAt(run.Header.To))
                {
                    adopted = run.Run;
                    adoptedName = name.Value;
                    break;
                }

                run?.Run.Blob!.Release();
            }

            if (adopted is null)
            {
                break;
            }

            kept.Add(adoptedName!);
            Run[] runs = [.. loaded.Runs, adopted];
            loaded = new IndexVersion(adopted.To, runs, runs.Length);
        }

        return loaded;
    }

    // The runs a state names, each checked against the log; null if any is
    // missing, foreign or stale, and then nothing loaded is kept.
    private async ValueTask<IndexVersion?> LoadRunsAsync(State state, Checkpoint[] checkpoints, StateEntry[] entries, long position, CancellationToken cancellationToken)
    {
        List<Run> runs = [];
        long expected = 0;
        bool valid = entries.Length > 0;

        foreach (StateEntry entry in entries)
        {
            if (!valid || entry.From != expected || entry.To <= entry.From || entry.To > position)
            {
                valid = false;
                break;
            }

            Run? run = null;

            if (Checkpoint.TryParseName(entry.Name, out long at))
            {
                Checkpoint? checkpoint = Array.Find(checkpoints, c => c.Position == at);

                if (checkpoint is not null && entry.From == 0 && entry.To == at && checkpoint.Run.Blob!.TryAcquire())
                {
                    run = checkpoint.Run;
                }
            }
            else if (entry.Name.Value.StartsWith(RunPrefix, StringComparison.Ordinal))
            {
                LoadedRun? loaded = await DerivedFormat.TryLoadAsync(_storage.Derived, entry.Name, Id, DerivedFormat.KindRun, cancellationToken).ConfigureAwait(false);

                if (loaded is not null
                    && loaded.Header.From == entry.From
                    && loaded.Header.To == entry.To
                    && state.Commits.Entry(entry.To).HashEquals(loaded.Header.ToHash)
                    && loaded.Run.Terms.From == state.CanonicalAt(entry.From)
                    && loaded.Run.Terms.To == state.CanonicalAt(entry.To))
                {
                    run = loaded.Run;
                }
                else
                {
                    loaded?.Run.Blob!.Release();
                }
            }

            if (run is null)
            {
                valid = false;
                break;
            }

            runs.Add(run);
            expected = entry.To;
        }

        if (!valid || expected != position)
        {
            foreach (Run run in runs)
            {
                run.Blob!.Release();
            }

            return null;
        }

        return new IndexVersion(position, [.. runs], runs.Count);
    }
}
