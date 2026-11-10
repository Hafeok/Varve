// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// Whether an instance's bootstrap operators match the set the system dataset
/// recorded (ADR 0163 point 7).
/// </summary>
/// <remarks>
/// A value the client does not know is carried as the server wrote it.
/// </remarks>
public readonly record struct BootstrapState
{
    /// <summary>The state <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public BootstrapState(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>The configured set is the recorded one.</summary>
    public static BootstrapState Recorded { get; } = new("recorded");

    /// <summary>No set is recorded yet.</summary>
    public static BootstrapState Absent { get; } = new("absent");

    /// <summary>The configured set differs from the recorded one; the instance is not ready.</summary>
    public static BootstrapState Mismatch { get; } = new("mismatch");

    /// <summary>The value as the server wrote it.</summary>
    public string Value { get; }

    /// <summary>Renders as <see cref="Value"/>.</summary>
    public override string ToString() => Value ?? string.Empty;
}
