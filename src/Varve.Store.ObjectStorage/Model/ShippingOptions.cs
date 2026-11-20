// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Store.Log;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// One shipping target of one dataset (ADR 0184): where the dataset is, where
/// its copy goes, and what the copy's lineage record says of its source.
/// </summary>
public sealed record ShippingOptions
{
    /// <summary>The source's store. The shipper only reads it.</summary>
    public required IObjectStore Source { get; init; }

    /// <summary>The dataset's prefix in the source, ending in <c>/</c>, or none.</summary>
    public ObjectName? SourcePrefix { get; init; }

    /// <summary>
    /// The target's store, at its root: the container or the bucket. Its
    /// verification record is read there (ADR 0183), and the dataset is under
    /// <see cref="TargetPrefix"/>.
    /// </summary>
    public required IObjectStore Target { get; init; }

    /// <summary>The copy's prefix in the target, ending in <c>/</c>, or none.</summary>
    public ObjectName? TargetPrefix { get; init; }

    /// <summary>What the copy's lineage record names as its source (ADR 0185).</summary>
    public required LineageSource Lineage { get; init; }

    /// <summary>
    /// The id of the shipper's reader record on the source (ADR 0160): one per
    /// target, so that two targets of one dataset never share an interval.
    /// </summary>
    public required ReaderId Reader { get; init; }

    /// <summary>The name the shipping instance gives itself, for the operator who reads the record.</summary>
    public WriterInstance? Holder { get; init; }

    /// <summary>A directory of this machine in which a consolidated segment is staged on its way; the shipper empties what it puts there.</summary>
    public required CacheDirectory Staging { get; init; }

    /// <summary>The host's clock: what a retry of a lost answer waits on (ADR 0136).</summary>
    public required TimeProvider Clock { get; init; }
}

/// <summary>What one round of shipping did (ADR 0184): how far the target's log and its catalog got.</summary>
public sealed record ShippedRound
{
    /// <summary>The target's log after the round.</summary>
    public required ShippedLog Log { get; init; }

    /// <summary>The catalog the target holds after the round, or none while the source has no checkpoint.</summary>
    public CatalogSummary? Catalog { get; init; }

    /// <summary>Whether the round wrote anything to the target, its log or its sets or its catalog.</summary>
    public required bool Moved { get; init; }
}

/// <summary>How far a target's log has been shipped (ADR 0184): the next slot the shipper will write.</summary>
public sealed record ShippedLog
{
    /// <summary>The slot after the last object shipped.</summary>
    public required LogSlot NextSlot { get; init; }

    /// <summary>Whether the round that reported this wrote anything to the target.</summary>
    public required bool Moved { get; init; }
}
