// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven.Ledger.Varve;
using DecisionDriven;
using Varve.Store.Log;

namespace Varve.Store;

/// <summary>
/// A dataset's storage: the log, which is the source of truth, and derived
/// data, which is not. Split exactly where specification §2 splits them.
/// </summary>
/// <remarks>
/// ADR 0018 fixes the shape and ADR 0040 the members. The two halves are
/// separate types so that dropping everything derived is an operation the
/// type makes safe, and confusing the two is not expressible.
/// </remarks>
[Contract(typeof(SynchronousReadsOverAsynchronousStorage.SegmentStoreAndDerivedStore), Role = "a storage backend: the log's segments and the derived blobs")]
public interface IStorage
{
    /// <summary>The log: append-only segments.</summary>
    ISegmentStore Log { get; }

    /// <summary>Derived data: checkpoints, and anything else that can be rebuilt.</summary>
    IDerivedStore Derived { get; }
}

/// <summary>
/// The log's storage: numbered segments that are only ever appended to, then
/// sealed.
/// </summary>
/// <remarks>
/// <para>
/// **There is no truncate, no positional write, and no delete.** Specification
/// §2's "the log is never rewritten" is a property of this type rather than a
/// rule a caller has to remember (ADR 0018, first impossibility).
/// </para>
/// <para>
/// Segments are numbered by the backend in ascending order, and only the
/// newest may be unsealed; appending to any other fails. Bytes returned by
/// <see cref="ReadRangeAsync"/> are immutable and may be held by the caller: a
/// sealed segment never changes, and an append never changes a byte already
/// written. A backend that cannot hand out its own storage on those terms
/// copies (ADR 0040).
/// </para>
/// </remarks>
[Contract(typeof(SynchronousReadsOverAsynchronousStorage.StorageContractMembers), Role = "the log's append-only segments")]
public interface ISegmentStore
{
    /// <summary>What a completed <see cref="FlushAsync"/> guarantees.</summary>
    Durability Durability { get; }

    /// <summary>Every segment, in ascending order.</summary>
    ValueTask<IReadOnlyList<SegmentInfo>> ListSegmentsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Opens a new, empty segment after every existing one and returns its
    /// number. Fails when the newest existing segment is not sealed.
    /// </summary>
    ValueTask<SegmentId> CreateSegmentAsync(CancellationToken cancellationToken);

    /// <summary>Appends bytes to the end of the unsealed newest segment.</summary>
    ValueTask AppendAsync(SegmentId segment, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);

    /// <summary>Makes everything appended so far as durable as <see cref="Durability"/> says.</summary>
    ValueTask FlushAsync(SegmentId segment, CancellationToken cancellationToken);

    /// <summary>Makes a segment immutable. Sealing is permanent.</summary>
    ValueTask SealAsync(SegmentId segment, CancellationToken cancellationToken);

    /// <summary>
    /// Reads bytes from a segment. Returns fewer than asked for only at the
    /// segment's end. The returned bytes never change.
    /// </summary>
    ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(SegmentId segment, ByteOffset offset, ByteCount length, CancellationToken cancellationToken);

    /// <summary>The log's manifest, or empty when it has none yet.</summary>
    /// <remarks>
    /// The bytes are the store's (ADR 0072); the backend keeps them — the file
    /// backend as <c>log/MANIFEST</c> — and never interprets them.
    /// </remarks>
    ValueTask<ReadOnlyMemory<byte>> ReadManifestAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the log's manifest, durably, before any segment exists. A
    /// manifest is written once; a second write fails.
    /// </summary>
    ValueTask WriteManifestAsync(ReadOnlyMemory<byte> manifest, CancellationToken cancellationToken);
}

/// <summary>
/// A log its backend can open from a point rather than from its start (ADR
/// 0139): the catalog records the backend's slot for where the log continues
/// after the catalog's position, and an open hands it back before it reads
/// the log, so a backend that keeps its log in parts reads only those from
/// there on. Optional: a backend that reads its whole log cheaply does not
/// implement it, and an open then reads the whole log.
/// </summary>
[Contract(typeof(ExtentsAndTheProjectionCatalog.CatalogRecordsTheLogCursor), Role = "a log opened from the catalog's cursor rather than from its start")]
public interface ISegmentCursor
{
    /// <summary>
    /// The backend's slot holding the byte just before <paramref name="offset"/>
    /// of <paramref name="segment"/>, or one before it; 0, the segment's
    /// first, is always correct.
    /// </summary>
    SegmentSlot SlotOf(SegmentId segment, ByteOffset offset);

    /// <summary>
    /// Says the log will be read from <paramref name="slot"/> of
    /// <paramref name="segment"/> on. A read of anything before it is still
    /// answered, by reading it then.
    /// </summary>
    ValueTask ReadFromAsync(SegmentId segment, SegmentSlot slot, CancellationToken cancellationToken);
}

/// <summary>
/// Derived data: named blobs that may be rebuilt from the log and may be
/// dropped without loss.
/// </summary>
/// <remarks>
/// <para>
/// A blob is written as a stream and becomes visible only when published,
/// atomically replacing any blob of the same name; a writer disposed without
/// publishing leaves nothing (ADR 0071). A blob is never modified in place.
/// </para>
/// <para>
/// A blob is read **synchronously**, through <see cref="IReadableBlob"/>, so
/// that runs and checkpoints are scanned by the store's synchronous cursor
/// with the same code on every backend (ADR 0071).
/// </para>
/// </remarks>
[Contract(typeof(SynchronousReadsOverAsynchronousStorage.StorageContractMembers), Role = "derived blobs, rebuildable from the log")]
public interface IDerivedStore
{
    /// <summary>Starts writing a blob. Nothing is visible under the name until the writer publishes.</summary>
    ValueTask<IBlobWriter> CreateAsync(BlobName name, CancellationToken cancellationToken);

    /// <summary>Opens a blob for synchronous reads. Fails when there is no blob of that name.</summary>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">There is no blob of that name.</exception>
    ValueTask<IReadableBlob> OpenAsync(BlobName name, CancellationToken cancellationToken);

    /// <summary>Deletes a blob. Returns false when there was none.</summary>
    ValueTask<bool> DeleteAsync(BlobName name, CancellationToken cancellationToken);

    /// <summary>The names of every published blob, in ordinal order.</summary>
    ValueTask<IReadOnlyList<BlobName>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>A blob being written. Visible under its name only once published.</summary>
/// <remarks>
/// Disposing a writer that has not published discards what it wrote, so a
/// crash or an exception mid-write leaves no blob, and never a torn one
/// (ADR 0071, ADR 0073).
/// </remarks>
[Contract(typeof(SynchronousReadsOverAsynchronousStorage.BlobsArePublishedAtomically), Role = "a derived blob being written, published atomically")]
public interface IBlobWriter : IAsyncDisposable
{
    /// <summary>Appends bytes to the blob.</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);

    /// <summary>
    /// Makes the blob as durable as the backend's <see cref="ISegmentStore.Durability"/>
    /// says, then visible under its name, atomically replacing any blob of
    /// that name. A writer publishes once.
    /// </summary>
    ValueTask PublishAsync(CancellationToken cancellationToken);
}

/// <summary>A published blob, open for synchronous reads.</summary>
/// <remarks>
/// The blob's bytes never change while it is open. Deleting a blob that is
/// open is the backend's to allow or to defer; reads of an open blob keep
/// returning its bytes either way.
/// </remarks>
[Contract(typeof(SynchronousReadsOverAsynchronousStorage.DerivedReadsAreSynchronous), Role = "a derived blob, read synchronously")]
public interface IReadableBlob : IDisposable
{
    /// <summary>The blob's length.</summary>
    ByteCount Length { get; }

    /// <summary>
    /// Copies bytes from <paramref name="offset"/> into
    /// <paramref name="destination"/> and returns how many: fewer than the
    /// destination holds only at the blob's end.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    int Read(ByteOffset offset, Span<byte> destination);
}

/// <summary>
/// Storage another process writes, which a follower reads as it grows (ADR
/// 0164): a refresh learns what the writer has appended since the last one,
/// checks it as the writer's own open checks what it adopts, and makes it
/// readable through <see cref="IStorage.Log"/>. It never writes.
/// </summary>
[Contract(typeof(TheTier1Follower.TailingStorageContract), Role = "storage a follower tails while another process writes it")]
public interface ITailingStorage : IStorage
{
    /// <summary>
    /// Learns and adopts what the writer appended since the last refresh.
    /// True when there was anything.
    /// </summary>
    /// <exception cref="LogVerificationException">What the writer appended does not verify.</exception>
    ValueTask<bool> RefreshAsync(CancellationToken cancellationToken);
}
