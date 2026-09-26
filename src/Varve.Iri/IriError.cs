// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Iri;

/// <summary>Why an IRI reference failed to validate.</summary>
public enum IriErrorKind : byte
{
    /// <summary>No error.</summary>
    None = 0,

    /// <summary>The input ended in the middle of a construct.</summary>
    UnexpectedEnd,

    /// <summary>A byte that is not permitted where it appears.</summary>
    InvalidCharacter,

    /// <summary>A <c>%</c> not followed by two hexadecimal digits.</summary>
    InvalidPercentEncoding,

    /// <summary>The bytes are not well-formed UTF-8.</summary>
    InvalidUtf8,

    /// <summary>An absolute IRI was required and the input has no scheme.</summary>
    MissingScheme,

    /// <summary>A scheme that does not match <c>ALPHA *( ALPHA / DIGIT / "+" / "-" / "." )</c>.</summary>
    InvalidScheme,

    /// <summary>A port containing something other than digits.</summary>
    InvalidPort,

    /// <summary>An IP-literal opened with <c>[</c> and never closed.</summary>
    UnterminatedIpLiteral,
}

/// <summary>
/// A validation failure, with the byte at which the input could not continue.
/// </summary>
/// <remarks>
/// The offset is the first byte that could not be accepted, not the start of
/// the construct it belongs to: a caller reporting a position wants to point at
/// the byte that broke.
/// </remarks>
[HotPath(typeof(BriefHardConstraints.AllocationPerQuadIsADefect))]
public readonly struct IriError : System.IEquatable<IriError>
{
    internal IriError(IriErrorKind kind, int offset)
    {
        Kind = kind;
        Offset = offset;
    }

    /// <summary>What went wrong.</summary>
    public IriErrorKind Kind { get; }

    /// <summary>The byte offset at which it went wrong.</summary>
    public int Offset { get; }

    /// <summary>Whether this value represents a failure.</summary>
    public bool IsError => Kind != IriErrorKind.None;

    /// <inheritdoc />
    public bool Equals(IriError other) => Kind == other.Kind && Offset == other.Offset;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is IriError other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => System.HashCode.Combine(Kind, Offset);

    /// <summary>Compares two errors for equality.</summary>
    public static bool operator ==(IriError left, IriError right) => left.Equals(right);

    /// <summary>Compares two errors for inequality.</summary>
    public static bool operator !=(IriError left, IriError right) => !left.Equals(right);
}
