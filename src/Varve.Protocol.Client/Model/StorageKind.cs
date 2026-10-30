// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// Where a dataset's log is kept (ADR 0166): <c>File</c>, <c>Memory</c> or
/// <c>ObjectStorage</c>, as the server writes it.
/// </summary>
/// <remarks>
/// A value the client does not know is carried as the server wrote it.
/// </remarks>
public readonly record struct StorageKind
{
    /// <summary>The value <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public StorageKind(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>Files under the datasets root.</summary>
    public static StorageKind File { get; } = new("File");

    /// <summary>In the process: lost when it stops.</summary>
    public static StorageKind Memory { get; } = new("Memory");

    /// <summary>In the fleet's object store.</summary>
    public static StorageKind ObjectStorage { get; } = new("ObjectStorage");

    /// <summary>The value as the server wrote it.</summary>
    public string Value { get; }

    /// <summary>Renders as <see cref="Value"/>.</summary>
    public override string ToString() => Value ?? string.Empty;
}
