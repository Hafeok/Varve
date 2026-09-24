// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Varve.Sparql.Algebra;

/// <summary>
/// An immutable sequence of children with element-wise equality, so that a
/// record holding one gets structural equality for free.
/// </summary>
/// <remarks>
/// <c>ImmutableArray&lt;T&gt;</c> compares by reference to its backing array,
/// which would make two identical trees unequal. This is the small helper
/// <c>docs/spec/sparql-algebra.md</c> §2.2 mentions: a wrapper over an array
/// that nobody else holds, comparing elements with their own equality. It is a
/// struct so that an empty list costs nothing, and it enumerates as a span.
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
public readonly struct AlgebraList<T> : IEquatable<AlgebraList<T>>
{
    private readonly T[]? _items;

    /// <summary>The number of elements.</summary>
    public int Count => _items?.Length ?? 0;

    /// <summary>True when there are no elements.</summary>
    public bool IsEmpty => Count == 0;

    /// <summary>The elements, as a span.</summary>
    public ReadOnlySpan<T> Span => _items;

    /// <summary>The element at an index.</summary>
    public T this[int index] => Span[index];

    /// <summary>Wraps an array nobody else holds; internal so that the promise can be kept.</summary>
    internal AlgebraList(T[]? items) => _items = items is { Length: 0 } ? null : items;

    /// <summary>The elements, as a new array.</summary>
    public T[] ToArray() => Span.ToArray();

    /// <summary>Enumerates the elements without allocating.</summary>
    public ReadOnlySpan<T>.Enumerator GetEnumerator() => Span.GetEnumerator();

    /// <inheritdoc />
    public bool Equals(AlgebraList<T> other)
    {
        ReadOnlySpan<T> left = Span;
        ReadOnlySpan<T> right = other.Span;

        if (left.Length != right.Length)
        {
            return false;
        }

        EqualityComparer<T> comparer = EqualityComparer<T>.Default;

        for (int i = 0; i < left.Length; i++)
        {
            if (!comparer.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is AlgebraList<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(Count);

        foreach (T item in Span)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    /// <summary>Element-wise equality.</summary>
    public static bool operator ==(AlgebraList<T> left, AlgebraList<T> right) => left.Equals(right);

    /// <summary>Element-wise inequality.</summary>
    public static bool operator !=(AlgebraList<T> left, AlgebraList<T> right) => !left.Equals(right);
}

/// <summary>Builds <see cref="AlgebraList{T}"/> values.</summary>
public static class AlgebraList
{
    /// <summary>The empty list.</summary>
    public static AlgebraList<T> Empty<T>() => default;

    /// <summary>A list holding a copy of the elements.</summary>
    public static AlgebraList<T> From<T>(ReadOnlySpan<T> items) => new(items.ToArray());

    /// <summary>A list holding a copy of the elements.</summary>
    public static AlgebraList<T> From<T>(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new AlgebraList<T>([.. items]);
    }

    /// <summary>A list of one element.</summary>
    public static AlgebraList<T> Of<T>(T item) => new([item]);

    /// <summary>A list of two elements.</summary>
    public static AlgebraList<T> Of<T>(T first, T second) => new([first, second]);

    /// <summary>A list of three elements.</summary>
    public static AlgebraList<T> Of<T>(T first, T second, T third) => new([first, second, third]);

    /// <summary>Takes ownership of an array nobody else holds. Internal so that the promise can be kept.</summary>
    internal static AlgebraList<T> Own<T>(T[] items) => new(items);
}
