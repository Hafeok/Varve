// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Model;

/// <summary>
/// The application that acted for a token's subject (ADR 0127): its client
/// identifier as the token states it (<c>azp</c>, or <c>appid</c> in an Entra
/// v1 token). It is telemetry, the request span's <c>varve.azp</c>, and never
/// part of a commit: the log records who changed what, not through what.
/// </summary>
public readonly record struct ActingApplication
{
    /// <summary>An application identifier.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public ActingApplication(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>The identifier, as the token gives it.</summary>
    public string Value { get; }

    /// <summary>Renders as the identifier.</summary>
    public override string ToString() => Value ?? string.Empty;
}
