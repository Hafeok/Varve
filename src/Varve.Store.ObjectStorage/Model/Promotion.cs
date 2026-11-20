// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Store.Log;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// A promotion of a replica (ADR 0185 decision 4): who promotes, what the
/// operator expects the copy to be, and the tail the promoting instance
/// verified by replaying it.
/// </summary>
public sealed record PromotionRequest
{
    /// <summary>The instance that promotes, and becomes the writer.</summary>
    public required WriterInstance Instance { get; init; }

    /// <summary>The address writes are forwarded to, where the instance has one.</summary>
    public WriterAddress? Address { get; init; }

    /// <summary>The source the operator says this is a copy of. A lineage that names another refuses the promotion.</summary>
    public required StoreAddress Source { get; init; }

    /// <summary>The position the operator means to keep. A verified tail at another position refuses the promotion.</summary>
    public required Position Expected { get; init; }

    /// <summary>The last position of the copy, as the promoting instance replayed and verified it.</summary>
    public required Position Verified { get; init; }

    /// <summary>The header hash of the commit at <see cref="Verified"/>.</summary>
    public required CommitHeaderHash Hash { get; init; }

    /// <summary>Randomness for the probe of the store's conditional writes (ADR 0134).</summary>
    public required IProbeRandomness Randomness { get; init; }
}

/// <summary>How a promotion ended (ADR 0185 decision 4).</summary>
public enum PromotionOutcome
{
    /// <summary>The copy is promoted, and this storage is its writer's.</summary>
    Promoted,

    /// <summary>The dataset has no lineage record, or one this build does not read: nothing was written.</summary>
    NotAReplica,

    /// <summary>The lineage names another source than the operator expects: nothing was written.</summary>
    SourceMismatch,

    /// <summary>The verified tail, or an earlier promotion's, is not at the position the operator expects: nothing was written.</summary>
    PositionMismatch,

    /// <summary>Another promotion or another claim came first. Promote again.</summary>
    Lost,
}
