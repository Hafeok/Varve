using System;

namespace Varve.Rdf;

/// <summary>
/// An opaque 64-bit reference to a term, issued by one quad source.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0022. A handle means nothing on its own: a store's handle is its
/// <c>TermId</c>, an in-memory dataset's handle is an index into its own
/// interning table, and neither is the other's business. A handle from one
/// source is meaningless to another.
/// </para>
/// <para>
/// The structural equality on this type compares the bits, which is correct
/// only within a source that interns every term. **Consumers must compare
/// terms through <see cref="IQuadSource.TermComparer"/> instead**, because a
/// readable private term compares by decrypted value and two handles with
/// different bits can name equal terms.
/// </para>
/// </remarks>
public readonly struct TermHandle : IEquatable<TermHandle>
{
    /// <summary>Wraps a source-specific value.</summary>
    public TermHandle(ulong value) => Value = value;

    /// <summary>The source-specific value.</summary>
    public ulong Value { get; }

    /// <summary>
    /// True for the absent handle. In a <see cref="Quad"/>'s graph position it
    /// means the default graph; in a match pattern it means "any term".
    /// </summary>
    public bool IsNone => Value == 0;

    /// <summary>The absent handle. Zero is never issued.</summary>
    public static TermHandle None => default;

    /// <inheritdoc />
    public bool Equals(TermHandle other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TermHandle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>Compares the bits. See the type's remarks before using it.</summary>
    public static bool operator ==(TermHandle left, TermHandle right) => left.Equals(right);

    /// <summary>Compares the bits. See the type's remarks before using it.</summary>
    public static bool operator !=(TermHandle left, TermHandle right) => !left.Equals(right);
}
