// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.Log;

/// <summary>A segment of the log: its number, its length, and whether it is sealed.</summary>
/// <remarks>
/// Made by <see cref="Open"/> or <see cref="Sealed"/> rather than by a
/// constructor taking a <c>bool</c>, whose call site would read
/// <c>new SegmentInfo(id, length, true)</c> (DD0016).
/// </remarks>
public readonly struct SegmentInfo : IEquatable<SegmentInfo>
{
    private SegmentInfo(SegmentId id, ByteCount length, bool isSealed)
    {
        Id = id;
        Length = length;
        IsSealed = isSealed;
    }

    /// <summary>Describes a segment that is still open for appends.</summary>
    public static SegmentInfo Open(SegmentId id, ByteCount length) => new(id, length, isSealed: false);

    /// <summary>Describes a sealed segment, which will never change.</summary>
    public static SegmentInfo Sealed(SegmentId id, ByteCount length) => new(id, length, isSealed: true);

    /// <summary>The segment's number.</summary>
    public SegmentId Id { get; }

    /// <summary>How many bytes it holds.</summary>
    public ByteCount Length { get; }

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
