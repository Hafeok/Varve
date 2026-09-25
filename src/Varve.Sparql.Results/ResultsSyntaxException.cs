// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Sparql.Results;

/// <summary>
/// How a format parser stops. Never escapes the package: the reader catches it
/// and turns it into <see cref="SparqlResultsReader.Error"/>. A malformed result
/// document has no useful remainder, so the first error ends the read, and an
/// exception is the shortest way out of a recursive descent.
/// </summary>
internal sealed class ResultsSyntaxException : Exception
{
    internal ResultsSyntaxException(SparqlResultsErrorKind kind, ResultsPosition position, string message)
        : base(message)
    {
        Kind = kind;
        Position = position;
    }

    internal SparqlResultsErrorKind Kind { get; }

    internal ResultsPosition Position { get; }

    internal SparqlResultsError ToError() => new(Kind, Position, Message);
}
