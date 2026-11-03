// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Model;

/// <summary>
/// A dataset's writer epoch as a takeover sees it (ADR 0179 point 6): the
/// one a request names, which the record must still show, and the one the
/// record shows after.
/// </summary>
public readonly record struct TakeoverEpoch
{
    /// <summary>An epoch: 0 before the first claim, or a claim's.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public TakeoverEpoch(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The epoch.</summary>
    public long Value { get; }
}

/// <summary>Why a takeover was refused (ADR 0179 point 6); <see cref="None"/> when it was not.</summary>
public enum TakeoverRefusal : byte
{
    /// <summary>It took the dataset over.</summary>
    None = 0,

    /// <summary><c>handover-off</c>: the dataset does not declare automatic handover.</summary>
    HandoverOff = 1,

    /// <summary><c>not-in-writer-set</c>: this instance is neither the dataset's preferred writer nor a standby.</summary>
    NotInWriterSet = 2,

    /// <summary><c>cold</c>: this instance is not a warm standby of the dataset.</summary>
    Cold = 3,

    /// <summary><c>epoch-moved</c>: the writer record no longer shows the epoch named.</summary>
    EpochMoved = 4,
}

/// <summary>A takeover's outcome: the refusal, if any, and the writer epoch after it, the new one when it took the dataset over.</summary>
/// <param name="Refusal">Why it was refused, or <see cref="TakeoverRefusal.None"/>.</param>
/// <param name="Epoch">The writer epoch now.</param>
public readonly record struct TakeoverOutcome(TakeoverRefusal Refusal, TakeoverEpoch Epoch);
