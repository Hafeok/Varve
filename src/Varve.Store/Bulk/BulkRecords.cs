// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Runtime.InteropServices;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Store;

/// <summary>
/// A term as a bulk load refers to it before its id is final (ADR 0081): an
/// existing id, or a new term's 120-bit content hash, in a segment that sorts
/// where the term's final id will — existing canonical ids, then new canonical
/// terms by nesting depth, then existing blank nodes, new blank nodes, and
/// inline values. New terms take their final ids in hash order within their
/// segment, so a list sorted by these references is already sorted by the
/// final ids, and nothing is sorted twice.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct BulkRef : IComparable<BulkRef>, IOrdered<BulkRef>, IEquatable<BulkRef>
{
    internal const int Size = 16;

    internal const byte Existing = 0;
    internal const byte NewCanonical = 1;
    internal const int MaxDepth = 14;
    internal const byte ExistingBlank = 0x40;
    internal const byte NewBlank = 0x41;
    internal const byte Inline = 0x7F;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal BulkRef(ulong hi, ulong lo)
    {
        Hi = hi;
        Lo = lo;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal ulong Hi { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal ulong Lo { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal byte Segment => (byte)(Hi >> 56);

    /// <summary>Whether the term is new to the dataset.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool IsNew => Segment is >= NewCanonical and <= NewCanonical + MaxDepth or NewBlank;

    /// <summary>A reference to an id the dataset already has, inline ones and the default graph included.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static BulkRef Of(ulong id)
    {
        byte segment = TermIds.ClassOf(id) switch
        {
            IdClass.Blank => ExistingBlank,
            IdClass.Inline => Inline,
            _ => Existing,
        };

        return new BulkRef((ulong)segment << 56, id);
    }

    /// <summary>
    /// A new term's reference: its segment and the first 120 bits of the
    /// SHA-256 of its key — a hash whose collisions are not a property of the
    /// input's shape. Computed once per term a load meets that its cache does
    /// not hold.
    /// </summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static BulkRef New(byte segment, ReadOnlySpan<byte> key)
    {
        Span<byte> hash = stackalloc byte[32];
        Sha256(key, hash);
        return new(((ulong)segment << 56) | (System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(hash) >> 8), System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(hash[8..]));
    }

    [DesignDecision(typeof(TheBulkLoader.NewTermsByContentHash), Scope = ExceptionScope.HotPath)]
    private static void Sha256(ReadOnlySpan<byte> key, Span<byte> hash) => System.Security.Cryptography.SHA256.HashData(key, hash);

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public int CompareTo(BulkRef other)
    {
        int c = Hi.CompareTo(other.Hi);
        return c != 0 ? c : Lo.CompareTo(other.Lo);
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool Equals(BulkRef other) => Hi == other.Hi && Lo == other.Lo;

    public override bool Equals(object? obj) => obj is BulkRef other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Hi, Lo);
}

/// <summary>
/// One operation of a bulk load: four term references, and its sequence
/// number with the operation in the low bit (1 asserts). Ordered by the
/// quad, then the sequence, so the last operation on a quad comes last.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct BulkQuad : IComparable<BulkQuad>, IOrdered<BulkQuad>
{
    internal const int Size = (4 * BulkRef.Size) + 8;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal BulkQuad(BulkRef subject, BulkRef predicate, BulkRef @object, BulkRef graph, ulong sequence)
    {
        Subject = subject;
        Predicate = predicate;
        Object = @object;
        Graph = graph;
        Sequence = sequence;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal BulkRef Subject { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal BulkRef Predicate { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal BulkRef Object { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal BulkRef Graph { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal ulong Sequence { get; }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool Asserts => (Sequence & 1) != 0;

    /// <summary>Whether two operations are on the same quad.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool SameQuad(in BulkQuad other) =>
        Subject.Equals(other.Subject) && Predicate.Equals(other.Predicate) && Object.Equals(other.Object) && Graph.Equals(other.Graph);

    /// <summary>Whether every term is one the dataset already has: only then can the quad be present.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal bool AllExisting => !Subject.IsNew && !Predicate.IsNew && !Object.IsNew && !Graph.IsNew;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public int CompareTo(BulkQuad other)
    {
        int c = Subject.CompareTo(other.Subject);

        if (c == 0)
        {
            c = Predicate.CompareTo(other.Predicate);
        }

        if (c == 0)
        {
            c = Object.CompareTo(other.Object);
        }

        if (c == 0)
        {
            c = Graph.CompareTo(other.Graph);
        }

        return c != 0 ? c : Sequence.CompareTo(other.Sequence);
    }
}
