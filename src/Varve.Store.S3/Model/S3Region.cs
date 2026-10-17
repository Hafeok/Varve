// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.S3.Model;

/// <summary>The region a signature is scoped to: <c>us-east-1</c> unless the store says otherwise.</summary>
public readonly record struct S3Region
{
    /// <summary>A region.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty or not lowercase letters, digits and '-'.</exception>
    public S3Region(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        foreach (char c in value)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
            {
                throw new ArgumentException("'" + value + "' is not a region name.", nameof(value));
            }
        }

        Value = value;
    }

    /// <summary>The name.</summary>
    public string Value { get; }

    /// <summary>The default region.</summary>
    public static S3Region Default => new("us-east-1");

    /// <summary>Renders as the name.</summary>
    public override string ToString() => Value;
}
