// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Store.ObjectStorage.Model;

namespace Varve.Store.ObjectStorage;

/// <summary>
/// The dataset is a replica: a copy that log shipping writes, as its lineage
/// record says (ADR 0185). It is never opened for write, and its writer
/// epoch is never claimed, until an operator promotes it. Nothing was
/// written, not even the probe. It opens read-only, as a follower.
/// </summary>
public sealed class DatasetIsReplicaException : Exception
{
    /// <summary>The dataset is a replica.</summary>
    public DatasetIsReplicaException()
    {
    }

    /// <summary>The dataset is a replica, as the message says.</summary>
    public DatasetIsReplicaException(string message)
        : base(message)
    {
    }

    /// <summary>The dataset is a replica, as the message says, and the cause.</summary>
    public DatasetIsReplicaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The dataset is the replica <paramref name="lineage"/> describes.</summary>
    public DatasetIsReplicaException(LineageRecord lineage)
        : base(Describe(lineage)) =>
        Lineage = lineage;

    /// <summary>The lineage record that says so, where one was read.</summary>
    public LineageRecord? Lineage { get; }

    private static string Describe(LineageRecord lineage)
    {
        ArgumentNullException.ThrowIfNull(lineage);
        return !lineage.Understood
            ? "The dataset has a lineage record of a format this version of Varve does not read, so it is treated as a replica and is not opened for write (ADRs 0185, 0217). Upgrade Varve, or open it read-only."
            : "The dataset is a replica of the one at " + lineage.Source!.Store + (lineage.Source.Prefix is { } prefix ? " under '" + prefix + "'" : string.Empty)
                + ": log shipping writes it, and nothing else does. It is opened for write only after an operator promotes it (varve promote, ADR 0185). It opens read-only as it is.";
    }
}
