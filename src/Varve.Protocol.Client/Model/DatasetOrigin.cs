// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// Why a server holds a dataset (ADR 0163): declared in the management plane, or
/// otherwise, as the server writes it.
/// </summary>
/// <remarks>
/// A value the client does not know is carried as the server wrote it.
/// </remarks>
public readonly record struct DatasetOrigin
{
    /// <summary>The value <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public DatasetOrigin(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>Declared in the management plane.</summary>
    public static DatasetOrigin Declared { get; } = new("declared");

    /// <summary>The value as the server wrote it.</summary>
    public string Value { get; }

    /// <summary>Renders as <see cref="Value"/>.</summary>
    public override string ToString() => Value ?? string.Empty;
}
