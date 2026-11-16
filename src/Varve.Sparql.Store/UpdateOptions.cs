// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using Varve.Rdf;
using Varve.Sparql.Evaluation;
using Varve.Store;
using Varve.Store.Log;

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

    /// <summary>
    /// The position the request must be evaluated at, or <see langword="null"/>
    /// for whatever the head is. When given and the pinned head is elsewhere,
    /// the request is not evaluated and the result is the store's
    /// <see cref="CommitOutcome.Conflict"/> with the head; when it matches, it
    /// is the commit's expected position, and <see cref="ConflictRetries"/>
    /// does not apply. HTTP's <c>If-Match</c> (ADR 0094).
    /// </summary>
    public Position? ExpectedPosition { get; init; }

    /// <summary>
    /// The graphs the client read, each with the version it read it at (ADR
    /// 0126, <c>Varve-If-Graphs-Match</c>), or <see langword="null"/>. When
    /// given, a graph whose version at the pin is another is a
    /// <see cref="CommitOutcome.Conflict"/> without evaluation; otherwise the
    /// request commits expecting the pinned position with these graphs as its
    /// scope, in place of the scope inferred from what it read (ADR 0057,
    /// amended 2026-10-13), and <see cref="ConflictRetries"/> does not apply.
    /// Not combined with <see cref="ExpectedPosition"/>.
    /// </summary>
    public IReadOnlyList<ExpectedGraphVersion>? ExpectedGraphs { get; init; }

    /// <summary>The commit's agent, cause and graph scope, passed to the store as given.</summary>
    public CommitMetadata Metadata { get; init; } = new();

    /// <summary>
    /// The request's own validators, passed to the store as given and run after
    /// the dataset's (ADR 0058): a host's check that runs under the sequencer,
    /// such as the order of management positions (ADR 0165).
    /// </summary>
    public IReadOnlyList<ICommitValidator> Validators { get; init; } = [];

    /// <summary>Where <c>LOAD</c> reads documents. The default refuses every IRI.</summary>
    public ILoadSource LoadSource { get; init; } = RefusingLoadSource.Instance;

    /// <summary>
    /// The graphs every pattern of the request reads (ADR 0107): a
    /// <c>DELETE WHERE</c>, a <c>DELETE/INSERT … WHERE</c>, <c>CLEAR</c>,
    /// <c>COPY</c> and the rest evaluate over the staging view seen through
    /// this scope, so a caller cannot delete, copy or move what it cannot
    /// read. Every graph by default; the scope of every graph costs nothing.
    /// </summary>
    public GraphScope ReadScope { get; init; } = GraphScope.All;

    /// <summary>
    /// The graphs the request may change (ADR 0107). The composed delta is
    /// checked against it before the submit: one quad outside it fails the
    /// whole request with <see cref="Varve.Rdf.GraphNotWritableException"/>, and
    /// nothing is committed. Every graph by default.
    /// </summary>
    public GraphScope WriteScope { get; init; } = GraphScope.All;

    /// <summary>
    /// The rows every pattern of the request reads, per classifier binding
    /// (ADR 0235): beneath <see cref="ReadScope"/>, a pattern sees only the
    /// quads the caller's subject sets allow, and a retraction of a quad it
    /// does not see is a no-op (ADR 0236, W5). Every row by default, which
    /// builds nothing.
    /// </summary>
    public RowScope ReadRows { get; init; } = RowScope.All;

    /// <summary>
    /// The rows the request may write (ADR 0236). The requested quads are
    /// checked against the row-level write rule before the submit, and one
    /// that breaks it fails the whole request with
    /// <see cref="Varve.Rdf.RowNotWritableException"/>. Every row by default.
    /// </summary>
    public RowScope WriteRows { get; init; } = RowScope.All;

    /// <summary>
    /// The dataset's classifier (ADR 0231), which the row scopes are judged
    /// by: of the staging view as the request reads it, and of the state the
    /// request would make. None when the dataset has no classifier binding;
    /// then the row scopes restrict nothing.
    /// </summary>
    public IClassifier? Classifier { get; init; }

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
