// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Varve.Sparql.Evaluation;
using Varve.Store;

namespace Varve.Sparql.Store;

/// <summary>
/// How <see cref="SparqlUpdate"/> executes a request (<c>sparql-update-store.md</c>
/// §2). Immutable; nothing is ambient. Pre-commit validators are not here:
/// they are the dataset's (ADR 0058).
/// </summary>
public sealed class UpdateOptions
{
    /// <summary>
    /// The options every <c>WHERE</c> is evaluated with, and templates too:
    /// the clock and random source for <c>NOW()</c>, <c>RAND()</c>,
    /// <c>UUID()</c>, and the <c>SERVICE</c> handler.
    /// </summary>
    public EvaluationOptions Evaluation { get; init; } = new();

    /// <summary>The commit's agent, cause and graph scope, passed to the store as given.</summary>
    public CommitMetadata Metadata { get; init; } = new();

    /// <summary>Where <c>LOAD</c> reads documents. The default refuses every IRI.</summary>
    public ILoadSource LoadSource { get; init; } = RefusingLoadSource.Instance;

    /// <summary>
    /// How many times a request that met a <c>Conflict</c> is executed again
    /// from a fresh pin. Default 0: a conflict is returned, because whether
    /// the request still means the same against the new head is the caller's
    /// decision (ADR 0057).
    /// </summary>
    public int ConflictRetries
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }
}
