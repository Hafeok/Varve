// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    ValueTask<int> CreateSegmentAsync(CancellationToken cancellationToken);

    /// <summary>Appends bytes to the end of the unsealed newest segment.</summary>
    ValueTask AppendAsync(int segment, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);

    /// <summary>Makes everything appended so far as durable as <see cref="Durability"/> says.</summary>
    ValueTask FlushAsync(int segment, CancellationToken cancellationToken);

    /// <summary>Makes a segment immutable. Sealing is permanent.</summary>
    ValueTask SealAsync(int segment, CancellationToken cancellationToken);

    /// <summary>
    /// Reads bytes from a segment. Returns fewer than asked for only at the
    /// segment's end. The returned bytes never change.
    /// </summary>
    ValueTask<ReadOnlyMemory<byte>> ReadRangeAsync(int segment, long offset, int length, CancellationToken cancellationToken);
}

/// <summary>
/// Derived data: named blobs that may be rebuilt from the log and may be
/// dropped without loss.
/// </summary>
/// <remarks>
/// Bytes returned by <see cref="GetRangeAsync"/> are immutable and may be held,
/// on the same terms as <see cref="ISegmentStore.ReadRangeAsync"/>: a blob is
/// replaced, never modified in place. That is what lets a checkpoint be
/// scanned where it lies (ADR 0041).
/// </remarks>
public interface IDerivedStore
{
    /// <summary>Stores a blob under a name, replacing any blob of that name.</summary>
    ValueTask PutAsync(string name, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);

    /// <summary>
    /// Reads bytes from a blob. Returns fewer than asked for only at the blob's
    /// end. Fails when there is no blob of that name.
    /// </summary>
    ValueTask<ReadOnlyMemory<byte>> GetRangeAsync(string name, long offset, int length, CancellationToken cancellationToken);

    /// <summary>Deletes a blob. Returns false when there was none.</summary>
    ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken);

    /// <summary>The names of every blob, in ordinal order.</summary>
    ValueTask<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>A segment of the log: its number, its length, and whether it is sealed.</summary>
public readonly struct SegmentInfo : IEquatable<SegmentInfo>
{
    /// <summary>Describes a segment.</summary>
    public SegmentInfo(int id, long length, bool isSealed)
    {
        Id = id;
        Length = length;
        IsSealed = isSealed;
    }

    /// <summary>The segment's number.</summary>
    public int Id { get; }

    /// <summary>How many bytes it holds.</summary>
    public long Length { get; }

    /// <summary>Whether it is sealed and will never change.</summary>
    public bool IsSealed { get; }

    /// <inheritdoc />
    public bool Equals(SegmentInfo other) => Id == other.Id && Length == other.Length && IsSealed == other.IsSealed;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SegmentInfo other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Id, Length, IsSealed);

    /// <summary>Compares all three fields.</summary>
    public static bool operator ==(SegmentInfo left, SegmentInfo right) => left.Equals(right);

    /// <summary>Compares all three fields.</summary>
    public static bool operator !=(SegmentInfo left, SegmentInfo right) => !left.Equals(right);
}
