// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Varve.Store.Log;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// A reader record as an operator reads it (ADR 0160): <c>readers/&lt;id&gt;</c>
/// under the dataset's prefix, JSON, replaced by its reader only when what it
/// announces changes.
/// </summary>
public sealed record ReaderRecord
{
    /// <summary>The reader's id, the record's name.</summary>
    public required ReaderId Reader { get; init; }

    /// <summary>The name the instance gives itself; none in a record of an unknown format.</summary>
    public WriterInstance? Holder { get; init; }

    /// <summary>The oldest catalog version its reads hold.</summary>
    public required CatalogVersion Oldest { get; init; }

    /// <summary>The newest catalog version its reads hold.</summary>
    public required CatalogVersion Newest { get; init; }

    /// <summary>When it was last replaced, by the host's clock: for the operator, never compared (ADR 0160).</summary>
    public ClaimTime? AnnouncedAt { get; init; }

    /// <summary>Whether it announces, or was forgotten.</summary>
    public ReaderRecordState State { get; init; }

    /// <summary>False for a record of a format this build does not read, which holds every catalog version.</summary>
    public bool Understood { get; init; } = true;

    /// <summary>What it announces, as the deletion rule reads it.</summary>
    public ReaderAnnouncement Announcement => Understood
        ? new ReaderAnnouncement(Reader, Oldest, Newest)
        : ReaderAnnouncement.OfUnknownFormat(Reader);
}
