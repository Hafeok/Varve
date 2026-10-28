// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// What a grant's grantee is (ADR 0166): an agent, a role or a group, as the
/// grants route writes it.
/// </summary>
/// <remarks>
/// A value the client does not know is carried as the server wrote it.
/// </remarks>
public readonly record struct GranteeKind
{
    /// <summary>The value <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public GranteeKind(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>One agent, by IRI.</summary>
    public static GranteeKind Agent { get; } = new("agent");

    /// <summary>Every caller whose role claim holds the value.</summary>
    public static GranteeKind Role { get; } = new("role");

    /// <summary>Every caller whose group claim holds the value.</summary>
    public static GranteeKind Group { get; } = new("group");

    /// <summary>The value as the server wrote it.</summary>
    public string Value { get; }

    /// <summary>Renders as <see cref="Value"/>.</summary>
    public override string ToString() => Value ?? string.Empty;
}
