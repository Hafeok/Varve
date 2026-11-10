// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Client.Model;

/// <summary>
/// A graph template (ADR 0166): an IRI with <c>{claim}</c> placeholders, filled
/// from the caller's token, as the declaration writes it.
/// </summary>
public readonly record struct GraphTemplate
{
    /// <summary>The value <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public GraphTemplate(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>The value as the server wrote it.</summary>
    public string Value { get; }

    /// <summary>Renders as <see cref="Value"/>.</summary>
    public override string ToString() => Value ?? string.Empty;
}
