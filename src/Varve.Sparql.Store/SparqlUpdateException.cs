// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Sparql.Store;

/// <summary>
/// An operation of an update request failed, so the request did (SPARQL 1.1
/// Update §2.2): nothing was committed.
/// </summary>
public sealed class SparqlUpdateException : Exception
{
    /// <summary>An update failure with no operation named.</summary>
    public SparqlUpdateException()
    {
    }

    /// <summary>An update failure with a message.</summary>
    public SparqlUpdateException(string message)
        : base(message)
    {
    }

    /// <summary>An update failure with a message and its cause.</summary>
    public SparqlUpdateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The operation that failed, by its index in the request and its kind.</summary>
    public SparqlUpdateException(int operationIndex, string operationKind, string message, Exception? innerException = null)
        : base("Operation " + (operationIndex + 1) + " (" + operationKind + ") failed: " + message, innerException)
    {
        OperationIndex = operationIndex;
        OperationKind = operationKind;
    }

    /// <summary>The failed operation's zero-based index in the request, or -1.</summary>
    public int OperationIndex { get; } = -1;

    /// <summary>The failed operation's kind — <c>LOAD</c>, <c>CREATE</c>, … — or empty.</summary>
    public string OperationKind { get; } = string.Empty;
}
