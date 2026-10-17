// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// The local directory an object-storage dataset caches in: copies of sealed
/// segments, and derived data that need not be in the store. One process
/// uses it at a time. Absolute.
/// </summary>
public readonly record struct CacheDirectory
{
    /// <summary>A cache directory.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty or not an absolute path.</exception>
    public CacheDirectory(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        if (!Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException("The cache directory '" + value + "' is not an absolute path.", nameof(value));
        }

        Value = Path.GetFullPath(value);
    }

    /// <summary>The path, normalised.</summary>
    public string Value { get; }

    /// <summary>Renders as the path.</summary>
    public override string ToString() => Value;
}
