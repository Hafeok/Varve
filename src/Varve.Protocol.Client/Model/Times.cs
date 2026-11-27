// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Protocol.Client.Model;

/// <summary>A time an as-of read is pinned to: the server reads the last commit at or before it.</summary>
public readonly record struct AsOfTime
{
    /// <summary>The time <paramref name="value"/>, kept in UTC.</summary>
    public AsOfTime(DateTimeOffset value) => Value = value.ToUniversalTime();

    /// <summary>The time, in UTC.</summary>
    public DateTimeOffset Value { get; }

    /// <summary>Renders as RFC 3339 in UTC with seven fractional digits, as <c>Varve-As-Of: time:</c> takes it.</summary>
    public override string ToString() => Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}

/// <summary>When the commit at a read's position was made, as <c>Last-Modified</c> states it: to the second.</summary>
public readonly record struct CommitTime
{
    /// <summary>The time <paramref name="value"/>.</summary>
    public CommitTime(DateTimeOffset value) => Value = value.ToUniversalTime();

    /// <summary>The time, in UTC.</summary>
    public DateTimeOffset Value { get; }

    /// <summary>Renders as RFC 3339 in UTC, to the second.</summary>
    public override string ToString() => Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>How many commits below the head an as-of read may be (ADR 0114, <c>MaxAsOfDistance</c>).</summary>
public readonly record struct AsOfDistance
{
    /// <summary>The distance <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public AsOfDistance(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Value = value;
    }

    /// <summary>The number of commits.</summary>
    public long Value { get; }

    /// <summary>Renders as the number.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
