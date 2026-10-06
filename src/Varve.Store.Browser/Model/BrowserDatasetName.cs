// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.Browser.Model;

/// <summary>
/// Where a dataset lives in a browser origin's storage: one or more parts of
/// ASCII letters, digits, <c>-</c>, <c>_</c> and <c>.</c>, separated by
/// <c>/</c>. A directory path in the origin private file system, and the
/// database's name in IndexedDB (ADR 0084).
/// </summary>
/// <remarks>
/// The browser's counterpart of a dataset directory. It is never a host file
/// system path: the origin's storage has no root outside itself.
/// </remarks>
public readonly record struct BrowserDatasetName
{
    /// <summary>A dataset name.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a name of the form above.</exception>
    public BrowserDatasetName(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);

        foreach (string part in value.Split('/'))
        {
            if (part.Length == 0 || part == "." || part == "..")
            {
                throw new ArgumentException("'" + value + "' is not a dataset name: every part between '/' is non-empty, not '.' or '..'.", nameof(value));
            }

            foreach (char c in part)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
                {
                    throw new ArgumentException("'" + value + "' is not a dataset name: a part holds ASCII letters, digits, '-', '_' and '.'.", nameof(value));
                }
            }
        }

        Value = value;
    }

    /// <summary>The name.</summary>
    public string Value { get; }

    /// <summary>Renders as the name.</summary>
    public override string ToString() => Value;
}
