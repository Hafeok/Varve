// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// A dataset's state as the management routes write it (ADRs 0163, 0165): open,
/// closed, or failed.
/// </summary>
/// <remarks>
/// A value the client does not know is carried as the server wrote it.
/// </remarks>
public readonly record struct DatasetState
{
    /// <summary>The value <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public DatasetState(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>Served for reads and writes.</summary>
    public static DatasetState Open { get; } = new("open");

    /// <summary>Declared closed: no instance serves it.</summary>
    public static DatasetState Closed { get; } = new("closed");

    /// <summary>It failed to open; the reason says why.</summary>
    public static DatasetState Failed { get; } = new("failed");

    /// <summary>The value as the server wrote it.</summary>
    public string Value { get; }

    /// <summary>Renders as <see cref="Value"/>.</summary>
    public override string ToString() => Value ?? string.Empty;
}
