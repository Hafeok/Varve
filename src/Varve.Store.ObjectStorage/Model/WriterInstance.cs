// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// Who holds a writer epoch: the name a process gives itself, kept in the
/// writer record so an operator can tell writers apart. The host chooses it;
/// the store never generates one (ADR 0011).
/// </summary>
public readonly record struct WriterInstance
{
    /// <summary>An instance name: printable ASCII, at most 256 characters.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty, too long, or not printable ASCII.</exception>
    public WriterInstance(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        if (value.Length > 256)
        {
            throw new ArgumentException("An instance name is at most 256 characters.", nameof(value));
        }

        foreach (char c in value)
        {
            if (c < ' ' || c > '~')
            {
                throw new ArgumentException("An instance name is printable ASCII.", nameof(value));
            }
        }

        Value = value;
    }

    /// <summary>The name.</summary>
    public string Value { get; }

    /// <summary>Renders as the name.</summary>
    public override string ToString() => Value;
}
