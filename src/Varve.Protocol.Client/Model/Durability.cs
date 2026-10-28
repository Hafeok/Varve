// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// How a dataset's writes reach durable storage (ADR 0124), as its status
/// writes it.
/// </summary>
/// <remarks>
/// A value the client does not know is carried as the server wrote it.
/// </remarks>
public readonly record struct Durability
{
    /// <summary>The value <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public Durability(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>Each commit is synchronised before it is acknowledged.</summary>
    public static Durability Synchronised { get; } = new("Synchronised");

    /// <summary>The value as the server wrote it.</summary>
    public string Value { get; }

    /// <summary>Renders as <see cref="Value"/>.</summary>
    public override string ToString() => Value ?? string.Empty;
}
