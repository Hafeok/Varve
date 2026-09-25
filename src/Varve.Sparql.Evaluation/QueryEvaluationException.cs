// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Sparql.Evaluation;

/// <summary>
/// An error that fails a whole query, as opposed to an expression error, which
/// never does (SPARQL 1.1 §17.2): a <c>SERVICE</c> without <c>SILENT</c> whose
/// endpoint failed, <c>NOW()</c> without a clock, <c>RAND()</c> without
/// randomness, a custom aggregate nobody supplied. The message names the cause.
/// </summary>
public sealed class QueryEvaluationException : Exception
{
    /// <summary>An exception with no message.</summary>
    public QueryEvaluationException()
    {
    }

    /// <summary>An exception naming its cause.</summary>
    public QueryEvaluationException(string message)
        : base(message)
    {
    }

    /// <summary>An exception naming its cause, wrapping another.</summary>
    public QueryEvaluationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
