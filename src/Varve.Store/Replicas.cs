// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>Replica bootstrap: the dataset's bytes, copied to another storage (ADR 0083).</summary>
public sealed partial class Dataset
{
    private const int ShipChunk = 1 << 20;

    /// <summary>
    /// Copies the dataset as it stands at <paramref name="position"/> into
    /// empty <paramref name="target"/> storage, as files: the manifest, the
    /// log up to the end of that position's commit — every segment before it
    /// whole, sealed as it is, and the one it ends in cut there — and the
    /// newest checkpoint at or below it, written first if there is none.
    /// Opening the target gives a dataset at exactly that position that
    /// replays only the log after the checkpoint. No protocol: the bytes are
    /// the source's, and the target continues as a dataset of its own.
    /// </summary>
    /// <returns>The position shipped.</returns>
    /// <exception cref="InvalidOperationException">The target already holds a dataset, or part of one.</exception>
    public async ValueTask<Position> ShipAsync(IStorage target, Position position, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        State state = _state;
        long at = position.Value;
        ArgumentOutOfRangeException.ThrowIfNegative(at);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(at, state.Head);

        if (!(await target.Log.ReadManifestAsync(cancellationToken).ConfigureAwait(false)).IsEmpty
            || (await target.Log.ListSegmentsAsync(cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            throw new InvalidOperationException("The target storage already holds a dataset.");
        }

        if (at > 0 && state.CheckpointAtOrBelow(at) is null)
        {
            await CheckpointCoreAsync(at, cancellationToken).ConfigureAwait(false);
        }

        await target.Log.WriteManifestAsync(await _storage.Log.ReadManifestAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

        if (at > 0)
        {
            CommitInfo last = state.Commits[at - 1];
            byte[] previous = at == 1 ? LogFormat.Genesis() : state.Commits[at - 2].HeaderHash;
            CommitLocation end = await LogReader.EndAsync(_storage.Log, Id, last.Location, at, previous, cancellationToken).ConfigureAwait(false);

            foreach (SegmentInfo segment in await _storage.Log.ListSegmentsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (segment.Id.Value > end.Segment)
                {
                    break;
                }

                bool whole = segment.Id.Value < end.Segment;
                await CopySegmentAsync(target.Log, segment, whole ? segment.Length.Value : end.Offset, seal: whole, cancellationToken).ConfigureAwait(false);
            }

            Checkpoint checkpoint = _state.CheckpointAtOrBelow(at)!;
            await CopyBlobAsync(target.Derived, checkpoint.Run.Blob!.Name, cancellationToken).ConfigureAwait(false);
        }

        return new Position(at);
    }

    private async ValueTask CopySegmentAsync(ISegmentStore target, SegmentInfo segment, long length, bool seal, CancellationToken cancellationToken)
    {
        SegmentId created = await target.CreateSegmentAsync(cancellationToken).ConfigureAwait(false);

        if (created != segment.Id)
        {
            throw new InvalidOperationException("The target numbered segment " + segment.Id + " as " + created + ".");
        }

        for (long offset = 0; offset < length; offset += ShipChunk)
        {
            ByteCount count = new(Math.Min(ShipChunk, length - offset));
            ReadOnlyMemory<byte> bytes = await _storage.Log.ReadRangeAsync(segment.Id, new ByteOffset(offset), count, cancellationToken).ConfigureAwait(false);
            await target.AppendAsync(created, bytes, cancellationToken).ConfigureAwait(false);
        }

        await target.FlushAsync(created, cancellationToken).ConfigureAwait(false);

        if (seal)
        {
            await target.SealAsync(created, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask CopyBlobAsync(IDerivedStore target, BlobName name, CancellationToken cancellationToken)
    {
        using IReadableBlob source = await _storage.Derived.OpenAsync(name, cancellationToken).ConfigureAwait(false);
        await using IBlobWriter writer = await target.CreateAsync(name, cancellationToken).ConfigureAwait(false);
        byte[] buffer = new byte[ShipChunk];

        for (long offset = 0; offset < source.Length.Value; offset += ShipChunk)
        {
            int read = source.Read(new ByteOffset(offset), buffer);
            await writer.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        await writer.PublishAsync(cancellationToken).ConfigureAwait(false);
    }
}
