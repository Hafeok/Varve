// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Varve.Store.Log;

namespace Varve.Store.ObjectStorage.Model;

/// <summary>
/// What <c>varve storage</c> reports of a dataset in an object store (ADR
/// 0162): the catalog's sets with their bytes per permutation, the sets no
/// catalog names that the store still holds, and what each reader record
/// holds of them. Read from the store alone; changes nothing.
/// </summary>
public sealed record StorageReport
{
    /// <summary>The catalog, or none when the dataset has no checkpoint yet.</summary>
    public CatalogSummary? Catalog { get; init; }

    /// <summary>Each set the catalog names, oldest first, with what its blobs take.</summary>
    public required IReadOnlyList<StoredSet> Sets { get; init; }

    /// <summary>The sets no catalog names that the store still holds, waiting for no record to hold them.</summary>
    public required IReadOnlyList<BlobName> UnnamedSets { get; init; }

    /// <summary>The bytes of those sets.</summary>
    public required ByteCount RetainedBytes { get; init; }

    /// <summary>Every reader record, and the bytes of the unnamed sets it holds.</summary>
    public required IReadOnlyList<ReaderHolding> Readers { get; init; }
}

/// <summary>An extent set the catalog names, and the bytes of each of its blobs.</summary>
/// <param name="Set">The set.</param>
/// <param name="Orders">The bytes of each order's extent, in index order: SPOG, POSG, OSPG, GSPO, GPOS, GOSP.</param>
/// <param name="Rest">The bytes of its dictionary, graph-version index and commit index.</param>
public sealed record StoredSet(CatalogSet Set, IReadOnlyList<ByteCount> Orders, ByteCount Rest);

/// <summary>A reader record, and the bytes of the sets no catalog names that it holds.</summary>
/// <param name="Record">The record.</param>
/// <param name="RetainedBytes">The bytes it holds back from deletion.</param>
public sealed record ReaderHolding(ReaderRecord Record, ByteCount RetainedBytes);
