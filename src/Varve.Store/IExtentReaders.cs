// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// The reader records of a storage that more than one instance reads extents
/// from: the object-storage backend's (ADR 0160). A storage without it — a
/// directory, held by one process under the OS lock — has one reader, and a
/// set no catalog names is deleted as soon as no view holds it.
/// </summary>
/// <remarks>
/// <para>
/// **Every instance announces**, the writer too: the oldest and the newest
/// catalog versions its reads hold. A reader announces a version and reads the
/// catalog again before reading an extent of it; the writer announces a
/// version before swapping to it.
/// </para>
/// <para>
/// **The writer deletes after the swap.** It reads the records after the swap
/// that stopped naming a set, and deletes the set only when no record holds it
/// (<see cref="ReaderAnnouncement.Holds"/>). A dead reader's record holds
/// deletion back until the same instance restarts and replaces it, or the
/// operator forgets it. Nothing is decided from elapsed time.
/// </para>
/// <para>
/// This is the contract the Management plane's follower (#105) uses as it
/// is: a follower opened read-only announces through the dataset's one path
/// that attaches a catalog.
/// </para>
/// </remarks>
[Contract(typeof(ExtentDeletionByReaderAnnouncements.EveryInstanceAnnouncesAnInterval), Role = "the reader records of a storage shared by several instances")]
public interface IExtentReaders
{
    /// <summary>
    /// Whether this instance holds the writer's epoch, and so merges and
    /// deletes: only the writer does (ADR 0158, ADR 0160).
    /// </summary>
    bool DeletesExtents { get; }

    /// <summary>
    /// Replaces this instance's record, when what it announces changes, with
    /// the oldest and newest catalog versions its reads hold.
    /// </summary>
    /// <exception cref="ReaderForgottenException">The record was forgotten, or replaced by another instance of the same id.</exception>
    ValueTask AnnounceAsync(CatalogVersion oldest, CatalogVersion newest, CancellationToken cancellationToken);

    /// <summary>Every present record but a forgotten one, this instance's included.</summary>
    ValueTask<IReadOnlyList<ReaderAnnouncement>> ReadAnnouncementsAsync(CancellationToken cancellationToken);

    /// <summary>The bytes the storage holds under each extent set's name, for what a record retains.</summary>
    ValueTask<IReadOnlyDictionary<string, long>> ExtentBytesAsync(CancellationToken cancellationToken);
}
