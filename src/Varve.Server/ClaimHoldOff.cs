// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Server;

/// <summary>
/// How long a member waits before it claims a dataset again, after its open
/// or its write against the store failed for that dataset (ADR 0163,
/// amended 2026-11-10; #251). The wait starts at one claim round and doubles
/// with each failure in a row, to <see cref="Cap"/>; each wait is drawn
/// between half of that and all of it, so members that failed together do
/// not claim together; and the member sits out at least one whole round,
/// however short the wait. A successful open as the writer ends it.
/// </summary>
[Contract(typeof(TheManagementPlane.ClaimsHoldOffAfterAStoreFailure), Role = "one member's hold-off for one dataset: when it may claim again")]
internal sealed class ClaimHoldOff(TimeSpan round, TimeProvider clock, IProbeRandomness randomness)
{
    /// <summary>The longest step: a store that keeps failing sees a claim every 15 to 30 seconds.</summary>
    internal static readonly TimeSpan Cap = TimeSpan.FromSeconds(30);

    private long _since;
    private bool _sitOut;

    /// <summary>The failures in a row since the last successful open as the writer.</summary>
    internal int Failures { get; private set; }

    /// <summary>The wait the last failure set; zero when none is in force.</summary>
    internal TimeSpan Wait { get; private set; }

    /// <summary>An open, a claim or a write against the store failed: the next wait begins now.</summary>
    internal void Failed()
    {
        Failures++;
        TimeSpan step = Failures > 30 || round * Math.Pow(2, Failures - 1) > Cap ? Cap : round * Math.Pow(2, Failures - 1);
        Span<byte> drawn = stackalloc byte[4];
        randomness.Fill(drawn);
        double unit = BitConverter.ToUInt32(drawn) / (uint.MaxValue + 1.0);
        Wait = step * (0.5 + (0.5 * unit));
        _since = clock.GetTimestamp();
        _sitOut = true;
    }

    /// <summary>The dataset is open here as its writer: the next failure waits one round again.</summary>
    internal void Succeeded()
    {
        Failures = 0;
        Wait = TimeSpan.Zero;
        _sitOut = false;
    }

    /// <summary>
    /// Asked once at each claim round: whether the member holds off in it.
    /// The first round after a failure is always sat out, so another member
    /// that sees the same record has a whole round of its own to claim in.
    /// </summary>
    internal bool Round()
    {
        if (Failures == 0)
        {
            return false;
        }

        if (_sitOut)
        {
            _sitOut = false;
            return true;
        }

        return clock.GetElapsedTime(_since) < Wait;
    }
}
