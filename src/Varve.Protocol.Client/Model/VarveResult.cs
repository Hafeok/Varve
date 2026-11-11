// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Protocol.Client.Model;

/// <summary>
/// What a typed read answered: the value, or the problem, or both when a
/// problem's body is itself the resource (a <c>503 not-ready</c> is a
/// readiness), always with the response's facts.
/// </summary>
/// <remarks>
/// An answer from the server is a value, never an exception, as every request
/// method of <see cref="SparqlHttpClient"/> answers its response whatever the
/// status. Exceptions are for no answer: a timeout, or a token known to have
/// expired.
/// </remarks>
public sealed class VarveResult<T>
    where T : class
{
    /// <summary>A result with these parts.</summary>
    public VarveResult(T? value, Problem? problem, ResponseFacts facts)
    {
        Value = value;
        Problem = problem;
        Facts = facts.With(problem);
    }

    /// <summary>The resource, when the response held one.</summary>
    public T? Value { get; }

    /// <summary>The problem, when the response was one.</summary>
    public Problem? Problem { get; }

    /// <summary>The status, positions and request id of the response.</summary>
    public ResponseFacts Facts { get; }
}
