// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.IO;
using System.Threading;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// The whole of one blob of an extent set, checked as a reader of the set
/// checks the parts it reads (ADR 0213): for a host that copies a set to
/// another store and must not copy a damaged one (ADR 0184 decision 5).
/// </summary>
/// <remarks>
/// <para>
/// **What is checked.** The header's own hash, that the blob is of
/// <c>dataset</c> and of the kind its name says, the directory under the
/// header's SHA-256, and then every block that carries a check: each key
/// block, each column block, the rows within and the seek directory of an
/// order extent; every page of a dictionary; every block of a commit index;
/// the whole of a graph-version index.
/// </para>
/// <para>
/// **What is not.** A blob of a version from before the checks (ADR 0213
/// point 7) has none below its directory. It is read whole all the same, so
/// a block that does not decode is found, and it passes when every block
/// decodes: that is all any reader of it can know. Whether the blob is bound
/// to a commit of the dataset's log is the reader's check against its own
/// log, and is not made here.
/// </para>
/// </remarks>
public static class ExtentBlobCheck
{
    /// <summary>
    /// Whether <paramref name="file"/>, holding the bytes of the blob
    /// <paramref name="name"/> of an extent set of <paramref name="dataset"/>,
    /// verifies. False for a name that is not a blob of a set this build
    /// knows: it cannot be said to verify. It reads the whole file, so a host
    /// calls it off the threads that serve requests.
    /// </summary>
    /// <param name="name">The blob's name, as the catalog's set gives it (<see cref="CatalogSet.Blobs"/>).</param>
    /// <param name="dataset">The dataset the set is of (<see cref="CatalogSummary.Dataset"/>).</param>
    /// <param name="file">A file of this machine holding the blob's bytes. It is only read.</param>
    /// <param name="cancellationToken">Stops the check between blocks.</param>
    /// <exception cref="FileNotFoundException"><paramref name="file"/> does not exist.</exception>
    public static bool Verifies(BlobName name, DatasetId dataset, FileInfo file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        string blob = name.Value;
        FileStorage.FileBlob content = new(new DiskFileSystem().Open(file.FullName, FileOpen.Read));
        bool held = false;

        try
        {
            if (blob.EndsWith("graphs", StringComparison.Ordinal))
            {
                return GraphVersions.TryRead(content, dataset) is not null;
            }

            if (blob.EndsWith("commits", StringComparison.Ordinal))
            {
                if (CommitIndexFormat.TryLoad(content, name, dataset, shared: true) is not { } segment)
                {
                    return false;
                }

                held = true;
                segment.Blob.Release();
                return true;
            }

            bool terms = blob.EndsWith("terms", StringComparison.Ordinal);

            if (!terms && !IsOrder(blob))
            {
                return false;
            }

            if (DerivedFormat.TryLoad(content, name, dataset, terms ? DerivedFormat.KindExtentTerms : DerivedFormat.KindExtent, shared: terms) is not { } loaded)
            {
                return false;
            }

            held = true;
            return Whole(loaded, cancellationToken);
        }
        catch (IOException)
        {
            // A block's check failed, or a block of an unchecked version did not decode or came up short.
            return false;
        }
        finally
        {
            // A loaded run or segment owns the blob, and released it.
            if (!held)
            {
                content.Dispose();
            }
        }
    }

    private static bool IsOrder(string blob)
    {
        for (int o = 0; o < Store.Orders.Count; o++)
        {
            if (blob.EndsWith("order" + o, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Every block of a loaded run or extent: its rows, where it is an extent, and its dictionary's pages.
    private static bool Whole(LoadedRun loaded, CancellationToken cancellationToken)
    {
        try
        {
            if (loaded.Extent is not null)
            {
                using ExtentRows rows = new(loaded);
                ExtentRow[] buffer = new ExtentRow[1024];

                while (rows.Next(buffer) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            TermSection terms = loaded.Run.Terms;

            // An order extent carries no terms, and below the version whose term pages are checked its empty
            // section is under no check: an offset of zero and a filter of no bits, so every byte of it is zero.
            if (loaded.Extent is not null && loaded.Header.Version < DerivedFormat.CheckedVersion)
            {
                (long at, long end) = loaded.TermBytes;
                byte[] empty = new byte[end - at];

                if (terms.Count != 0 || loaded.Run.Blob!.Blob.Read(new ByteOffset(at), empty) != empty.Length || empty.AsSpan().ContainsAnyExcept((byte)0))
                {
                    return false;
                }
            }

            if (terms.OnBlob && terms.Count > 0)
            {
                byte[] bytes = new byte[1 << 16];

                for (long at = 0; at < terms.EntriesLength; at += bytes.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    terms.ReadEntries(at, bytes.AsSpan(0, (int)Math.Min(bytes.Length, terms.EntriesLength - at)));
                }

                long[] offsets = new long[4096];

                for (long at = 0; at < terms.Count + 1; at += offsets.Length)
                {
                    terms.ReadOffsets(at, offsets.AsSpan(0, (int)Math.Min(offsets.Length, terms.Count + 1 - at)));
                }

                TermHash[] hashes = new TermHash[4096];

                for (long at = 0; at < terms.Count; at += hashes.Length)
                {
                    terms.ReadHashes(at, hashes.AsSpan(0, (int)Math.Min(hashes.Length, terms.Count - at)));
                }
            }

            return true;
        }
        finally
        {
            loaded.Run.Blob!.Release();
        }
    }
}
