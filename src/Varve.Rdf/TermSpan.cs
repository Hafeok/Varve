using System;

namespace Varve.Rdf;

/// <summary>
/// Where the bytes of one part of a term live: a range of the input being
/// parsed, or a range of a <see cref="TermArena"/>'s own scratch.
/// </summary>
/// <remarks>
/// Two buffers rather than one because of escapes. <c>&lt;http://a/b&gt;</c>
/// can be read in place; <c>"a\nb"</c> cannot, because the bytes the term is
/// made of do not occur in the input. A parser copies only what it must, and
/// the common case still costs nothing.
/// </remarks>
public readonly struct TermSpan : IEquatable<TermSpan>
{
    private TermSpan(int start, int length, bool scratch)
    {
        Start = start;
        Length = length;
        IsScratch = scratch;
        IsPresent = true;
    }

    /// <summary>A range of the input text.</summary>
    public static TermSpan FromText(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return new TermSpan(start, length, scratch: false);
    }

    /// <summary>A range of the arena's scratch buffer.</summary>
    public static TermSpan FromScratch(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return new TermSpan(start, length, scratch: true);
    }

    /// <summary>
    /// The absent span, which is not the same as an empty one: a literal with
    /// no datatype is absent, and <c>""^^&lt;d&gt;</c> has an empty lexical
    /// form that is present.
    /// </summary>
    public static TermSpan None => default;

    /// <summary>Whether this span refers to bytes at all.</summary>
    public bool IsPresent { get; }

    /// <summary>The offset into whichever buffer this span names.</summary>
    public int Start { get; }

    /// <summary>The length in bytes.</summary>
    public int Length { get; }

    /// <summary>Whether the bytes are in the arena's scratch rather than the input.</summary>
    public bool IsScratch { get; }

    /// <inheritdoc />
    public bool Equals(TermSpan other) =>
        Start == other.Start
        && Length == other.Length
        && IsScratch == other.IsScratch
        && IsPresent == other.IsPresent;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TermSpan other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Start, Length, IsScratch, IsPresent);

    /// <summary>Compares the ranges.</summary>
    public static bool operator ==(TermSpan left, TermSpan right) => left.Equals(right);

    /// <summary>Compares the ranges.</summary>
    public static bool operator !=(TermSpan left, TermSpan right) => !left.Equals(right);
}
