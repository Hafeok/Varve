// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// The graph-version index (ADR 0125): for each graph, the position of the
/// last commit at or before <see cref="Position"/> that touched it, and the
/// position of the last commit that touched every graph — a <c>Settings</c>
/// or <c>Erasure</c> commit. Immutable: each commit makes a new version, and a
/// published state holds the one at its head.
/// </summary>
/// <remarks>
/// Part of the default projection: maintained as each commit is staged,
/// persisted in <c>derived/</c> as <see cref="HeadName"/> and beside each
/// checkpoint, and rebuilt from the log when either is absent or names
/// another log (I8).
/// </remarks>
internal sealed class GraphVersions
{
    /// <summary>The head's index, written by maintenance.</summary>
    internal static readonly BlobName HeadName = new("index/graphs");

    /// <summary>A checkpoint's index, beside the checkpoint: <c>checkpoints/&lt;P&gt;.graphs</c>.</summary>
    internal const string Suffix = ".graphs";

    private const int EntryBytes = 16;
    private const int DirectoryHead = 16;

    private readonly ImmutableDictionary<ulong, long> _versions;

    internal GraphVersions(ImmutableDictionary<ulong, long> versions, long all, long position)
    {
        _versions = versions;
        All = all;
        Position = position;
    }

    internal static GraphVersions Empty { get; } = new(ImmutableDictionary<ulong, long>.Empty, 0, 0);

    /// <summary>The position this index is at.</summary>
    internal long Position { get; }

    /// <summary>The last commit that touched every graph; 0 when none has.</summary>
    internal long All { get; }

    /// <summary>How many graphs a commit has touched.</summary>
    internal int Count => _versions.Count;

    internal static BlobName NameAt(long position) => new(Checkpoint.Name(position).Value + Suffix);

    /// <summary>The version of a graph: the last commit that touched it, or every graph.</summary>
    internal long VersionOf(ulong graph) => Math.Max(All, _versions.GetValueOrDefault(graph));

    /// <summary>Whether some commit after <paramref name="since"/> touched a graph of the scope (T1 step 1).</summary>
    internal bool Touched(ReadOnlySpan<ulong> scope, long since)
    {
        if (All > since)
        {
            return true;
        }

        foreach (ulong graph in scope)
        {
            if (_versions.GetValueOrDefault(graph) > since)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>This index after a commit at <paramref name="position"/>.</summary>
    internal GraphVersions Apply(CommitKind kind, ReadOnlySpan<Quad> asserted, ReadOnlySpan<Quad> retracted, long position)
    {
        if (kind != CommitKind.Data)
        {
            return new GraphVersions(_versions, position, position);
        }

        ulong[] rented = ArrayPool<ulong>.Shared.Rent(Math.Max(1, asserted.Length + retracted.Length));

        try
        {
            int count = Distinct(asserted, retracted, rented);
            return With(rented.AsSpan(0, count), position);
        }
        finally
        {
            ArrayPool<ulong>.Shared.Return(rented);
        }
    }

    /// <summary>This index with the given distinct graphs touched at <paramref name="position"/>.</summary>
    internal GraphVersions With(ReadOnlySpan<ulong> graphs, long position)
    {
        ImmutableDictionary<ulong, long> versions = _versions;

        if (graphs.Length <= 8)
        {
            foreach (ulong graph in graphs)
            {
                versions = versions.SetItem(graph, position);
            }
        }
        else
        {
            ImmutableDictionary<ulong, long>.Builder builder = versions.ToBuilder();

            foreach (ulong graph in graphs)
            {
                builder[graph] = position;
            }

            versions = builder.ToImmutable();
        }

        return new GraphVersions(versions, All, position);
    }

    /// <summary>This index moved to <paramref name="position"/> by a commit that touched no graph of interest.</summary>
    internal GraphVersions At(long position) => new(_versions, All, position);

    /// <summary>
    /// The distinct graphs of a delta, in <paramref name="into"/>, sorted;
    /// returns how many. Per commit, never per quad: the quads' graph ids are
    /// copied, sorted in place and made unique.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static int Distinct(ReadOnlySpan<Quad> asserted, ReadOnlySpan<Quad> retracted, Span<ulong> into)
    {
        int count = 0;

        for (int i = 0; i < asserted.Length; i++)
        {
            into[count++] = asserted[i].Graph.Value;
        }

        for (int i = 0; i < retracted.Length; i++)
        {
            into[count++] = retracted[i].Graph.Value;
        }

        Span<ulong> graphs = into[..count];
        graphs.Sort();
        int unique = 0;

        for (int i = 0; i < graphs.Length; i++)
        {
            if (unique == 0 || graphs[unique - 1] != graphs[i])
            {
                graphs[unique++] = graphs[i];
            }
        }

        return unique;
    }

    /// <summary>The graphs a run's half holds, distinct: a seek past each graph in the graph-first order, so a bulk commit costs a seek per graph.</summary>
    internal static void DistinctGraphs(KeySection section, List<ulong> into)
    {
        Span<QuadKey> block = new QuadKey[KeySection.BlockKeys];
        long at = 0;

        while (at < section.Count)
        {
            ulong graph = KeyAt(section, at, block).K0;
            into.Add(graph);

            if (graph == ulong.MaxValue)
            {
                return;
            }

            at = section.LowerBound(new QuadKey(graph + 1, 0, 0, 0));
        }
    }

    private static QuadKey KeyAt(KeySection section, long index, Span<QuadKey> block)
    {
        if (!section.OnBlob)
        {
            return section.Memory.Span[(int)index];
        }

        section.ReadBlock(index / KeySection.BlockKeys, block);
        return block[(int)(index % KeySection.BlockKeys)];
    }

    /// <summary>
    /// Writes the index as a derived blob: its directory — the all-graphs
    /// version, the count, then each graph and its version sorted by graph —
    /// and the derived header naming its position and that commit's hash.
    /// </summary>
    internal async ValueTask WriteAsync(IDerivedStore store, BlobName name, DatasetId dataset, byte[] positionHash, CancellationToken cancellationToken)
    {
        KeyValuePair<ulong, long>[] entries = [.. _versions];
        Array.Sort(entries, (a, b) => a.Key.CompareTo(b.Key));
        byte[] directory = new byte[DirectoryHead + (entries.Length * EntryBytes)];
        BinaryPrimitives.WriteInt64LittleEndian(directory, All);
        BinaryPrimitives.WriteInt64LittleEndian(directory.AsSpan(8), entries.Length);

        for (int i = 0; i < entries.Length; i++)
        {
            Span<byte> entry = directory.AsSpan(DirectoryHead + (i * EntryBytes), EntryBytes);
            BinaryPrimitives.WriteUInt64LittleEndian(entry, entries[i].Key);
            BinaryPrimitives.WriteInt64LittleEndian(entry[8..], entries[i].Value);
        }

        byte[] header = DerivedFormat.EncodeHeader(DerivedFormat.KindGraphs, dataset, 0, Position, positionHash, 0, directory.Length, SHA256.HashData(directory));
        await using IBlobWriter writer = await store.CreateAsync(name, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(directory, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await writer.PublishAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The index a blob holds, verified whole, with the hash of the commit it names; null when absent or not one.</summary>
    internal static async ValueTask<(GraphVersions Versions, byte[] Hash)?> TryReadAsync(IDerivedStore store, BlobName name, DatasetId dataset, CancellationToken cancellationToken)
    {
        IReadableBlob blob;

        try
        {
            blob = await store.OpenAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }

        using (blob)
        {
            if (!DerivedFormat.TryReadHeader(blob, dataset, DerivedFormat.KindGraphs, out DerivedHeader header)
                || header.DirectoryOffset != 0
                || header.DirectoryLength < DirectoryHead
                || (header.DirectoryLength - DirectoryHead) % EntryBytes != 0)
            {
                return null;
            }

            byte[] directory = new byte[header.DirectoryLength];

            if (blob.Read(new ByteOffset(0), directory) != directory.Length || !LogFormat.HashMatches(directory, header.DirectoryHash))
            {
                return null;
            }

            long all = BinaryPrimitives.ReadInt64LittleEndian(directory);
            long count = BinaryPrimitives.ReadInt64LittleEndian(directory.AsSpan(8));

            if (count != (directory.Length - DirectoryHead) / EntryBytes)
            {
                return null;
            }

            ImmutableDictionary<ulong, long>.Builder builder = ImmutableDictionary.CreateBuilder<ulong, long>();

            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<byte> entry = directory.AsSpan(DirectoryHead + (i * EntryBytes), EntryBytes);
                builder[BinaryPrimitives.ReadUInt64LittleEndian(entry)] = BinaryPrimitives.ReadInt64LittleEndian(entry[8..]);
            }

            return (new GraphVersions(builder.ToImmutable(), all, header.To), header.ToHash);
        }
    }

    /// <summary>
    /// One graph's version in a blob, by binary search over its sorted
    /// entries: a few small reads, never the whole index. Null when the blob
    /// is absent, not one, or names another commit than <paramref name="hash"/>.
    /// Derived files are published whole (ADR 0071), so the entries read are
    /// those the header's hash covers.
    /// </summary>
    internal static async ValueTask<long?> TryLookupAsync(IDerivedStore store, BlobName name, DatasetId dataset, ReadOnlyMemory<byte> hash, ulong graph, CancellationToken cancellationToken)
    {
        IReadableBlob blob;

        try
        {
            blob = await store.OpenAsync(name, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }

        using (blob)
        {
            if (!DerivedFormat.TryReadHeader(blob, dataset, DerivedFormat.KindGraphs, out DerivedHeader header)
                || !hash.Span.SequenceEqual(header.ToHash)
                || header.DirectoryLength < DirectoryHead)
            {
                return null;
            }

            byte[] head = new byte[DirectoryHead];
            blob.Read(new ByteOffset(0), head);
            long all = BinaryPrimitives.ReadInt64LittleEndian(head);
            long low = 0;
            long high = BinaryPrimitives.ReadInt64LittleEndian(head.AsSpan(8)) - 1;
            byte[] entry = new byte[EntryBytes];

            while (low <= high)
            {
                long middle = low + ((high - low) / 2);
                blob.Read(new ByteOffset(DirectoryHead + (middle * EntryBytes)), entry);
                ulong key = BinaryPrimitives.ReadUInt64LittleEndian(entry);

                if (key == graph)
                {
                    return Math.Max(all, BinaryPrimitives.ReadInt64LittleEndian(entry.AsSpan(8)));
                }

                if (key < graph)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return all;
        }
    }
}
