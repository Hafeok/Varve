// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// A projection's bytes kept in a derived store by a package other than the
/// store's, through <see cref="Dataset.WriteCheckedAsync"/> and
/// <see cref="Dataset.TryReadCheckedAsync"/>: the validation projection (ADR
/// 0174), the inference projection (#171). Written in derived format 6 as a blob of kind 9, the checked
/// envelope: the bytes in blocks of 64 KiB, a directory of one 64-bit check a
/// block, and the derived header, which binds it to the dataset and to the
/// position it was built at by the log's header hash there (storage format
/// §7.7, ADR 0213). Read back only whole and verified; anything else is a
/// miss, and the projection is rebuilt from the log.
/// </summary>
[DesignDecision(typeof(VerifiedDerivedData.ProjectionsOutsideTheStoreWriteACheckedEnvelope), Scope = ExceptionScope.Boundary)]
internal static class CheckedBlob
{
    /// <summary>The bytes a block of the envelope holds; the last may hold fewer.</summary>
    internal const int BlockBytes = 64 * 1024;

    /// <summary>
    /// Writes <paramref name="bytes"/>, built at <paramref name="position"/>
    /// of <paramref name="dataset"/>, as <paramref name="name"/> in
    /// <paramref name="store"/>, replacing any blob of that name.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is not a position the dataset has reached.</exception>
    internal static async ValueTask WriteAsync(IDerivedStore store, BlobName name, Dataset dataset, Position position, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position.Value, dataset.Head.Value, nameof(position));
        byte[] hash = dataset.HeaderHashAt(position.Value);
        long blocks = (bytes.Length + BlockBytes - 1) / BlockBytes;
        byte[] directory = new byte[8 + (blocks * 8)];
        BinaryPrimitives.WriteInt64LittleEndian(directory, bytes.Length);

        for (long i = 0; i < blocks; i++)
        {
            ReadOnlySpan<byte> block = bytes.Span.Slice((int)(i * BlockBytes), (int)Math.Min(BlockBytes, bytes.Length - (i * BlockBytes)));
            BinaryPrimitives.WriteUInt64LittleEndian(directory.AsSpan((int)(8 + (i * 8))), BlockCheck.Of(block));
        }

        byte[] header = DerivedFormat.EncodeHeader(
            DerivedFormat.CheckedVersion, DerivedFormat.KindEnvelope, dataset.Id, 0, position.Value, hash, bytes.Length, directory.Length, SHA256.HashData(directory));
        await using IBlobWriter writer = await store.CreateAsync(name, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(directory, cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await writer.PublishAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The bytes written as <paramref name="name"/>, every block verified, or
    /// null: when there is no such blob, when it is not an envelope this
    /// build reads, when any byte of it is damaged, or when it is bound to
    /// another dataset or to a position the dataset's log does not hold with
    /// the same header hash. Null is a miss, never an error.
    /// </summary>
    [DesignDecision(typeof(VerifiedDerivedData.VerifiedBeforeFirstUse), Scope = ExceptionScope.Boundary)]
    internal static async ValueTask<CheckedContent?> TryReadAsync(IDerivedStore store, BlobName name, Dataset dataset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dataset);
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
            if (!DerivedFormat.TryReadHeader(blob, dataset.Id, DerivedFormat.KindEnvelope, out DerivedHeader header)
                || header.To > dataset.Head.Value
                || !dataset.HeaderHashAt(header.To).AsSpan().SequenceEqual(header.ToHash)
                || header.DirectoryOffset > int.MaxValue)
            {
                return null;
            }

            long length = header.DirectoryOffset;
            long blocks = (length + BlockBytes - 1) / BlockBytes;
            byte[] directory = new byte[header.DirectoryLength];

            if (header.DirectoryLength != 8 + (blocks * 8)
                || blob.Read(new ByteOffset(length), directory) != directory.Length
                || !SHA256.HashData(directory).AsSpan().SequenceEqual(header.DirectoryHash)
                || BinaryPrimitives.ReadInt64LittleEndian(directory) != length)
            {
                return null;
            }

            byte[] bytes = new byte[length];

            if (blob.Read(new ByteOffset(0), bytes) != bytes.Length)
            {
                return null;
            }

            for (long i = 0; i < blocks; i++)
            {
                ReadOnlySpan<byte> block = bytes.AsSpan((int)(i * BlockBytes), (int)Math.Min(BlockBytes, length - (i * BlockBytes)));

                if (BlockCheck.Of(block) != BinaryPrimitives.ReadUInt64LittleEndian(directory.AsSpan((int)(8 + (i * 8)))))
                {
                    return null;
                }
            }

            return new CheckedContent(new Position(header.To), bytes);
        }
    }
}
