// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Sparql.Algebra;

namespace Varve.Sparql;

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
