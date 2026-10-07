// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Varve.Protocol.Model;

/// <summary>
/// The name a request uses for a dataset: one path segment, the host's and
/// never the store's (ADR 0093).
/// </summary>
/// <remarks>
/// One to 63 characters from <c>[A-Za-z0-9._-]</c>, starting with a letter or
/// a digit, so that it is never <c>.</c> or <c>..</c> and maps to a directory
/// name on every file system the store runs on. Compared ordinally. There is
/// no conversion to or from <c>string</c> but the constructor,
/// <see cref="TryParse"/> and <see cref="Value"/> (DD0015).
/// </remarks>
public readonly record struct DatasetName
{
    private const int MaxLength = 63;

    /// <summary>A dataset name.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a valid name.</exception>
    public DatasetName(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException("A dataset name is 1 to 63 characters of [A-Za-z0-9._-], starting with a letter or a digit.", nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// The name a host that serves one dataset answers to, when the route has
    /// no <c>{dataset}</c> segment: <c>default</c>.
    /// </summary>
    public static DatasetName Default { get; } = new("default");

    /// <summary>The name.</summary>
    public string Value { get; }

    /// <summary>Reads a name from a path segment.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out DatasetName name)
    {
        if (IsValid(text))
        {
            name = new DatasetName(text);
            return true;
        }

        name = default;
        return false;
    }

    /// <summary>Renders as the name itself.</summary>
    public override string ToString() => Value;

    private static bool IsValid([NotNullWhen(true)] string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength || !char.IsAsciiLetterOrDigit(text[0]))
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
