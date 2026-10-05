// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.Log;

/// <summary>
/// The identity of a dataset, written in its manifest and in every segment and
/// derived file, so that a segment or a derived file from another dataset is
/// recognised as foreign.
/// </summary>
/// <remarks>
/// <para>
/// **Given by whoever creates the dataset; the store never generates one**
/// (ADR 0072, <c>DatasetIdIsGiven</c>). Ambient randomness is banned in
/// <c>Varve.Store</c> (ADR 0011), and two machines fed the same requests with
/// the same id write the same bytes (§10). A host makes one with
/// <c>Guid.NewGuid()</c> at its edge.
/// </para>
/// <para>
/// Written as 16 bytes in RFC 9562 order, the order a GUID is written as text.
/// There is no conversion to or from <see cref="Guid"/> but the constructor
/// and <see cref="Value"/> (DD0015).
/// </para>
/// </remarks>
public readonly record struct DatasetId
{
    internal const int Length = 16;

    /// <summary>A dataset id.</summary>
    public DatasetId(Guid value) => Value = value;

    /// <summary>The id.</summary>
    public Guid Value { get; }

    /// <summary>Renders as the GUID's text.</summary>
    public override string ToString() => Value.ToString("D");

    internal void WriteTo(Span<byte> destination) => Value.TryWriteBytes(destination, bigEndian: true, out _);

    internal static DatasetId Read(ReadOnlySpan<byte> source) => new(new Guid(source, bigEndian: true));
}
