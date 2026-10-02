// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Store.Log;

/// <summary>A version of the storage format of <c>log/</c> (ADR 0072).</summary>
/// <remarks>
/// Ordered: a build reads every version up to <see cref="Current"/>, and
/// refuses a later one by name. There is no conversion to or from
/// <c>ushort</c> but the constructor and <see cref="Value"/> (DD0015).
/// </remarks>
public readonly record struct FormatVersion : IComparable<FormatVersion>
{
    /// <summary>A format version.</summary>
    public FormatVersion(ushort value) => Value = value;

    /// <summary>The newest version this build reads and the one it writes: version 1.</summary>
    public static FormatVersion Current => new(1);

    /// <summary>The version number.</summary>
    public ushort Value { get; }

    /// <summary>Renders as the number, in the invariant culture.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public int CompareTo(FormatVersion other) => Value.CompareTo(other.Value);

    /// <summary>Whether <paramref name="left"/> is older than <paramref name="right"/>.</summary>
    public static bool operator <(FormatVersion left, FormatVersion right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> is newer than <paramref name="right"/>.</summary>
    public static bool operator >(FormatVersion left, FormatVersion right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> is not newer than <paramref name="right"/>.</summary>
    public static bool operator <=(FormatVersion left, FormatVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> is not older than <paramref name="right"/>.</summary>
    public static bool operator >=(FormatVersion left, FormatVersion right) => left.CompareTo(right) >= 0;
}
