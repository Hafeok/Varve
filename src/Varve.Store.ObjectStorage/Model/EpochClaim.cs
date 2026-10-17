// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Store.ObjectStorage.Model;

/// <summary>One claim in the epoch history: who claimed which epoch, why, where it fenced the log, and how it ended.</summary>
public sealed record EpochClaim
{
    /// <summary>The epoch claimed.</summary>
    public required WriterEpoch Epoch { get; init; }

    /// <summary>Who claimed it.</summary>
    public required WriterInstance Holder { get; init; }

    /// <summary>Why it was claimed.</summary>
    public required ClaimTrigger Trigger { get; init; }

    /// <summary>When, by the claimant's clock; for an operator to read, never to decide by.</summary>
    public required ClaimTime ClaimedAt { get; init; }

    /// <summary>
    /// The fence: the log slot the epoch's first create took, as
    /// <c>NNNNNNNN.chunks/CCCCCCCC</c>. Every commit after it is the epoch's.
    /// None until the fence was created.
    /// </summary>
    public LogSlot? Fence { get; init; }

    /// <summary>Why the epoch was released, when it was.</summary>
    public ReleaseReason? Released { get; init; }
}
