using System;
using System.Globalization;
using Varve.Iri;

namespace Varve.Turtle;

/// <summary>One rejected line.</summary>
public readonly struct ParseError : IEquatable<ParseError>
{
    /// <summary>Builds an error.</summary>
    public ParseError(ParseErrorKind kind, ParsePosition position, IriErrorKind iri = IriErrorKind.None)
    {
        Kind = kind;
        Position = position;
        Iri = iri;
    }

    /// <summary>Why the line was rejected.</summary>
    public ParseErrorKind Kind { get; }

    /// <summary>Where the parser was when it gave up on the line.</summary>
    public ParsePosition Position { get; }

    /// <summary>
    /// What the IRI validator said, when <see cref="Kind"/> is
    /// <see cref="ParseErrorKind.InvalidIri"/>. Passed through rather than
    /// flattened, because "bad IRI" on its own is not diagnosable.
    /// </summary>
    public IriErrorKind Iri { get; }

    /// <summary>Whether this value stands for an error at all.</summary>
    public bool IsError => Kind != ParseErrorKind.None;

    /// <inheritdoc />
    public bool Equals(ParseError other) =>
        Kind == other.Kind && Position.Equals(other.Position) && Iri == other.Iri;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ParseError other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Position, Iri);

    /// <summary>Renders the position, the kind, and the IRI detail when there is one.</summary>
    public override string ToString() =>
        Iri == IriErrorKind.None
            ? string.Create(CultureInfo.InvariantCulture, $"{Position}: {Kind}")
            : string.Create(CultureInfo.InvariantCulture, $"{Position}: {Kind} ({Iri})");

    /// <summary>Compares every component.</summary>
    public static bool operator ==(ParseError left, ParseError right) => left.Equals(right);

    /// <summary>Compares every component.</summary>
    public static bool operator !=(ParseError left, ParseError right) => !left.Equals(right);
}
