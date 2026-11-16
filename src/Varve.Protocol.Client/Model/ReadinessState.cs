// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// A dataset's state as readiness reports it (ADR 0113).
/// </summary>
/// <remarks>
/// A value the client does not know is carried as the server wrote it.
/// </remarks>
public readonly record struct ReadinessState
{
    /// <summary>The state <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public ReadinessState(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>Open and within its allowed lag.</summary>
    public static ReadinessState Ready { get; } = new("ready");

    /// <summary>Its projection lags the head by more than allowed.</summary>
    public static ReadinessState Behind { get; } = new("behind");

    /// <summary>It failed and serves nothing.</summary>
    public static ReadinessState Failed { get; } = new("failed");

    /// <summary>A newer writer holds it.</summary>
    public static ReadinessState Superseded { get; } = new("superseded");

    /// <summary>Declared closed (ADR 0163 point 14).</summary>
    public static ReadinessState Closed { get; } = new("closed");

    /// <summary>The value as the server wrote it.</summary>
    public string Value { get; }

    /// <summary>Renders as <see cref="Value"/>.</summary>
    public override string ToString() => Value ?? string.Empty;
}
