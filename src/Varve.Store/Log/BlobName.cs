// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.Log;

/// <summary>
/// The name of a blob in the derived store, such as a checkpoint's.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0065 left the derived store's names to the two-bucket rule, and DD0013
/// reported them once the storage contract was marked a contract. They are
/// Varve's own coordinates, as segment ids and offsets are: the store chooses
/// them (<c>checkpoints/…</c>) and a backend keeps them. So they take the
/// design change the ADR took for the rest of the contract, and are a type.
/// </para>
/// <para>
/// Names are compared ordinally, which is the order
/// <see cref="IDerivedStore.ListAsync"/> promises. There is no conversion to
/// or from <c>string</c> but the constructor and <see cref="Value"/> (DD0015).
/// </para>
/// </remarks>
public readonly record struct BlobName : IComparable<BlobName>
{
    /// <summary>A blob name.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null or empty.</exception>
    public BlobName(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>The name.</summary>
    public string Value { get; }

    /// <summary>Renders as the name itself.</summary>
    public override string ToString() => Value;

    /// <inheritdoc />
    public int CompareTo(BlobName other) => string.CompareOrdinal(Value, other.Value);

    /// <summary>Whether <paramref name="left"/> sorts before <paramref name="right"/>, ordinally.</summary>
    public static bool operator <(BlobName left, BlobName right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> sorts after <paramref name="right"/>, ordinally.</summary>
    public static bool operator >(BlobName left, BlobName right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> does not sort after <paramref name="right"/>, ordinally.</summary>
    public static bool operator <=(BlobName left, BlobName right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> does not sort before <paramref name="right"/>, ordinally.</summary>
    public static bool operator >=(BlobName left, BlobName right) => left.CompareTo(right) >= 0;
}
