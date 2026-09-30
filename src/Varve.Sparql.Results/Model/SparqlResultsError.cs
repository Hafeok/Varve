// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Sparql.Results.Model;

/// <summary>What went wrong in a result document.</summary>
public enum SparqlResultsErrorKind : byte
{
    /// <summary>No error.</summary>
    None,

    /// <summary>The document ended where more was required.</summary>
    UnexpectedEnd,

    /// <summary>The bytes are not valid UTF-8.</summary>
    InvalidUtf8,

    /// <summary>The document is not well-formed in its format's own syntax: XML, JSON, CSV or TSV.</summary>
    Malformed,

    /// <summary>The document is well-formed, but not a SPARQL result: an element, member or field is missing or out of place.</summary>
    UnexpectedStructure,

    /// <summary>An RDF term in the document is malformed: an unknown term type, an unterminated string, a bad escape.</summary>
    InvalidTerm,

    /// <summary>A binding names a variable the head does not declare, or names one twice in a solution.</summary>
    UnknownVariable,

    /// <summary>A row has a different number of fields from the head (CSV, TSV).</summary>
    FieldCount,

    /// <summary>The XML document has a document type declaration, which this reader refuses.</summary>
    DocumentTypeDeclaration,
}

/// <summary>An error in a result document, and where it is. The first error stops the reader.</summary>
public readonly struct SparqlResultsError : IEquatable<SparqlResultsError>
{
    /// <summary>An error of a kind, at a position.</summary>
    public SparqlResultsError(SparqlResultsErrorKind kind, ResultsPosition position, string message)
    {
        Kind = kind;
        Position = position;
        Message = message;
    }

    /// <summary>What went wrong.</summary>
    public SparqlResultsErrorKind Kind { get; }

    /// <summary>Where.</summary>
    public ResultsPosition Position { get; }

    /// <summary>A sentence for a person. Not a stable format.</summary>
    [DesignDecision(typeof(SyntaxModelSurfaces.ErrorMessagesAreDisplayText), Scope = ExceptionScope.Boundary)]
    public string Message { get; }

    /// <summary>Whether this is an error at all.</summary>
    public bool IsError => Kind != SparqlResultsErrorKind.None;

    /// <inheritdoc />
    public bool Equals(SparqlResultsError other) => Kind == other.Kind && Position == other.Position;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SparqlResultsError other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Position);

    /// <inheritdoc />
    public override string ToString() => IsError ? $"{Kind} at {Position}: {Message}" : "None";

    /// <summary>Compares kind and position.</summary>
    public static bool operator ==(SparqlResultsError left, SparqlResultsError right) => left.Equals(right);

    /// <summary>Compares kind and position.</summary>
    public static bool operator !=(SparqlResultsError left, SparqlResultsError right) => !left.Equals(right);
}
