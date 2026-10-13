// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Delta.Model;
using Varve.Store.Log;

namespace Varve.Protocol.Model;

/// <summary>
/// RFC 3339 instants as the protocol reads and writes them (ADRs 0096, 0097):
/// read with any offset and at most seven fractional digits, written in UTC
/// with exactly seven and <c>Z</c>.
/// </summary>
public static class Instants
{
    /// <summary>
    /// Reads an RFC 3339 <c>date-time</c>, as the delta format does
    /// (<see cref="FeedTimestamp.TryParse"/>): <c>T</c> and <c>Z</c> may be
    /// lower case; more than seven fractional digits are refused.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> text, out CommitTimestamp time)
    {
        bool parsed = FeedTimestamp.TryParse(text, out FeedTimestamp instant);
        time = parsed ? new CommitTimestamp(instant.Value) : default;
        return parsed;
    }

    /// <summary>Writes an instant in UTC with seven fractional digits and <c>Z</c>.</summary>
    public static string Format(CommitTimestamp time) => new FeedTimestamp(time.Value).ToString();

    internal static bool IsDecimal(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
