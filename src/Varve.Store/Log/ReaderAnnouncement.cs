// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Store.Log;

/// <summary>
/// What a reader record announces (ADR 0160): the oldest and the newest
/// catalog versions the reader's reads hold. A record of a format this build
/// does not know is read as holding every version, so it blocks deletion
/// rather than being ignored.
/// </summary>
/// <param name="Reader">The record's reader.</param>
/// <param name="Oldest">The oldest catalog version its reads hold.</param>
/// <param name="Newest">The newest catalog version its reads hold.</param>
public sealed record ReaderAnnouncement(ReaderId Reader, CatalogVersion Oldest, CatalogVersion Newest)
{
    /// <summary>False for a record of a format this build does not read, which holds every version.</summary>
    public bool Understood { get; init; } = true;

    /// <summary>
    /// The newest derived format the reader reads (ADR 0178): 4 for a record
    /// that does not say, as one written before #133, and for one of a format
    /// this build does not read. While a present record says less than 5, the
    /// writer leaves format-4 extent sets as they are.
    /// </summary>
    [DesignDecision(typeof(VerifiedDerivedData.ReaderRecordsStateMaxDerived), Scope = ExceptionScope.Boundary)]
    public DerivedVersion MaxDerived { get; init; } = DerivedVersion.Format4;

    /// <summary>A record of a format this build does not read: it holds every version (ADR 0160).</summary>
    public static ReaderAnnouncement OfUnknownFormat(ReaderId reader) =>
        new(reader, new CatalogVersion(0), CatalogVersion.Newest) { Understood = false };

    /// <summary>
    /// Whether this announcement holds an extent set that versions after
    /// <paramref name="createdAfter"/> may name — the sequence its name
    /// carries — and that the catalog stopped naming at
    /// <paramref name="removedAt"/>: some version it holds lies strictly
    /// between them, so a catalog it reads may name the set.
    /// </summary>
    public bool Holds(CatalogVersion createdAfter, CatalogVersion removedAt) =>
        Oldest.Value < removedAt.Value && Newest.Value > createdAfter.Value && removedAt.Value > createdAfter.Value + 1;
}
