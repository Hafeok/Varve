// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// The kinds of local copy the object-storage backend keeps of what the store
/// holds (#146; ADR 0179 point 6). A write that fails turns the kind off: it
/// is read from the store from then on, and the writer goes on committing.
/// </summary>
[Flags]
public enum LocalCache : byte
{
    /// <summary>No kind.</summary>
    None = 0,

    /// <summary>The copies of consolidated log segments.</summary>
    SegmentCopies = 1,

    /// <summary>The page files of derived extents.</summary>
    ExtentPages = 2,
}
