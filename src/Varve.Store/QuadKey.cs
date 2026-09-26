// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Runtime.InteropServices;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Rdf;

namespace Varve.Store;

/// <summary>
/// A quad's four ids in one of the six index orders, compared
/// lexicographically. 32 bytes: ADR 0012's 64-bit ids, four of them.
/// </summary>
/// <remarks>
/// Laid out sequentially so that a checkpoint's bytes can be read as keys in
/// place (ADR 0041).
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct QuadKey : IComparable<QuadKey>, IEquatable<QuadKey>
{
    internal QuadKey(ulong k0, ulong k1, ulong k2, ulong k3)
    {
        K0 = k0;
        K1 = k1;
        K2 = k2;
        K3 = k3;
    }

    internal ulong K0 { get; }

    internal ulong K1 { get; }

    internal ulong K2 { get; }

    internal ulong K3 { get; }

    internal const int Size = 32;

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public int CompareTo(QuadKey other)
    {
        if (K0 != other.K0)
        {
            return K0 < other.K0 ? -1 : 1;
        }

        if (K1 != other.K1)
        {
            return K1 < other.K1 ? -1 : 1;
        }

        if (K2 != other.K2)
        {
            return K2 < other.K2 ? -1 : 1;
        }

        return K3 == other.K3 ? 0 : K3 < other.K3 ? -1 : 1;
    }

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    public bool Equals(QuadKey other) => K0 == other.K0 && K1 == other.K1 && K2 == other.K2 && K3 == other.K3;

    public override bool Equals(object? obj) => obj is QuadKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(K0, K1, K2, K3);
}

/// <summary>The six orders of ADR 0041. Every combination of bound positions is a prefix of one.</summary>
internal enum IndexOrder
{
    Spog = 0,
    Posg = 1,
    Ospg = 2,
    Gspo = 3,
    Gpos = 4,
    Gosp = 5,
}

internal static class Orders
{
    internal const int Count = 6;

    /// <summary>A quad's key in an order.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static QuadKey Key(IndexOrder order, ulong s, ulong p, ulong o, ulong g) => order switch
    {
        IndexOrder.Spog => new QuadKey(s, p, o, g),
        IndexOrder.Posg => new QuadKey(p, o, s, g),
        IndexOrder.Ospg => new QuadKey(o, s, p, g),
        IndexOrder.Gspo => new QuadKey(g, s, p, o),
        IndexOrder.Gpos => new QuadKey(g, p, o, s),
        _ => new QuadKey(g, o, s, p),
    };

    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static QuadKey Key(IndexOrder order, in Quad quad) =>
        Key(order, quad.Subject.Value, quad.Predicate.Value, quad.Object.Value, quad.Graph.Value);

    /// <summary>The quad a key in an order stands for.</summary>
    [HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
    internal static Quad Quad(IndexOrder order, in QuadKey key)
    {
        (ulong s, ulong p, ulong o, ulong g) = order switch
        {
            IndexOrder.Spog => (key.K0, key.K1, key.K2, key.K3),
            IndexOrder.Posg => (key.K2, key.K0, key.K1, key.K3),
            IndexOrder.Ospg => (key.K1, key.K2, key.K0, key.K3),
            IndexOrder.Gspo => (key.K1, key.K2, key.K3, key.K0),
            IndexOrder.Gpos => (key.K3, key.K1, key.K2, key.K0),
            _ => (key.K2, key.K3, key.K1, key.K0),
        };

        return new Quad(new TermHandle(s), new TermHandle(p), new TermHandle(o), new TermHandle(g));
    }

    /// <summary>
    /// Which of the four positions (0 s, 1 p, 2 o, 3 g) the key's components hold,
    /// in key order.
    /// </summary>
    internal static ReadOnlySpan<byte> Positions(IndexOrder order) => order switch
    {
        IndexOrder.Spog => [0, 1, 2, 3],
        IndexOrder.Posg => [1, 2, 0, 3],
        IndexOrder.Ospg => [2, 0, 1, 3],
        IndexOrder.Gspo => [3, 0, 1, 2],
        IndexOrder.Gpos => [3, 1, 2, 0],
        _ => [3, 2, 0, 1],
    };
}
