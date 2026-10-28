// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Varve.Management.Model;
using Varve.Protocol.Model;
using Varve.Store;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Server;

/// <summary>
/// A dataset with a writer set (ADR 0180): its preferred writer and its
/// standbys. Placement is compared as events between the declarations this
/// instance applies, never as state; at start a member claims only by the
/// start rule; a standby tails on the interval, reads the writer record on
/// it, and claims in place a release its reason lets it race for.
/// </summary>
internal sealed partial class OpenDatasets
{
    /// <summary>Whether this instance is a warm standby of the dataset (ADR 0180 point 6), and whether it tails it at all.</summary>
    /// <summary>A dataset's local storage as readiness reports it: its last spill's failure, and the local caches turned off (ADR 0179 point 6).</summary>
    internal (Exception? Spill, LocalCache Cache) LocalHealthOf(DatasetName name)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(name.Value, out Entry? entry) && entry.State == DatasetState.Open
                ? (entry.Dataset?.SpillFailure, entry.Objects?.CacheOff ?? LocalCache.None)
                : (null, LocalCache.None);
        }
    }

    internal (bool Tailing, bool Warm) StandbyOf(DatasetName name)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(name.Value, out Entry? entry) && entry.Role == DatasetRole.Standby && entry.State == DatasetState.Open
                ? (true, entry.Warm)
                : (false, false);
        }
    }

    private async Task ConvergeMemberAsync(string name, Entry entry, DatasetDeclaration declared, Placement placement, bool fresh, CancellationToken cancellationToken)
    {
        (string? PlacedOn, System.Collections.Generic.IReadOnlyList<string> StandbyOn)? previous = entry.Applied;
        entry.Applied = (declared.PlacedOn, declared.StandbyOn);

        if (declared.Closed)
        {
            await ConvergeAsync(name, entry, DatasetRole.Follower, declared, placement, fresh, cancellationToken).ConfigureAwait(false);
            return;
        }

        bool holding = entry.State == DatasetState.Open && entry.Role == DatasetRole.Writer && !entry.Superseded;

        if (!placement.IsMember(declared))
        {
            // Out of the writer set: an ordinary follower. A holder taken out
            // of it abdicates for placement, and the preferred writer claims
            // on seeing the release (point 2).
            if (entry.State == DatasetState.Open && entry.Role == DatasetRole.Standby)
            {
                entry.Placed(DatasetRole.Follower);
                return;
            }

            await ConvergeAsync(name, entry, DatasetRole.Follower, declared, placement, fresh, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (holding && entry.Dataset is { AppendFailed: true })
        {
            // A batch failed against the store: the writer releases the epoch
            // for storage-error and stands by, so a warm standby claims rather
            // than the dataset waiting for a re-open (ADR 0179 point 6).
            ReconcileLog.AbdicatedForStorage(_logger, name);
            await SwitchAsync(name, entry, DatasetRole.Standby, wait: false, abdicate: ReleaseReason.StorageError, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (holding && entry.Dataset is { SpillBound: not SpillBound.None } unbounded)
        {
            // The commit that crossed the bound decided it (ADR 0179 point 6):
            // spills to the local disk fail, and the memtable passed
            // SpillBoundFactor times MemtableLimit or the memory load reached
            // the GC's high-load threshold. A standby with its own disk can
            // spill; an idle writer never gets here.
            ReconcileLog.AbdicatedForLocalDisk(_logger, name, unbounded.MemtableEntries.Value, unbounded.SpillBound == SpillBound.MemoryLoad ? "the memory load reached the GC's high-load threshold" : "the memtable passed " + Dataset.SpillBoundFactor + " times its limit");
            await SwitchAsync(name, entry, DatasetRole.Standby, wait: false, abdicate: ReleaseReason.LocalDiskError, cancellationToken).ConfigureAwait(false);
            return;
        }

        MemberState state = holding ? MemberState.Holding
            : entry.State == DatasetState.Open && entry.Role == DatasetRole.Writer ? MemberState.Superseded
            : entry.State == DatasetState.Open && entry.Role == DatasetRole.Standby ? MemberState.Standby
            : MemberState.Away;

        // The record is read only where the decision needs it: a holder acts on events alone.
        WriterRecord? record = state is MemberState.Standby or MemberState.Away ? await _objects!.ReadWriterAsync(name, cancellationToken).ConfigureAwait(false) : null;
        RecordView? seen = record is null ? null : new RecordView(record.Holder.Value, record.Released);

        if (state == MemberState.Standby)
        {
            await TailAsync(name, entry, cancellationToken).ConfigureAwait(false);

            if (entry.Dataset is { ReopenDue: true })
            {
                // Not warm while it re-opens (point 8).
                ReconcileLog.Reopened(_logger, name);
                await SwitchAsync(name, entry, DatasetRole.Standby, wait: false, abdicate: null, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        MemberStep step = Decide(previous, declared, placement, state, seen, entry.Warm, entry.Started);
        entry.Started = true;

        switch (step)
        {
            case MemberStep.Abdicate:
                await SwitchAsync(name, entry, DatasetRole.Standby, wait: false, abdicate: ReleaseReason.Placement, cancellationToken).ConfigureAwait(false);
                break;
            case MemberStep.Rejoin:
                // Superseded: it rejoins as a standby, opened again, and is
                // eligible to claim once it is warm (ADR 0179 point 10).
                ReconcileLog.Rejoined(_logger, name);
                await SwitchAsync(name, entry, DatasetRole.Standby, wait: false, abdicate: null, cancellationToken).ConfigureAwait(false);
                break;
            case MemberStep.Start:
                await SwitchAsync(name, entry, DatasetRole.Writer, wait: true, abdicate: null, cancellationToken).ConfigureAwait(false);
                break;
            case MemberStep.Open:
                await SwitchAsync(name, entry, DatasetRole.Writer, wait: false, abdicate: null, cancellationToken).ConfigureAwait(false);
                break;
            case MemberStep.Stand:
                await SwitchAsync(name, entry, DatasetRole.Standby, wait: false, abdicate: null, cancellationToken).ConfigureAwait(false);
                break;
            case MemberStep.Claim:
                await ClaimInPlaceAsync(name, entry, record!, placement, ClaimTrigger.Abdication, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>What a member of a dataset's writer set holds of it, as the decision sees it.</summary>
    internal enum MemberState
    {
        /// <summary>It holds the writer epoch.</summary>
        Holding,

        /// <summary>It opened for write, and a newer epoch took the log.</summary>
        Superseded,

        /// <summary>It follows the dataset as a standby.</summary>
        Standby,

        /// <summary>It holds nothing open: not yet opened, or failed to open.</summary>
        Away,
    }

    /// <summary>What a member does next (ADR 0180).</summary>
    internal enum MemberStep
    {
        /// <summary>Nothing.</summary>
        Stay,

        /// <summary>Release the epoch for placement, and stand by.</summary>
        Abdicate,

        /// <summary>Open again as a standby, after a supersession.</summary>
        Rejoin,

        /// <summary>Claim at start, by the start rule.</summary>
        Start,

        /// <summary>Open for write after a placement release, as the preferred writer.</summary>
        Open,

        /// <summary>Open as a standby.</summary>
        Stand,

        /// <summary>Claim in place a release its reason lets it race for.</summary>
        Claim,
    }

    /// <summary>The writer record as the decision reads it: who holds it, and why it was released.</summary>
    internal readonly record struct RecordView(string Holder, ReleaseReason? Released);

    /// <summary>
    /// A member's next step, from the declaration applied before and the one
    /// applied now, what it holds, and the writer record (ADR 0180 points 2–4),
    /// a function of these alone so a property can drive it. Placement is a
    /// sequence of events: a holder abdicates only when placedOn changed and
    /// it is not the new placedOn, never because the holder differs from
    /// placedOn. A member that is not holding starts by the start rule, opens
    /// as the preferred writer on a release for placement, and otherwise
    /// stands by, racing for a release its reason allows once warm.
    /// </summary>
    internal static MemberStep Decide((string? PlacedOn, System.Collections.Generic.IReadOnlyList<string> StandbyOn)? previous, DatasetDeclaration declared, Placement placement, MemberState state, RecordView? record, bool warm, bool started)
    {
        bool preferred = placement.IsPreferred(declared);

        switch (state)
        {
            case MemberState.Holding:
                return previous is { } before && before.PlacedOn != declared.PlacedOn && !preferred ? MemberStep.Abdicate : MemberStep.Stay;
            case MemberState.Superseded:
                return MemberStep.Rejoin;
        }

        if (!started && Starts(record, declared, placement, preferred))
        {
            return MemberStep.Start;
        }

        if (state == MemberState.Away)
        {
            return record is { Released: ReleaseReason.Placement } && preferred ? MemberStep.Open : MemberStep.Stand;
        }

        // The record read before the advance: a release it shows is one the
        // advance has applied up to, or more (ADR 0179 point 3).
        return record is { Released: { } reason } && Races(reason, preferred, warm) ? MemberStep.Claim : MemberStep.Stay;
    }

    // The start rule (point 3): the record is empty or released and this is
    // the preferred writer; it holds the record itself, a restart after its
    // own crash; or its holder is not a member, so placement changed while
    // this instance was down. A holder named by no declared member, where the
    // preferred writer is the system writer's, may be that writer: it is
    // taken for a member, and not fenced.
    private static bool Starts(RecordView? record, DatasetDeclaration declared, Placement placement, bool preferred)
    {
        if (record is not { } held || held.Released is not null)
        {
            return preferred;
        }

        string holder = held.Holder;
        bool member = declared.PlacedOn is not { } placed || placed == holder || declared.StandbyOn.Contains(holder);
        return holder == placement.Instance.Value || !member;
    }

    // Who claims after a release (point 4): one for placement, the preferred
    // writer alone; one for shutdown, storage-error or local-disk-error,
    // every warm member, racing. An operator's release is the operator's.
    internal static bool Races(ReleaseReason reason, bool preferred, bool warm) => reason switch
    {
        ReleaseReason.Placement => preferred,
        ReleaseReason.Shutdown or ReleaseReason.StorageError or ReleaseReason.LocalDiskError => warm,
        _ => false,
    };

    // A standby follows on the interval (point 5), whether or not a read
    // arrives; a failed advance leaves it serving what it applied, and not warm.
    private async Task TailAsync(string name, Entry entry, CancellationToken cancellationToken)
    {
        if (entry.Dataset is not { IsFollower: true } dataset)
        {
            return;
        }

        try
        {
            await dataset.AdvanceAsync(cancellationToken).ConfigureAwait(false);
            entry.Advanced(succeeded: true);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            entry.Advanced(succeeded: false);
            ReconcileLog.TailFailed(_logger, name, error.Message);
        }
    }

    // The claim in place (ADR 0179 point 8), guarded by the epoch the record
    // showed: the store's replace-if-unchanged picks one of the members that
    // race. A loser stays a standby, opened again, since its storage may no
    // longer refresh.
    private async Task<bool> ClaimInPlaceAsync(string name, Entry entry, WriterRecord record, Placement placement, ClaimTrigger trigger, CancellationToken cancellationToken)
    {
        if (entry.Dataset is not { } dataset || entry.Objects is not { } storage)
        {
            return false;
        }

        bool won;

        try
        {
            won = await dataset.TakeWriteAsync(
                claim => storage.ClaimInPlaceAsync(placement.Instance, _address, SystemProbeRandomness.Instance, trigger, record.Epoch, claim),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ReconcileLog.ClaimFailed(_logger, name, error.Message);
            won = false;
        }

        if (!won)
        {
            ReconcileLog.Lost(_logger, name);
            await SwitchAsync(name, entry, DatasetRole.Standby, wait: false, abdicate: null, cancellationToken).ConfigureAwait(false);
            return false;
        }

        entry.Placed(DatasetRole.Writer);
        string why = record.Released is { } reason ? Commands.ObjectLease.Shown(reason) : Commands.ObjectLease.Shown(trigger);
        ReconcileLog.Claimed(_logger, name, storage.Epoch?.Value ?? 0, why);
        await SeedOrderAsync(new DatasetName(name), dataset, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// A takeover request for a dataset (ADR 0179 point 6): refused unless
    /// it declares automatic handover, this instance is in its writer set and
    /// a warm standby of it, and the writer record still shows
    /// <paramref name="from"/>; then claimed in place with the trigger
    /// <c>probe</c>. The epoch returned is the record's after it.
    /// </summary>
    internal async Task<TakeoverOutcome> TakeoverAsync(string name, TakeoverEpoch from, DatasetDeclaration declared, Placement placement, CancellationToken cancellationToken)
    {
        WriterRecord? record = await _objects!.ReadWriterAsync(name, cancellationToken).ConfigureAwait(false);
        long now = record?.Epoch.Value ?? 0;

        if (declared.Storage != StorageKind.ObjectStorage || !declared.AutomaticHandover)
        {
            return new TakeoverOutcome(TakeoverRefusal.HandoverOff, new TakeoverEpoch(now));
        }

        if (!placement.IsMember(declared))
        {
            return new TakeoverOutcome(TakeoverRefusal.NotInWriterSet, new TakeoverEpoch(now));
        }

        Entry? entry;

        lock (_gate)
        {
            entry = _entries.TryGetValue(name, out Entry? held) ? held : null;
        }

        // A trigger late or repeated for an old epoch is a visible no-op, whoever holds the dataset now (ADR 0179 point 10).
        if (record is null || now != from.Value)
        {
            return new TakeoverOutcome(TakeoverRefusal.EpochMoved, new TakeoverEpoch(now));
        }

        if (entry is not { Warm: true })
        {
            return new TakeoverOutcome(TakeoverRefusal.Cold, new TakeoverEpoch(now));
        }

        bool won = await ClaimInPlaceAsync(name, entry, record, placement, ClaimTrigger.Probe, cancellationToken).ConfigureAwait(false);
        now = (await _objects.ReadWriterAsync(name, cancellationToken).ConfigureAwait(false))?.Epoch.Value ?? now;
        return new TakeoverOutcome(won ? TakeoverRefusal.None : TakeoverRefusal.EpochMoved, new TakeoverEpoch(now));
    }

    private static partial class ReconcileLog
    {
        [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "This standby claimed '{Name}' in place at epoch {Epoch}, on {Reason} (ADR 0180).")]
        internal static partial void Claimed(ILogger logger, string name, long epoch, string reason);

        [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "Another member claimed '{Name}' first; this instance stays its standby (ADR 0180).")]
        internal static partial void Lost(ILogger logger, string name);

        [LoggerMessage(EventId = 15, Level = LogLevel.Warning, Message = "The standby of '{Name}' did not claim it in place: {Reason}")]
        internal static partial void ClaimFailed(ILogger logger, string name, string reason);

        [LoggerMessage(EventId = 16, Level = LogLevel.Warning, Message = "The standby of '{Name}' did not advance, and is not warm until it does: {Reason}")]
        internal static partial void TailFailed(ILogger logger, string name, string reason);

        [LoggerMessage(EventId = 18, Level = LogLevel.Warning, Message = "A batch of '{Name}' failed against the store: this writer released its epoch for storage-error and stands by (ADR 0179 point 6).")]
        internal static partial void AbdicatedForStorage(ILogger logger, string name);

        [LoggerMessage(EventId = 19, Level = LogLevel.Error, Message = "The memtable of '{Name}' holds {Entries} entries, its spills to the local disk fail, and {Bound}: this writer released its epoch for local-disk-error and stands by (ADR 0179 point 6).")]
        internal static partial void AbdicatedForLocalDisk(ILogger logger, string name, long entries, string bound);

        [LoggerMessage(EventId = 17, Level = LogLevel.Warning, Message = "A newer writer holds '{Name}': this instance rejoins its writer set as a standby (ADR 0179 point 10).")]
        internal static partial void Rejoined(ILogger logger, string name);
    }
}
