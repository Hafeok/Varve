// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CsCheck;
using Varve.Management.Model;
using Varve.Rdf;
using Varve.Store.ObjectStorage.Model;
using Xunit;
using static Varve.Server.OpenDatasets;

namespace Varve.Server.Tests;

/// <summary>
/// Placement is a sequence of events (ADR 0180 point 2), over generated
/// histories of declarations of one dataset with a writer set (its
/// <c>placedOn</c> moved, its standbys changed) and takeovers that leave the
/// holder differing from <c>placedOn</c>. Three members each apply every
/// declaration and act on <see cref="Decide"/>'s step, in an order generated
/// per round, against one writer record. Throughout:
/// - a holder abdicates only in the round its <c>placedOn</c> changed, and
///   only when it is not the new <c>placedOn</c>;
/// - nothing reacts to a holder that differs from <c>placedOn</c>: a quiet
///   round after a takeover changes no holder;
/// - a release for placement is claimed by the new <c>placedOn</c> alone, and
///   once the rounds go quiet the epoch is with it;
/// - the record names one holder, and every member that believes it holds
///   the epoch either is that holder or has not written since it lost it.
/// </summary>
public sealed class PlacementEventTests
{
    // The full depth, under a second, is the land gate's count; VARVE_PLACEMENT_SCHEDULES sets another (docs/testing.md §8).
    private static readonly int Schedules = int.TryParse(Environment.GetEnvironmentVariable("VARVE_PLACEMENT_SCHEDULES"), out int n) ? n : 20_000;

    private static readonly string[] Names = ["a", "b", "c"];

    [Fact]
    public void placement_moves_the_epoch_by_events_only_and_never_reacts_to_a_holder_that_differs_from_placedOn()
    {
        Gen<(int Kind, int Arg, int Order)[]> schedules = Gen.Select(Gen.Int[0, 5], Gen.Int[0, 7], Gen.Int[0, 5]).Array[1, 40];

        schedules.Sample(
            Run,
            iter: Schedules,
            print: schedule => string.Join(" ", schedule.Select(step => step.Kind + ":" + step.Arg + "/" + step.Order)));
    }

    // Kinds: 0 placedOn moves; 1 the standbys change; 2 a takeover by a warm standby; 3–5 a quiet round.
    private static void Run((int Kind, int Arg, int Order)[] schedule)
    {
        Fleet fleet = new();
        DatasetDeclaration declared = Declared("a", ["b", "c"]);
        fleet.Round(declared, 0);
        fleet.Round(declared, 1);
        Assert.Equal("a", fleet.Holder);

        int quiet = 0;

        foreach ((int kind, int arg, int order) in schedule)
        {
            string? holderBefore = fleet.Holder;

            switch (kind)
            {
                case 0:
                {
                    string placed = Names[arg % 3];
                    string[] standbys = [.. Names.Where(name => name != placed).Where((_, i) => (arg >> 2 & (1 << i)) == 0 || i == 0)];
                    declared = Declared(placed, standbys);
                    quiet = 0;
                    break;
                }

                case 1:
                {
                    string[] others = [.. Names.Where(name => name != declared.PlacedOn)];
                    string[] standbys = (arg % 3) switch { 0 => [others[0]], 1 => [others[1]], _ => others };
                    declared = Declared(declared.PlacedOn!, standbys);
                    quiet = 0;
                    break;
                }

                case 2:
                    // The probe trigger: a warm standby takes the epoch over, whatever placedOn says.
                    fleet.Takeover(arg);
                    holderBefore = fleet.Holder;
                    quiet = 0;
                    break;
                default:
                    quiet++;
                    break;
            }

            fleet.Round(declared, order);

            // Nothing reacts to a holder that differs from placedOn: a quiet round moves no epoch.
            if (kind >= 3 && quiet >= 3 && holderBefore is not null)
            {
                Assert.Equal(holderBefore, fleet.Holder);
            }
        }

        // Quiet rounds, and the epoch is where the last placedOn change sent it, or with the holder a takeover left.
        for (int round = 0; round < 4; round++)
        {
            fleet.Round(declared, round);
        }

        Assert.NotNull(fleet.Holder);
        Assert.Contains(fleet.Holder, (string[])[declared.PlacedOn!, .. declared.StandbyOn]);

        if (fleet.LastPlacementRelease)
        {
            Assert.Equal(declared.PlacedOn, fleet.Holder);
        }
    }

    private static DatasetDeclaration Declared(string placedOn, string[] standbys) => new()
    {
        Name = "d",
        Node = RdfTerm.Iri(Encoding.UTF8.GetBytes("urn:fleet:d")),
        Storage = StorageKind.ObjectStorage,
        PlacedOn = placedOn,
        StandbyOn = standbys,
        AutomaticHandover = true,
    };

    /// <summary>Three members and one writer record, acting on the decision's steps.</summary>
    private sealed class Fleet
    {
        private readonly Dictionary<string, Member> _members = Names.ToDictionary(name => name, name => new Member(name));
        private long _epoch;

        internal string? Holder { get; private set; }

        private ReleaseReason? Released { get; set; }

        /// <summary>Whether the epoch's last move was claimed from a release for placement.</summary>
        internal bool LastPlacementRelease { get; private set; }

        internal void Round(DatasetDeclaration declared, int order)
        {
            string[] turn = [.. Names.Skip(order % 3).Concat(Names.Take(order % 3))];

            if (order >= 3)
            {
                Array.Reverse(turn);
            }

            foreach (string name in turn)
            {
                Member member = _members[name];
                (string? PlacedOn, IReadOnlyList<string> StandbyOn)? previous = member.Applied;
                member.Applied = (declared.PlacedOn, declared.StandbyOn);
                Placement placement = new(new WriterInstance(name), MayClaim: true, SystemWriter: false, FleetOfOne: false);

                if (!placement.IsMember(declared))
                {
                    // Out of the writer set: an ordinary follower, a holder releasing for placement.
                    if (member.State == MemberState.Holding && Holder == name)
                    {
                        Release(ReleaseReason.Placement);
                    }

                    member.State = MemberState.Away;
                    member.Started = true;
                    continue;
                }

                RecordView? record = Holder is { } holder ? new RecordView(holder, Released) : null;

                if (member.State == MemberState.Standby)
                {
                    member.Warm = true;
                }

                MemberStep step = Decide(previous, declared, placement, member.State, record, member.Warm, member.Started);
                member.Started = true;
                Act(member, step, declared, previous);
            }
        }

        internal void Takeover(int arg)
        {
            Member[] warm = [.. _members.Values.Where(member => member.State == MemberState.Standby && member.Warm && member.Applied is { } applied && (applied.PlacedOn == member.Name || applied.StandbyOn.Contains(member.Name)))];

            if (warm.Length == 0)
            {
                return;
            }

            Member taker = warm[arg % warm.Length];
            Claim(taker);
            LastPlacementRelease = false;
        }

        private void Act(Member member, MemberStep step, DatasetDeclaration declared, (string? PlacedOn, IReadOnlyList<string> StandbyOn)? previous)
        {
            switch (step)
            {
                case MemberStep.Abdicate:
                    // Only in the round placedOn changed, and never by the new placedOn.
                    Assert.True(previous is { } before && before.PlacedOn != declared.PlacedOn, member.Name + " abdicated with placedOn unchanged");
                    Assert.NotEqual(declared.PlacedOn, member.Name);

                    // A holder a takeover deposed does not know it yet: its release does not apply.
                    if (Holder == member.Name)
                    {
                        Release(ReleaseReason.Placement);
                    }

                    member.State = MemberState.Standby;
                    member.Warm = false;
                    break;
                case MemberStep.Rejoin or MemberStep.Stand:
                    member.State = MemberState.Standby;
                    member.Warm = false;
                    break;
                case MemberStep.Start:
                    Claim(member);
                    LastPlacementRelease = false;
                    break;
                case MemberStep.Open or MemberStep.Claim:
                {
                    bool placement = Released == ReleaseReason.Placement;

                    // A release for placement is the new placedOn's alone.
                    if (placement)
                    {
                        Assert.Equal(declared.PlacedOn, member.Name);
                    }

                    if (Released is null && step == MemberStep.Claim)
                    {
                        break;
                    }

                    Claim(member);
                    LastPlacementRelease = placement;
                    break;
                }
            }
        }

        // A claim: the next epoch, this holder; the instance it took the epoch from is superseded at its next write.
        private void Claim(Member member)
        {
            if (Holder is { } previous && previous != member.Name && _members[previous].State == MemberState.Holding)
            {
                _members[previous].State = MemberState.Superseded;
            }

            _epoch++;
            Holder = member.Name;
            Released = null;
            member.State = MemberState.Holding;
        }

        private void Release(ReleaseReason reason) => Released = reason;
    }

    private sealed class Member(string name)
    {
        internal string Name { get; } = name;

        internal MemberState State { get; set; } = MemberState.Away;

        internal bool Warm { get; set; }

        internal bool Started { get; set; }

        internal (string? PlacedOn, IReadOnlyList<string> StandbyOn)? Applied { get; set; }
    }
}
