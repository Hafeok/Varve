// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Store.Log;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// The lineage record: what a copy of a dataset is (ADR 0185). Kept as the
/// object <c>lineage</c> beside <c>writer</c>. A copy that log shipping
/// writes is a <see cref="LineageState.Replica"/> of its source, and is
/// never opened for write until an operator promotes it.
/// </summary>
public sealed record LineageRecord
{
    /// <summary>
    /// Whether this build reads the record's format. One it does not read is
    /// never written on, and the dataset is treated as a replica (ADR 0217).
    /// </summary>
    public required bool Understood { get; init; }

    /// <summary>What the copy is. A state this build does not know is <see cref="LineageState.Unknown"/>, and is treated as a replica.</summary>
    public LineageState State { get; init; }

    /// <summary>The dataset this is a copy of, where the record reads.</summary>
    public LineageSource? Source { get; init; }

    /// <summary>The promotion, once the copy has been promoted.</summary>
    public LineagePromotion? Promoted { get; init; }

    /// <summary>
    /// Whether the dataset may not be opened for write: every state but
    /// <see cref="LineageState.Promoted"/>, and every record this build does
    /// not read.
    /// </summary>
    public bool IsReplica => !Understood || State != LineageState.Promoted;

    // The state as the record spelled it, kept when this build does not know it (ADR 0217).
    internal string? StateText { get; init; }
}

/// <summary>What a copy of a dataset is, as its lineage record says (ADR 0185).</summary>
public enum LineageState
{
    /// <summary>A copy that log shipping writes. Nothing else writes it, and it is never claimed.</summary>
    Replica,

    /// <summary>A copy an operator promoted: it is the dataset now, and takes no write from its old source.</summary>
    Promoted,

    /// <summary>A state a later version wrote that this one does not know: held as written, and treated as a replica (ADR 0217).</summary>
    Unknown,
}

/// <summary>The dataset a copy was shipped from (ADR 0185).</summary>
/// <param name="Store">The source's store: a container, a bucket.</param>
/// <param name="Prefix">The dataset's prefix in it, or none for the store's root.</param>
/// <param name="Dataset">The dataset's id, from its manifest. A copy has its source's.</param>
/// <param name="Fleet">The id of the source fleet's system dataset, whose positions the copy's commits record (ADR 0165), where the source has a fleet.</param>
public sealed record LineageSource(StoreAddress Store, ObjectName? Prefix, DatasetId Dataset, DatasetId? Fleet);

/// <summary>A promotion, as the lineage record keeps it (ADR 0185).</summary>
public sealed record LineagePromotion
{
    /// <summary>The epoch the promotion claimed: above every epoch that was shipped.</summary>
    public required WriterEpoch Epoch { get; init; }

    /// <summary>The last position shipped: what the promoted copy continues from.</summary>
    public required Position Position { get; init; }

    /// <summary>The header hash of the commit at that position.</summary>
    public required CommitHeaderHash Hash { get; init; }

    /// <summary>When, by the promoter's clock: for an operator to read, never to decide by.</summary>
    public required ClaimTime At { get; init; }

    /// <summary>Which instance promoted it.</summary>
    public required WriterInstance By { get; init; }
}

/// <summary>
/// A store's address as a lineage record names it: what the operator
/// declared the store as, for example a container's URL or <c>s3://bucket</c>.
/// </summary>
public readonly record struct StoreAddress
{
    /// <summary>An address: printable ASCII with no space, at most 2048 characters.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty, too long, or holds another character.</exception>
    public StoreAddress(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        if (value.Length > 2048)
        {
            throw new ArgumentException("A store's address is at most 2048 characters.", nameof(value));
        }

        foreach (char c in value)
        {
            if (c <= ' ' || c > '~')
            {
                throw new ArgumentException("A store's address is printable ASCII with no space.", nameof(value));
            }
        }

        Value = value;
    }

    /// <summary>The address.</summary>
    public string Value { get; }

    /// <summary>Renders as the address.</summary>
    public override string ToString() => Value;
}

/// <summary>A commit's header hash, as a record an operator reads spells it: 64 lower-case hexadecimal digits.</summary>
public readonly record struct CommitHeaderHash
{
    /// <summary>A header hash.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not 64 lower-case hexadecimal digits.</exception>
    public CommitHeaderHash(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        if (value.Length != 64)
        {
            throw new ArgumentException("A header hash is 64 lower-case hexadecimal digits.", nameof(value));
        }

        foreach (char c in value)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                throw new ArgumentException("A header hash is 64 lower-case hexadecimal digits.", nameof(value));
            }
        }

        Value = value;
    }

    /// <summary>The digits.</summary>
    public string Value { get; }

    /// <summary>Renders as the digits.</summary>
    public override string ToString() => Value;
}
