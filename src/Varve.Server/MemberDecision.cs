// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Management.Model;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Server;

/// <summary>
/// What a writer set's member does at a reconcile (ADR 0180 points 2–4): a
/// function of the declarations, its state and the writer record alone, so
/// a property can drive it.
/// </summary>
[Contract(typeof(WarmStandbysAndTheWriterSet.PlacementIsAnEvent), Role = "a writer set's member's next step, from the change between declarations")]
internal static class MemberDecision
{
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
    internal static OpenDatasets.MemberStep Decide((string? PlacedOn, System.Collections.Generic.IReadOnlyList<string> StandbyOn)? previous, DatasetDeclaration declared, Placement placement, OpenDatasets.MemberState state, OpenDatasets.RecordView? record, bool warm, bool started)
    {
        bool preferred = placement.IsPreferred(declared);

        switch (state)
        {
            case OpenDatasets.MemberState.Holding:
                return previous is { } before && before.PlacedOn != declared.PlacedOn && !preferred ? OpenDatasets.MemberStep.Abdicate : OpenDatasets.MemberStep.Stay;
            case OpenDatasets.MemberState.Superseded:
                return OpenDatasets.MemberStep.Rejoin;
        }

        if (!started && StartRule.Starts(record, declared, placement, preferred))
        {
            return OpenDatasets.MemberStep.Start;
        }

        if (state == OpenDatasets.MemberState.Away)
        {
            return record is { Released: ReleaseReason.Placement } && preferred ? OpenDatasets.MemberStep.Open : OpenDatasets.MemberStep.Stand;
        }

        // The record read before the advance: a release it shows is one the
        // advance has applied up to, or more (ADR 0179 point 3).
        return record is { Released: { } reason } && ReleaseRace.Races(reason, preferred, warm) ? OpenDatasets.MemberStep.Claim : OpenDatasets.MemberStep.Stay;
    }

    // The start rule (point 3): the record is empty or released and this is
    // the preferred writer; it holds the record itself, a restart after its
    // own crash; or its holder is not a member, so placement changed while
    // this instance was down. A holder named by no declared member, where the
    // preferred writer is the system writer's, may be that writer: it is
    // taken for a member, and not fenced.
    [Contract(typeof(WarmStandbysAndTheWriterSet.StartRuleForAWriterSet), Role = "whether a member claims at its start")]
    internal static class StartRule
    {
        internal static bool Starts(OpenDatasets.RecordView? record, DatasetDeclaration declared, Placement placement, bool preferred)
        {
            if (record is not { } held || held.Released is not null)
            {
                return preferred;
            }

            string holder = held.Holder;
            bool member = declared.PlacedOn is not { } placed || placed == holder || declared.StandbyOn.Contains(holder);
            return holder == placement.Instance.Value || !member;
        }
    }

    // Who claims after a release (point 4): one for placement, the preferred
    // writer alone; one for shutdown, storage-error or local-disk-error,
    // every warm member, racing. An operator's release is the operator's.
    [Contract(typeof(WarmStandbysAndTheWriterSet.ClaimAfterAReleaseByItsReason), Role = "who races for a release, by its reason")]
    internal static class ReleaseRace
    {
        internal static bool Races(ReleaseReason reason, bool preferred, bool warm) => reason switch
        {
            ReleaseReason.Placement => preferred,
            ReleaseReason.Shutdown or ReleaseReason.StorageError or ReleaseReason.LocalDiskError => warm,
            _ => false,
        };
    }
}
