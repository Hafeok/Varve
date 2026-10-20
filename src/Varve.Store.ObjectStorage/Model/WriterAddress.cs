// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// Where a writer is reached by the other instances of its fleet: its
/// internal URL, recorded with its claim so that a write sent elsewhere can
/// be forwarded to it (ADR 0168). The host chooses it; the store only keeps
/// it.
/// </summary>
public readonly record struct WriterAddress
{
    /// <summary>An absolute <c>http</c> or <c>https</c> URL, printable ASCII, at most 2048 characters.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not such a URL.</exception>
    public WriterAddress(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        if (value.Length > 2048)
        {
            throw new ArgumentException("A writer's address is at most 2048 characters.", nameof(value));
        }

        foreach (char c in value)
        {
            if (c <= ' ' || c > '~')
            {
                throw new ArgumentException("A writer's address is printable ASCII with no space.", nameof(value));
            }
        }

        if (!value.StartsWith("http://", StringComparison.Ordinal) && !value.StartsWith("https://", StringComparison.Ordinal))
        {
            throw new ArgumentException("A writer's address is an absolute http or https URL.", nameof(value));
        }

        Value = value;
    }

    /// <summary>The URL.</summary>
    public string Value { get; }

    /// <summary>Renders as the URL.</summary>
    public override string ToString() => Value;
}
