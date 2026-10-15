// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.Log;

/// <summary>
/// An object's version as its store names it: an entity tag, or a
/// generation. Opaque; compared only for equality, and handed back to
/// <see cref="IObjectStore.ReplaceIfUnchangedAsync"/>.
/// </summary>
public readonly record struct ObjectVersion
{
    /// <summary>A version.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null or empty.</exception>
    public ObjectVersion(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>The version, exactly as the store gave it.</summary>
    public string Value { get; }

    /// <summary>Renders as the version itself.</summary>
    public override string ToString() => Value;
}
