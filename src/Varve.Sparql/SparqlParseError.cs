// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Globalization;

namespace Varve.Sparql;

/// <summary>What went wrong, by class. The message says which token and which production.</summary>
public enum SparqlErrorKind : byte
{
    /// <summary>Nothing went wrong.</summary>
    None,

    /// <summary>The text does not match the grammar at this point.</summary>
    Syntax,

    /// <summary>The input ended where the grammar needed more.</summary>
    UnexpectedEnd,

    /// <summary>A byte sequence that is not UTF-8, or a UTF-16 surrogate with no partner.</summary>
    InvalidEncoding,

    /// <summary>An escape sequence that is malformed, out of place, or produces a surrogate.</summary>
    InvalidEscape,

    /// <summary>An IRI reference that fails RFC 3987 §2.2 after escape processing and prefix expansion.</summary>
    InvalidIri,

    /// <summary>A relative IRI with no base to resolve it against.</summary>
    RelativeIri,

    /// <summary>A prefixed name whose prefix was not declared.</summary>
    UndeclaredPrefix,

    /// <summary>A language tag that is not well formed, or a base direction that is not <c>ltr</c> or <c>rtl</c>.</summary>
    InvalidLanguageTag,

    /// <summary>A construct outside the version in force, or a version label that is not one of the three.</summary>
    Version,

    /// <summary>A variable assigned where it is already in scope, or projected where grouping hides it.</summary>
    VariableScope,

    /// <summary>A blank node where the grammar forbids one, or a label reused across basic graph patterns.</summary>
    BlankNode,

    /// <summary>An aggregate where none may appear, an aggregate inside an aggregate, or a variable a grouped level cannot see.</summary>
    Aggregate,

    /// <summary>A <c>VALUES</c> row of the wrong width, or a variable listed twice.</summary>
    Values,

    /// <summary>A number the parser cannot hold: <c>LIMIT</c> or <c>OFFSET</c> beyond 64 bits.</summary>
    Range,
}

/// <summary>A parse error: its kind, where it happened, and a message naming what was expected.</summary>
/// <remarks>
/// Positions are in the units of <c>docs/spec/sparql-grammar.md</c> §6: a byte
/// offset from the start of the UTF-8 input, a 1-based line, and a 1-based
/// column counted in bytes.
/// </remarks>
public readonly struct SparqlParseError : IEquatable<SparqlParseError>
{
    /// <summary>Builds an error.</summary>
    public SparqlParseError(SparqlErrorKind kind, long offset, int line, int column, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Kind = kind;
        Offset = offset;
        Line = line;
        Column = column;
        Message = message;
    }

    /// <summary>The class of error.</summary>
    public SparqlErrorKind Kind { get; }

    /// <summary>Bytes from the start of the input.</summary>
    public long Offset { get; }

    /// <summary>The line, counting from one.</summary>
    public int Line { get; }

    /// <summary>The byte within the line, counting from one.</summary>
    public int Column { get; }

    /// <summary>What was found and what was expected.</summary>
    public string Message { get; }

    /// <summary>True unless the kind is <see cref="SparqlErrorKind.None"/>.</summary>
    public bool IsError => Kind != SparqlErrorKind.None;

    /// <inheritdoc />
    public bool Equals(SparqlParseError other) =>
        Kind == other.Kind && Offset == other.Offset && Line == other.Line && Column == other.Column
        && string.Equals(Message, other.Message, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SparqlParseError other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Offset, Line, Column, Message);

    /// <summary>Renders as <c>line:column: kind: message</c>.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Line}:{Column} (byte {Offset}): {Kind}: {Message}");

    /// <summary>Same error.</summary>
    public static bool operator ==(SparqlParseError left, SparqlParseError right) => left.Equals(right);

    /// <summary>Different error.</summary>
    public static bool operator !=(SparqlParseError left, SparqlParseError right) => !left.Equals(right);
}

/// <summary>Thrown by the parsing entry points that do not return an error; <see cref="Error"/> has the details.</summary>
public sealed class SparqlParseException : Exception
{
    /// <summary>An exception carrying no error. For serialisation-shaped callers only.</summary>
    public SparqlParseException()
        : this(new SparqlParseError(SparqlErrorKind.Syntax, 0, 1, 1, "The text could not be parsed."))
    {
    }

    /// <summary>An exception with a message and no position.</summary>
    public SparqlParseException(string message)
        : this(new SparqlParseError(SparqlErrorKind.Syntax, 0, 1, 1, message))
    {
    }

    /// <summary>An exception with a message and a cause.</summary>
    public SparqlParseException(string message, Exception innerException)
        : base(message, innerException) =>
        Error = new SparqlParseError(SparqlErrorKind.Syntax, 0, 1, 1, message);

    /// <summary>An exception carrying an error.</summary>
    public SparqlParseException(SparqlParseError error)
        : base(error.ToString()) =>
        Error = error;

    /// <summary>The error.</summary>
    public SparqlParseError Error { get; }
}
