// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Server;

/// <summary>
/// What readiness reports of a dataset this instance released for
/// storage-error or local-disk-error (ADR 0163, amended 2026-11-10; #251):
/// the reason of its last such release, how many it has made in a row, and
/// since when. It stands through the hold-off, through the claim that
/// follows, and through the writing after it, and ends only once this
/// instance has written the dataset for <see cref="Quiet"/> with no spill
/// failing. So an instance that is alone and releases for local-disk-error
/// again and again, each claim in place succeeding in between, reports it
/// the whole time, where a failing spill is reported only while it fails.
/// </summary>
internal sealed class OwnRelease(TimeProvider clock)
{
    /// <summary>How long this instance writes the dataset, no spill failing, before the report ends: the hold-off's cap.</summary>
    internal static readonly TimeSpan Quiet = ClaimHoldOff.Cap;

    private long? _writingSince;

    /// <summary>The reason of the last release; none when nothing is reported.</summary>
    internal ReleaseReason? Reason { get; private set; }

    /// <summary>The releases in a row since the report began.</summary>
    internal int Releases { get; private set; }

    /// <summary>When the first of them was made.</summary>
    internal DateTimeOffset Since { get; private set; }

    /// <summary>This instance released the dataset for <paramref name="reason"/>.</summary>
    internal void Released(ReleaseReason reason)
    {
        if (Reason is null)
        {
            Since = clock.GetUtcNow();
        }

        Reason = reason;
        Releases++;
        _writingSince = null;
    }

    /// <summary>Asked once a round: whether this instance writes the dataset now, and whether a spill of it is failing.</summary>
    internal void Round(bool writing, bool spillFailing)
    {
        if (Reason is null)
        {
            return;
        }

        if (!writing || spillFailing)
        {
            _writingSince = null;
            return;
        }

        _writingSince ??= clock.GetTimestamp();

        if (clock.GetElapsedTime(_writingSince.Value) >= Quiet)
        {
            Reason = null;
            Releases = 0;
            _writingSince = null;
        }
    }
}
