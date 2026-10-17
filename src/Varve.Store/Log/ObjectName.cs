// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.Log;

/// <summary>The name of an object in an object store, or a prefix of names.</summary>
/// <remarks>
/// <para>
/// ASCII letters, digits, <c>-</c>, <c>_</c> and <c>.</c>, in parts separated
/// by <c>/</c>: no part empty except after a trailing <c>/</c>, which makes a
/// prefix, and no part <c>.</c> or <c>..</c>. Every provider takes these names
/// unescaped, and a container downloaded to a disk keeps them as paths.
/// </para>
/// <para>
/// Names compare ordinally, the order <see cref="IObjectStore.ListAsync"/>
/// returns (ADR 0134).
/// </para>
/// </remarks>
public readonly record struct ObjectName : IComparable<ObjectName>
{
    // The longest name: the shortest limit any provider sets is 1,024 bytes.
    private const int MaxLength = 1024;

    /// <summary>An object name.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a name of the form above.</exception>
    public ObjectName(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        if (value.Length > MaxLength)
        {
            throw new ArgumentException("An object name is at most " + MaxLength + " characters.", nameof(value));
        }

        int start = 0;

        for (int i = 0; i <= value.Length; i++)
        {
            if (i < value.Length && value[i] != '/')
            {
                char c = value[i];

                if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
                {
                    throw new ArgumentException("'" + value + "' is not an object name: a part holds ASCII letters, digits, '-', '_' and '.'.", nameof(value));
                }

                continue;
            }

            ReadOnlySpan<char> part = value.AsSpan(start, i - start);
            bool trailing = i == value.Length && i > 0 && value[i - 1] == '/';

            if ((part.IsEmpty && !trailing) || part is "." or "..")
            {
                throw new ArgumentException("'" + value + "' is not an object name: every part between '/' is non-empty, not '.' or '..'.", nameof(value));
            }

            start = i + 1;
        }

        Value = value;
    }

    /// <summary>The name.</summary>
    public string Value { get; }

    /// <summary>Whether this name ends in <c>/</c>, so names only a prefix.</summary>
    public bool IsPrefix => Value.EndsWith('/');

    /// <summary>Whether this name begins with <paramref name="prefix"/>.</summary>
    public bool StartsWith(ObjectName prefix) => Value.StartsWith(prefix.Value, StringComparison.Ordinal);

    /// <summary>Renders as the name itself.</summary>
    public override string ToString() => Value;

    /// <inheritdoc />
    public int CompareTo(ObjectName other) => string.CompareOrdinal(Value, other.Value);

    /// <summary>Whether <paramref name="left"/> sorts before <paramref name="right"/>, ordinally.</summary>
    public static bool operator <(ObjectName left, ObjectName right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> sorts after <paramref name="right"/>, ordinally.</summary>
    public static bool operator >(ObjectName left, ObjectName right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> does not sort after <paramref name="right"/>, ordinally.</summary>
    public static bool operator <=(ObjectName left, ObjectName right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> does not sort before <paramref name="right"/>, ordinally.</summary>
    public static bool operator >=(ObjectName left, ObjectName right) => left.CompareTo(right) >= 0;
}
