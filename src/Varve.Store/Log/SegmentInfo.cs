// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.Log;

/// <summary>A segment of the log: its number, its length, and whether it is sealed.</summary>
public readonly struct SegmentInfo : IEquatable<SegmentInfo>
{
    /// <summary>Describes a segment.</summary>
    public SegmentInfo(int id, long length, bool isSealed)
    {
        Id = id;
        Length = length;
        IsSealed = isSealed;
    }

    /// <summary>The segment's number.</summary>
    public int Id { get; }

    /// <summary>How many bytes it holds.</summary>
    public long Length { get; }

    /// <summary>Whether it is sealed and will never change.</summary>
    public bool IsSealed { get; }

    /// <inheritdoc />
    public bool Equals(SegmentInfo other) => Id == other.Id && Length == other.Length && IsSealed == other.IsSealed;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SegmentInfo other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Id, Length, IsSealed);

    /// <summary>Compares all three fields.</summary>
    public static bool operator ==(SegmentInfo left, SegmentInfo right) => left.Equals(right);

    /// <summary>Compares all three fields.</summary>
    public static bool operator !=(SegmentInfo left, SegmentInfo right) => !left.Equals(right);
}
