// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Store.ObjectStorage;

/// <summary>
/// A target holds, at a name the shipper was about to write, bytes that are
/// not the source's (ADR 0184): the two logs have diverged. Nothing was
/// overwritten and nothing deleted. The shipper stops for that target.
/// </summary>
public sealed class ShippingDivergedException : Exception
{
    /// <summary>The target diverged.</summary>
    public ShippingDivergedException()
    {
    }

    /// <summary>The target diverged, as the message says.</summary>
    public ShippingDivergedException(string message)
        : base(message)
    {
    }

    /// <summary>The target diverged, as the message says, and the cause.</summary>
    public ShippingDivergedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The target diverged at <paramref name="slot"/>, in the way <paramref name="how"/> says.</summary>
    public ShippingDivergedException(LogSlot slot, string how)
        : base("The shipping target has diverged from its source at log slot " + slot + ": " + how
            + ". Nothing was overwritten. A target is written only by create-if-absent of the source's bytes (ADR 0184); ship to a fresh prefix.") =>
        Slot = slot;

    /// <summary>The target diverged at <paramref name="slot"/>, in the way <paramref name="how"/> says, and the cause.</summary>
    public ShippingDivergedException(LogSlot slot, string how, Exception innerException)
        : base("The shipping target has diverged from its source at log slot " + slot + ": " + how
            + ". Nothing was overwritten. A target is written only by create-if-absent of the source's bytes (ADR 0184); ship to a fresh prefix.", innerException) =>
        Slot = slot;

    /// <summary>The slot at which they differ, where one is known.</summary>
    public LogSlot? Slot { get; }
}

/// <summary>
/// The target's prefix holds a dataset that is not a replica of this source
/// (ADR 0184): another dataset, a copy of another source, or a log with no
/// lineage record. Nothing was written to it.
/// </summary>
public sealed class ShippingTargetNotEmptyException : Exception
{
    /// <summary>The target is not empty.</summary>
    public ShippingTargetNotEmptyException()
    {
    }

    /// <summary>The target is not empty, as the message says.</summary>
    public ShippingTargetNotEmptyException(string message)
        : base(message)
    {
    }

    /// <summary>The target is not empty, as the message says, and the cause.</summary>
    public ShippingTargetNotEmptyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The target was promoted (ADR 0185): it is a dataset of its own now, and
/// takes no write from its old source. The shipper stops for that target.
/// </summary>
public sealed class ReplicaPromotedException : Exception
{
    /// <summary>The target was promoted.</summary>
    public ReplicaPromotedException()
    {
    }

    /// <summary>The target was promoted, as the message says.</summary>
    public ReplicaPromotedException(string message)
        : base(message)
    {
    }

    /// <summary>The target was promoted, as the message says, and the cause.</summary>
    public ReplicaPromotedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The target was promoted, as <paramref name="lineage"/> records.</summary>
    public ReplicaPromotedException(LineageRecord lineage)
        : base("The shipping target was promoted at position " + (lineage ?? throw new ArgumentNullException(nameof(lineage))).Promoted?.Position
            + " by " + lineage.Promoted?.By + ": it is the dataset now and takes no write from its old source (ADR 0185). To make this dataset a replica of it, ship from the promoted copy to a fresh prefix.") =>
        Lineage = lineage;

    /// <summary>The target's lineage record, where one was read.</summary>
    public LineageRecord? Lineage { get; }
}
