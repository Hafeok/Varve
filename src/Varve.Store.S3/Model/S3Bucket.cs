// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.S3.Model;

/// <summary>A bucket's name: 3 to 63 lowercase letters, digits, '.' and '-'.</summary>
public readonly record struct S3Bucket
{
    /// <summary>A bucket name.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a bucket name.</exception>
    public S3Bucket(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        if (value.Length is < 3 or > 63)
        {
            throw new ArgumentException("A bucket name is 3 to 63 characters.", nameof(value));
        }

        foreach (char c in value)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-'))
            {
                throw new ArgumentException("'" + value + "' is not a bucket name: lowercase letters, digits, '.' and '-'.", nameof(value));
            }
        }

        Value = value;
    }

    /// <summary>The name.</summary>
    public string Value { get; }

    /// <summary>Renders as the name.</summary>
    public override string ToString() => Value;
}
