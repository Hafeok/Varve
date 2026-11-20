// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// One entry of <c>Varve-Without-Closure</c> (ADR 0197
/// point 6): a named graph's IRI in angle brackets, read without a
/// closure, or <c>+N more</c> after the twentieth.
/// </summary>
public readonly record struct GraphWithoutClosure
{
    /// <summary>The entry <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public GraphWithoutClosure(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>The entry as the server wrote it.</summary>
    public string Value { get; }

    /// <summary>Renders as <see cref="Value"/>.</summary>
    public override string ToString() => Value ?? string.Empty;
}
