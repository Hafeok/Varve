// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;

namespace Varve.Store.Log;

/// <summary>
/// An object's content staged in a file (ADR 0158): a create streams it, so a
/// large derived blob is never held whole in memory. Each attempt opens the
/// file again, so a retry sends the same bytes.
/// </summary>
public sealed class ObjectContent
{
    private readonly FileInfo _file;

    /// <summary>The file's content, as it is now; it must not change while the create runs.</summary>
    /// <exception cref="FileNotFoundException">There is no such file.</exception>
    public ObjectContent(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);
        file.Refresh();

        if (!file.Exists)
        {
            throw new FileNotFoundException("No staged file.", file.FullName);
        }

        _file = file;
        Length = new ByteCount(file.Length);
    }

    /// <summary>How many bytes the content is.</summary>
    public ByteCount Length { get; }

    /// <summary>A stream of the content from its start, for one attempt.</summary>
    public Stream OpenRead() => new FileStream(_file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
}
