// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Varve.Store;

/// <summary>
/// What the store keeps for readers (ADR 0160, ADR 0162): the extent sets no
/// catalog names that are not yet deleted, the bytes they take, and what each
/// reader record holds of them. A set two records hold counts for both, so the
/// records' bytes can add up to more than the total.
/// </summary>
/// <param name="Version">The catalog version the figures were taken at.</param>
/// <param name="Sets">The sets the catalog names.</param>
/// <param name="UnnamedSets">The sets no catalog names that the store still holds.</param>
/// <param name="RetainedBytes">The bytes of those sets.</param>
/// <param name="Readers">Each present reader record, and the bytes of the unnamed sets it holds.</param>
public sealed record ExtentRetention(Log.CatalogVersion Version, int Sets, int UnnamedSets, long RetainedBytes, IReadOnlyList<ReaderRetention> Readers)
{
    /// <summary>
    /// The named sets not yet checked block by block (ADR 0213): written while
    /// a reader of derived format 5 or older was announced, and read
    /// unchecked until maintenance rewrites them once no such reader remains.
    /// </summary>
    public int UncheckedSets { get; init; }
}

/// <summary>A reader record, and the bytes of the unnamed extent sets it holds.</summary>
/// <param name="Announcement">What the record announces.</param>
/// <param name="RetainedBytes">The bytes of the sets no catalog names that it holds.</param>
public sealed record ReaderRetention(Log.ReaderAnnouncement Announcement, long RetainedBytes);
