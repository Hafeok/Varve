// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;
using Varve.Store.Log;

namespace Varve.Protocol.Model;

/// <summary>Which form an <see cref="AsOf"/> selector takes.</summary>
public enum AsOfKind : byte
{
    /// <summary><c>position:&lt;n&gt;</c>: a log position.</summary>
    Position = 0,

    /// <summary><c>time:&lt;RFC 3339&gt;</c>: an instant, resolved by I5.</summary>
    Time = 1,
}

/// <summary>
/// The value of a <c>Varve-As-Of</c> header: a position, or an instant that
/// resolves to the greatest position whose commit is at or before it (ADR 0096).
/// </summary>
public readonly record struct AsOf
{
    private AsOf(AsOfKind kind, Position position, CommitTimestamp time)
    {
        Kind = kind;
        Position = position;
        Time = time;
    }

    /// <summary>Which form this is.</summary>
    public AsOfKind Kind { get; }

    /// <summary>The position, when <see cref="Kind"/> is <see cref="AsOfKind.Position"/>.</summary>
    public Position Position { get; }

    /// <summary>The instant, in UTC, when <see cref="Kind"/> is <see cref="AsOfKind.Time"/>.</summary>
    public CommitTimestamp Time { get; }

    /// <summary>A position selector.</summary>
    public static AsOf AtPosition(Position position) => new(AsOfKind.Position, position, default);

    /// <summary>An instant selector, normalised to UTC.</summary>
    public static AsOf AtTime(CommitTimestamp time) => new(AsOfKind.Time, default, new CommitTimestamp(time.Value.ToUniversalTime()));

    /// <summary>
    /// Reads <c>position:&lt;n&gt;</c> or <c>time:&lt;RFC 3339 date-time&gt;</c>.
    /// A time takes any offset and at most seven fractional digits, the
    /// store's tick: more would be truncated, which would move the instant.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> text, out AsOf asOf)
    {
        asOf = default;

        if (text.StartsWith("position:", StringComparison.Ordinal))
        {
            ReadOnlySpan<char> digits = text["position:".Length..];

            if (Instants.IsDecimal(digits) && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long value))
            {
                asOf = AtPosition(new Position(value));
                return true;
            }

            return false;
        }

        if (text.StartsWith("time:", StringComparison.Ordinal) && Instants.TryParse(text["time:".Length..], out CommitTimestamp time))
        {
            asOf = AtTime(time);
            return true;
        }

        return false;
    }

    /// <summary>Renders as the header value it was read from, a time in UTC.</summary>
    public override string ToString() => Kind == AsOfKind.Position
        ? "position:" + Position.Value.ToString(CultureInfo.InvariantCulture)
        : "time:" + Instants.Format(Time);
}
