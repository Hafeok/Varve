// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// A projection catalog as an operator reads it (ADR 0162): its dataset, its
/// version, the position it reaches and the extent sets it names, oldest
/// first. Read from the catalog's file alone, without the log: what
/// <c>varve storage</c> reports.
/// </summary>
/// <param name="Dataset">The dataset its header names.</param>
/// <param name="Version">Its version: its next set sequence (ADR 0160).</param>
/// <param name="Position">The position its chain reaches.</param>
/// <param name="Sets">The sets it names, oldest first.</param>
public sealed record CatalogSummary(DatasetId Dataset, CatalogVersion Version, Position Position, IReadOnlyList<CatalogSet> Sets)
{
    /// <summary>The catalog a file holds, or none when it is not one this build reads, or does not verify.</summary>
    public static CatalogSummary? TryRead(ReadOnlyMemory<byte> file)
    {
        using BytesBlob blob = new(file);

        if (ExtentCatalog.TryParse(blob, null) is not { } read)
        {
            return null;
        }

        CatalogSet[] sets = new CatalogSet[read.Sets.Length];

        for (int i = 0; i < sets.Length; i++)
        {
            sets[i] = new CatalogSet(new BlobName(read.Sets[i].Name), new Position(read.Sets[i].From), new Position(read.Sets[i].To));
        }

        DatasetId dataset = DatasetId.Read(file.Span.Slice(file.Length - DerivedFormat.HeaderLength + 8, DatasetId.Length));
        return new CatalogSummary(dataset, new CatalogVersion(read.Sequence), new Position(read.Header.To), sets);
    }

    private sealed class BytesBlob(ReadOnlyMemory<byte> bytes) : IReadableBlob
    {
        public ByteCount Length => new(bytes.Length);

        public int Read(ByteOffset offset, Span<byte> destination)
        {
            if (offset.Value >= bytes.Length)
            {
                return 0;
            }

            int count = (int)Math.Min(destination.Length, bytes.Length - offset.Value);
            bytes.Span.Slice((int)offset.Value, count).CopyTo(destination);
            return count;
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>An extent set a catalog names: the prefix of its extents' names, ending in <c>/</c>, and its window.</summary>
/// <param name="Name">The set's name.</param>
/// <param name="From">The position its window starts after.</param>
/// <param name="To">The position its window ends at.</param>
public sealed record CatalogSet(BlobName Name, Position From, Position To)
{
    /// <summary>The version its name's sequence is: only versions after it name the set (ADR 0160).</summary>
    public CatalogVersion CreatedAfter => new(Math.Max(0, ExtentSet.SequenceOf(Name.Value)));

    /// <summary>The names of the blobs the set stands for: six order extents, its dictionary, its graph-version index and its commit index.</summary>
    public IEnumerable<BlobName> Blobs => ExtentSet.BlobsOf(Name.Value);

    /// <summary>The sequence of the set a blob name belongs to, or none when the name is not an extent's.</summary>
    public static CatalogVersion? SequenceOf(BlobName blob) =>
        ExtentSet.SequenceOf(blob.Value) is long sequence and >= 0 ? new CatalogVersion(sequence) : null;
}
