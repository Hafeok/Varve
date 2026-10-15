// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// A slot of the log, as the writer record names it:
/// <c>NNNNNNNN.chunks/CCCCCCCC</c>, a segment and a chunk under <c>log/</c>.
/// </summary>
public readonly record struct LogSlot
{
    /// <summary>A slot.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not of the form <c>NNNNNNNN.chunks/CCCCCCCC</c>.</exception>
    public LogSlot(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        if (value.Length != 24 || !value.AsSpan(8, 8).SequenceEqual(".chunks/") || !Digits(value.AsSpan(0, 8)) || !Digits(value.AsSpan(16, 8)))
        {
            throw new ArgumentException("'" + value + "' is not a log slot of the form NNNNNNNN.chunks/CCCCCCCC.", nameof(value));
        }

        Value = value;
    }

    /// <summary>The slot.</summary>
    public string Value { get; }

    /// <summary>Renders as the slot.</summary>
    public override string ToString() => Value;

    private static bool Digits(ReadOnlySpan<char> part)
    {
        foreach (char c in part)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
