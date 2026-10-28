// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// What a grant allows (ADR 0107): read, write or admin of a dataset, as the
/// grants route writes it.
/// </summary>
/// <remarks>
/// A value the client does not know is carried as the server wrote it.
/// </remarks>
public readonly record struct GrantRight
{
    /// <summary>The value <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public GrantRight(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>Read the graphs in scope.</summary>
    public static GrantRight Read { get; } = new("read");

    /// <summary>Read and write the graphs in scope.</summary>
    public static GrantRight Write { get; } = new("write");

    /// <summary>Administer the dataset.</summary>
    public static GrantRight Admin { get; } = new("admin");

    /// <summary>The value as the server wrote it.</summary>
    public string Value { get; }

    /// <summary>Renders as <see cref="Value"/>.</summary>
    public override string ToString() => Value ?? string.Empty;
}
