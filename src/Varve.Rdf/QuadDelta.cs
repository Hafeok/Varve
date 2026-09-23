// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Varve.Rdf;

/// <summary>
/// A change to a set of quads: the quads it asserts and the quads it retracts,
/// never the same quad in both.
/// </summary>
/// <remarks>
/// <para>
/// The specification's <c>δ = (A, R)</c> with <c>A ∩ R = ∅</c> (§1), and the
/// thing an overlay is made of (ADR 0017). It is a set operation over handles
/// issued by one source, and knows nothing about the source, a log or a
/// position — which is why it lives at layer 1 beside <see cref="QuadOverlay"/>.
/// </para>
/// <para>
/// Both halves are kept sorted by handle bits, subject first. That is an order
/// for set operations, not a term order: it says nothing about how two terms
/// compare, and a consumer must not read one into it.
/// </para>
/// <para>
/// <c>default(QuadDelta)</c> is <see cref="Empty"/>, the identity of
/// <see cref="Then"/>.
/// </para>
/// </remarks>
public readonly struct QuadDelta : IEquatable<QuadDelta>
{
    private readonly Quad[]? _asserted;
    private readonly Quad[]? _retracted;

    private QuadDelta(Quad[] asserted, Quad[] retracted)
    {
        _asserted = asserted;
        _retracted = retracted;
    }

    /// <summary>The delta that changes nothing, and the identity of <see cref="Then"/>.</summary>
    public static QuadDelta Empty => default;

    /// <summary>The quads asserted, sorted by handle bits.</summary>
    public ReadOnlySpan<Quad> Asserted => _asserted;

    /// <summary>The quads retracted, sorted by handle bits.</summary>
    public ReadOnlySpan<Quad> Retracted => _retracted;

    /// <summary>How many quads the delta mentions, asserted and retracted together.</summary>
    public int Count => (_asserted?.Length ?? 0) + (_retracted?.Length ?? 0);

    /// <summary>True when the delta changes nothing.</summary>
    public bool IsEmpty => Count == 0;

    /// <summary>
    /// A delta from two sets of quads. Both are copied, sorted and
    /// de-duplicated; a quad in both is refused, because a delta that asserts
    /// and retracts the same quad does not say what it means.
    /// </summary>
    /// <exception cref="ArgumentException">A quad is both asserted and retracted.</exception>
    public static QuadDelta Create(ReadOnlySpan<Quad> asserted, ReadOnlySpan<Quad> retracted)
    {
        Quad[] a = SortedSet(asserted);
        Quad[] r = SortedSet(retracted);

        if (Intersects(a, r))
        {
            throw new ArgumentException(
                "A delta cannot assert and retract the same quad (specification §1: A ∩ R = ∅).",
                nameof(retracted));
        }

        return a.Length == 0 && r.Length == 0 ? default : new QuadDelta(a, r);
    }

    /// <summary>Whether the delta asserts a quad.</summary>
    [HotPath]
    public bool Asserts(in Quad quad) => IndexOf(Asserted, in quad) >= 0;

    /// <summary>Whether the delta retracts a quad.</summary>
    [HotPath]
    public bool Retracts(in Quad quad) => IndexOf(Retracted, in quad) >= 0;

    /// <summary>
    /// This delta followed by <paramref name="next"/>: the specification's
    /// <c>(A₁, R₁) ; (A₂, R₂) = ((A₁ \ R₂) ∪ (A₂ \ R₁), (R₁ \ A₂) ∪ (R₂ \ A₁))</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="Empty"/> is its identity. It is associative over a chain of
    /// exact deltas — consecutive changes to one evolving set, which is every
    /// use a log makes of it — and **not** over arbitrary deltas: two deltas
    /// that both retract one quad, then one that asserts it, compose to
    /// different answers depending on grouping. The specification's §6 claims a
    /// monoid; milestone 4 reported that as a proposed change. The result's
    /// halves are disjoint whenever both inputs' are, so no check is needed.
    /// </remarks>
    public QuadDelta Then(QuadDelta next)
    {
        if (IsEmpty)
        {
            return next;
        }

        if (next.IsEmpty)
        {
            return this;
        }

        Quad[] asserted = Union(Except(Asserted, next.Retracted), Except(next.Asserted, Retracted));
        Quad[] retracted = Union(Except(Retracted, next.Asserted), Except(next.Retracted, Asserted));

        return asserted.Length == 0 && retracted.Length == 0 ? default : new QuadDelta(asserted, retracted);
    }

    /// <summary>The delta that undoes this one: its halves swapped.</summary>
    public QuadDelta Inverse() => IsEmpty ? default : new QuadDelta(_retracted ?? [], _asserted ?? []);

    /// <summary>Set equality of both halves.</summary>
    public bool Equals(QuadDelta other) =>
        Asserted.SequenceEqual(other.Asserted) && Retracted.SequenceEqual(other.Retracted);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is QuadDelta other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = default;

        foreach (Quad quad in Asserted)
        {
            hash.Add(quad);
        }

        hash.Add(-1);

        foreach (Quad quad in Retracted)
        {
            hash.Add(quad);
        }

        return hash.ToHashCode();
    }

    /// <summary>Set equality of both halves.</summary>
    public static bool operator ==(QuadDelta left, QuadDelta right) => left.Equals(right);

    /// <summary>Set equality of both halves.</summary>
    public static bool operator !=(QuadDelta left, QuadDelta right) => !left.Equals(right);

    [HotPath]
    internal static int Compare(in Quad left, in Quad right)
    {
        int c = left.Subject.Value.CompareTo(right.Subject.Value);

        if (c != 0)
        {
            return c;
        }

        c = left.Predicate.Value.CompareTo(right.Predicate.Value);

        if (c != 0)
        {
            return c;
        }

        c = left.Object.Value.CompareTo(right.Object.Value);
        return c != 0 ? c : left.Graph.Value.CompareTo(right.Graph.Value);
    }

    [HotPath]
    internal static int IndexOf(ReadOnlySpan<Quad> sorted, in Quad quad)
    {
        int low = 0;
        int high = sorted.Length - 1;

        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int c = Compare(in sorted[middle], in quad);

            if (c == 0)
            {
                return middle;
            }

            if (c < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return ~low;
    }

    private static Quad[] SortedSet(ReadOnlySpan<Quad> quads)
    {
        if (quads.IsEmpty)
        {
            return [];
        }

        Quad[] copy = quads.ToArray();
        copy.AsSpan().Sort(new Order());

        int kept = 1;

        for (int i = 1; i < copy.Length; i++)
        {
            if (Compare(in copy[i], in copy[kept - 1]) != 0)
            {
                copy[kept++] = copy[i];
            }
        }

        return kept == copy.Length ? copy : copy.AsSpan(0, kept).ToArray();
    }

    private static bool Intersects(ReadOnlySpan<Quad> left, ReadOnlySpan<Quad> right)
    {
        int i = 0;
        int j = 0;

        while (i < left.Length && j < right.Length)
        {
            int c = Compare(in left[i], in right[j]);

            if (c == 0)
            {
                return true;
            }

            if (c < 0)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return false;
    }

    private static List<Quad> Except(ReadOnlySpan<Quad> left, ReadOnlySpan<Quad> right)
    {
        List<Quad> kept = new(left.Length);

        foreach (Quad quad in left)
        {
            if (IndexOf(right, in quad) < 0)
            {
                kept.Add(quad);
            }
        }

        return kept;
    }

    private static Quad[] Union(List<Quad> left, List<Quad> right)
    {
        Quad[] merged = new Quad[left.Count + right.Count];
        int i = 0;
        int j = 0;
        int k = 0;

        while (i < left.Count && j < right.Count)
        {
            int c = Compare(left[i], right[j]);

            if (c < 0)
            {
                merged[k++] = left[i++];
            }
            else if (c > 0)
            {
                merged[k++] = right[j++];
            }
            else
            {
                merged[k++] = left[i++];
                j++;
            }
        }

        while (i < left.Count)
        {
            merged[k++] = left[i++];
        }

        while (j < right.Count)
        {
            merged[k++] = right[j++];
        }

        return k == merged.Length ? merged : merged.AsSpan(0, k).ToArray();
    }

    private readonly struct Order : IComparer<Quad>
    {
        public int Compare(Quad x, Quad y) => QuadDelta.Compare(in x, in y);
    }
}
