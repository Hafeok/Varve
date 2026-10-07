// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// The delta of a bulk commit as a validator reads it: two quad sources over
/// the run on disk, never an array (ADR 0077).
/// </summary>
[Contract(typeof(BulkLoadValidatorsScanTheDeltaOnDisk.ValidatorsSeeTheBulkDeltaOnDisk), Role = "a bulk commit's delta, read where it lies")]
public sealed class BulkDelta
{
    internal BulkDelta(IQuadSource asserted, IQuadSource retracted, QuadCount count)
    {
        Asserted = asserted;
        Retracted = retracted;
        Count = count;
    }

    /// <summary>The quads the commit asserts, in the proposed state's handles.</summary>
    public IQuadSource Asserted { get; }

    /// <summary>The quads the commit retracts, in the proposed state's handles.</summary>
    public IQuadSource Retracted { get; }

    /// <summary>How many quads the commit asserts and retracts.</summary>
    public QuadCount Count { get; }

    /// <summary>The delta as an array, in memory: what a validator that knows no better gets.</summary>
    public QuadDelta Materialise()
    {
        return QuadDelta.Create([.. All(Asserted)], [.. All(Retracted)]);

        static List<Quad> All(IQuadSource source)
        {
            List<Quad> quads = [];
            using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

            while (cursor.MoveNext())
            {
                quads.Add(cursor.Current);
            }

            return quads;
        }
    }
}

/// <summary>Bulk loads (ADRs 0076, 0077, 0081).</summary>
public sealed partial class Dataset
{
    private IndexVersion? _bulkVersion;

    /// <summary>
    /// Starts a bulk load: writes the memtable to <c>derived/</c> if it holds
    /// anything, then holds the sequencer — every other commit waits — until
    /// the load commits or is disposed. The load is computed against the head
    /// as it stands now.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">In a browser, which has no thread for the load to block while it writes.</exception>
    /// <exception cref="DatasetUnavailableException">The dataset is in the failed state.</exception>
    public async ValueTask<BulkLoad> BeginBulkLoadAsync(BulkLoadOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsBrowser())
        {
            throw new PlatformNotSupportedException("A bulk load writes its sort buffer from the thread that feeds it, which a browser cannot block (ADR 0081).");
        }

        options ??= new BulkLoadOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MemoryBytes.Value, 16L << 20, nameof(options));

        while (true)
        {
            if (LeadingBlobRuns(_state.Index) != _state.Index.Runs.Length)
            {
                await _maintenanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    await FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _maintenanceGate.Release();
                }
            }

            await EnterSequencerAsync(cancellationToken).ConfigureAwait(false);
            State state = _state;

            if (state.Failed is not null || _broken is not null)
            {
                _sequencer.Release();
                throw new DatasetUnavailableException(state.Failed ?? _broken!);
            }

            if (LeadingBlobRuns(state.Index) == state.Index.Runs.Length && state.Index.TryAcquire())
            {
                _bulkVersion = state.Index;
                TermView terms = state.TermsAt(_dictionary, state.Index.Runs, state.Head);
                return new BulkLoad(this, state.Index, terms, state.Head, _storage.Derived, options);
            }

            _sequencer.Release();
        }
    }

    /// <summary>Called once by the load when it ends, committed or not.</summary>
    internal void EndBulkLoad()
    {
        Interlocked.Exchange(ref _bulkVersion, null)?.Release();
        _sequencer.Release();
    }

    /// <summary>
    /// The bulk commit, under the sequencer the load holds: the header, the
    /// delta run, the validators over it, the records, and the new state.
    /// </summary>
    internal async ValueTask<CommitResult> CommitBulkAsync(BulkCommit commit, TermSection terms, ulong agent, ulong cause, ulong scope, CancellationToken cancellationToken)
    {
        State state = _state;
        long head = commit.Head;
        long next = head + 1;
        byte[] previous = head == 0 ? LogFormat.Genesis() : state.Commits.Entry(head).HeaderHash();
        long now = _options.Clock.GetUtcNow().UtcTicks;
        long timestamp = Math.Max(now, head == 0 ? long.MinValue : state.Commits.Entry(head).TimestampTicks);
        byte[] content = await commit.ContentHashAsync(cancellationToken).ConfigureAwait(false);

        CommitHeader header = new(CommitKind.Data, next, timestamp, agent, cause, scope, commit.CanonicalAfter, commit.BlankAfter, 0, [], [], previous, content);
        byte[] headerBytes = LogFormat.EncodeHeader(in header);
        byte[] headerHash = SHA256.HashData(headerBytes);

        BlobName runName = new(RunPrefix + head.ToString("D20", CultureInfo.InvariantCulture) + "-" + next.ToString("D20", CultureInfo.InvariantCulture)
            + "." + Interlocked.Increment(ref _runSequence).ToString(CultureInfo.InvariantCulture));
        await DerivedFormat.WriteRunAsync(_storage.Derived, runName, DerivedFormat.KindRun, Id, head, next, headerHash, commit.SectionAsync, [terms], commit.BlankAfter, cancellationToken).ConfigureAwait(false);
        await commit.DropOrderAsync(cancellationToken).ConfigureAwait(false);
        LoadedRun delta = await DerivedFormat.TryLoadAsync(_storage.Derived, runName, Id, DerivedFormat.KindRun, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A bulk load's delta run does not read back.");

        try
        {
            if (_options.Validators.Count > 0)
            {
                List<ulong> attachments = [];
                Run[] proposedRuns = [.. commit.Version.Runs, delta.Run];
                TermView view = new(_dictionary, proposedRuns, commit.CanonicalAfter, commit.BlankAfter);
                IQuadSource proposed = new IndexSource(new IndexVersion(next, proposedRuns, proposedRuns.Length), view);
                BulkDelta bulk = new(
                    new IndexSource(new IndexVersion(next, [Halves(delta.Run, asserted: true)], 1), view),
                    new IndexSource(new IndexVersion(next, [Halves(delta.Run, asserted: false)], 1), view),
                    new QuadCount(delta.Run.Asserted(IndexOrder.Spog).Count + delta.Run.Retracted(IndexOrder.Spog).Count));

                foreach (ICommitValidator validator in _options.Validators)
                {
                    ValidationVerdict verdict = validator.Validate(proposed, bulk);

                    if (!verdict.IsAccepted)
                    {
                        delta.Run.Blob!.Release();
                        await _storage.Derived.DeleteAsync(runName, cancellationToken).ConfigureAwait(false);
                        return CommitResult.Rejected(head, verdict.Report);
                    }

                    if (verdict.Attachment is not null)
                    {
                        attachments.Add(view.TryInternalise(verdict.Attachment, out TermHandle handle)
                            ? handle.Value
                            : throw new InvalidOperationException("A bulk commit's attachment must be a term the proposed state holds (ADR 0081)."));
                    }
                }

                if (attachments.Count > 0)
                {
                    header = new CommitHeader(CommitKind.Data, next, timestamp, agent, cause, scope, commit.CanonicalAfter, commit.BlankAfter, 0, [.. attachments], [], previous, content);
                    headerBytes = LogFormat.EncodeHeader(in header);
                    headerHash = SHA256.HashData(headerBytes);
                    delta.Run.Blob!.Release();
                    await DerivedFormat.RenameToAsync(_storage.Derived, runName, headerHash, cancellationToken).ConfigureAwait(false);
                    delta = await DerivedFormat.TryLoadAsync(_storage.Derived, runName, Id, DerivedFormat.KindRun, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("A bulk load's delta run does not read back.");
                }
            }

            CommitLocation location;

            try
            {
                location = await _writer.AppendStreamAsync(CommitKind.Data, next, previous, commit.BodyAsync(cancellationToken), headerBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                _broken = "An append failed; reopen the dataset to recover.";
                throw;
            }

            CommitEntry info = new(header.TimestampTicks, headerHash, location, header.CanonicalCount, header.BlankCount, state.LogBytesAt(head) + _writer.LastCommitBytes);
            Run[] runs = [.. state.Index.Runs, delta.Run];
            IndexVersion index = new(next, runs, runs.Length);
            delta = null!;
            _state = new State(next, state.Commits.Append(in info, null), index, state.Checkpoints, null);
            Signal();
            await PersistStateAsync(index, cancellationToken).ConfigureAwait(false);

            if (MaintenanceDue(_state))
            {
                StartMaintenance();
            }

            return CommitResult.Committed(next);
        }
        catch
        {
            if (delta is not null)
            {
                delta.Run.Blob!.Release();
                await _storage.Derived.DeleteAsync(runName, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }

    // One half of a run as a run of assertions: what a validator reads as δ's asserted or retracted quads.
    private static Run Halves(Run run, bool asserted)
    {
        KeySection[] sections = new KeySection[Orders.Count];
        KeySection[] none = new KeySection[Orders.Count];

        for (int order = 0; order < Orders.Count; order++)
        {
            sections[order] = asserted ? run.Asserted((IndexOrder)order) : run.Retracted((IndexOrder)order);
            none[order] = KeySection.Empty;
        }

        return new Run(sections, none, run.From, run.To, run.Terms);
    }
}
