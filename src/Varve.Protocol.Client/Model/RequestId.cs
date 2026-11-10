// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// A request's id as the server names it in <c>Varve-Request-Id</c> and a
/// problem's <c>instance</c> (ADRs 0119, 0168): the instance's name, a colon
/// and the request's trace identifier.
/// </summary>
/// <remarks>
/// Opaque to the client: it is shown and quoted, never parsed.
/// </remarks>
public readonly record struct RequestId
{
    /// <summary>The request id <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public RequestId(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>The value as the server wrote it.</summary>
    public string Value { get; }

    /// <summary>Renders as <see cref="Value"/>.</summary>
    public override string ToString() => Value ?? string.Empty;
}
