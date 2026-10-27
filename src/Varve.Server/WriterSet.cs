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

        bool preferred = placement.IsPreferred(declared);

        if (holding)
        {
            // A change of placedOn moves the epoch to the new placedOn only;
            // a holder that differs from placedOn after a takeover is a state,
            // and nothing reacts to it (point 2).
            if (previous is { } before && before.PlacedOn != declared.PlacedOn && !preferred)
            {
                await SwitchAsync(name, entry, DatasetRole.Standby, wait: false, abdicate: true, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (entry.State == DatasetState.Open && entry.Role == DatasetRole.Writer)
        {
            // Superseded: it rejoins as a standby, opened again, and is
            // eligible to claim once it is warm (ADR 0179 point 10).
            ReconcileLog.Rejoined(_logger, name);
            await SwitchAsync(name, entry, DatasetRole.Standby, wait: false, abdicate: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        WriterRecord? record = await _objects!.ReadWriterAsync(name, cancellationToken).ConfigureAwait(false);

        if (!entry.Started)
        {
            entry.Started = true;

            if (Starts(record, declared, placement, preferred))
            {
                await SwitchAsync(name, entry, DatasetRole.Writer, wait: true, abdicate: false, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        bool standing = entry.State == DatasetState.Open && entry.Role == DatasetRole.Standby;

        if (!standing)
        {
            if (record is { Released: ReleaseReason.Placement } && preferred)
            {
                await SwitchAsync(name, entry, DatasetRole.Writer, wait: false, abdicate: false, cancellationToken).ConfigureAwait(false);
                return;
            }

            await SwitchAsync(name, entry, DatasetRole.Standby, wait: false, abdicate: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        await TailAsync(name, entry, cancellationToken).ConfigureAwait(false);

        if (entry.Dataset is { ReopenDue: true })
        {
            // Not warm while it re-opens (point 8).
            ReconcileLog.Reopened(_logger, name);
            await SwitchAsync(name, entry, DatasetRole.Standby, wait: false, abdicate: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        // The record read before the advance: a release it shows is one the
        // advance has applied up to, or more (ADR 0179 point 3).
        if (record is { Released: { } reason } && Races(reason, preferred, entry.Warm))
        {
            await ClaimInPlaceAsync(name, entry, record, placement, cancellationToken).ConfigureAwait(false);
        }
    }

    // The start rule (point 3): the record is empty or released and this is
    // the preferred writer; it holds the record itself, a restart after its
    // own crash; or its holder is not a member, so placement changed while
    // this instance was down. A holder named by no declared member, where the
    // preferred writer is the system writer's, may be that writer: it is
    // taken for a member, and not fenced.
    private static bool Starts(WriterRecord? record, DatasetDeclaration declared, Placement placement, bool preferred)
    {
        if (record is null || record.Released is not null)
        {
            return preferred;
        }

        string holder = record.Holder.Value;
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
    private async Task ClaimInPlaceAsync(string name, Entry entry, WriterRecord record, Placement placement, CancellationToken cancellationToken)
    {
        if (entry.Dataset is not { } dataset || entry.Objects is not { } storage)
        {
            return;
        }

        bool won;

        try
        {
            won = await dataset.TakeWriteAsync(
                claim => storage.ClaimInPlaceAsync(placement.Instance, _address, SystemProbeRandomness.Instance, ClaimTrigger.Abdication, record.Epoch, claim),
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
            await SwitchAsync(name, entry, DatasetRole.Standby, wait: false, abdicate: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        entry.Placed(DatasetRole.Writer);
        ReconcileLog.Claimed(_logger, name, storage.Epoch?.Value ?? 0, record.Released is { } reason ? Commands.ObjectLease.Shown(reason) : "none");
        await SeedOrderAsync(new DatasetName(name), dataset, cancellationToken).ConfigureAwait(false);
    }

    private static partial class ReconcileLog
    {
        [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "This standby claimed '{Name}' in place at epoch {Epoch}, after a release for {Reason} (ADR 0180).")]
        internal static partial void Claimed(ILogger logger, string name, long epoch, string reason);

        [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "Another member claimed '{Name}' first; this instance stays its standby (ADR 0180).")]
        internal static partial void Lost(ILogger logger, string name);

        [LoggerMessage(EventId = 15, Level = LogLevel.Warning, Message = "The standby of '{Name}' did not claim it in place: {Reason}")]
        internal static partial void ClaimFailed(ILogger logger, string name, string reason);

        [LoggerMessage(EventId = 16, Level = LogLevel.Warning, Message = "The standby of '{Name}' did not advance, and is not warm until it does: {Reason}")]
        internal static partial void TailFailed(ILogger logger, string name, string reason);

        [LoggerMessage(EventId = 17, Level = LogLevel.Warning, Message = "A newer writer holds '{Name}': this instance rejoins its writer set as a standby (ADR 0179 point 10).")]
        internal static partial void Rejoined(ILogger logger, string name);
    }
}
