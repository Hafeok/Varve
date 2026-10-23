// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.Log;

/// <summary>
/// The identity of an instance that reads extents from an object store, the
/// name of its reader record (ADR 0160). It survives a restart of the same
/// instance — configured, or kept in its cache directory — so a restart
/// replaces the record its previous run left.
/// </summary>
/// <remarks>
/// One to 128 characters, each an ASCII letter, a digit, <c>-</c>, <c>_</c> or
/// <c>.</c>, not starting with <c>.</c>: it is the last part of an object's
/// name. There is no conversion to or from <c>string</c> but the constructor
/// and <see cref="Value"/> (DD0015).
/// </remarks>
public readonly record struct ReaderId
{
    /// <summary>An id.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not an id.</exception>
    public ReaderId(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException("A reader id is 1 to 128 ASCII letters, digits, '-', '_' or '.', not starting with '.'.", nameof(value));
        }

        Value = value;
    }

    /// <summary>The id.</summary>
    public string Value { get; }

    /// <summary>Whether <paramref name="value"/> is an id.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 || value[0] == '.')
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Renders as the id itself.</summary>
    public override string ToString() => Value;
}
