// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Model;

/// <summary>
/// An RFC 9457 problem type: an IRI under <c>https://w3id.org/varve/problems/</c>
/// (ADR 0092). Every error the protocol answers carries one.
/// </summary>
public readonly record struct ProblemType
{
    /// <summary>The namespace every Varve problem type is in.</summary>
    public const string Namespace = "https://w3id.org/varve/problems/";

    private ProblemType(string name) => Value = Namespace + name;

    /// <summary>The type's IRI.</summary>
    public string Value { get; }

    /// <summary>A query or update that does not parse; the body carries line, column and offset.</summary>
    public static ProblemType SparqlSyntax { get; } = new("sparql-syntax");

    /// <summary>A request the protocol does not accept: a missing or repeated parameter, a bad value.</summary>
    public static ProblemType BadRequest { get; } = new("bad-request");

    /// <summary>A request body in a media type or charset the endpoint does not read.</summary>
    public static ProblemType UnsupportedMediaType { get; } = new("unsupported-media-type");

    /// <summary>Nothing in <c>Accept</c> that the endpoint can write.</summary>
    public static ProblemType NotAcceptable { get; } = new("not-acceptable");

    /// <summary>A method the endpoint does not serve.</summary>
    public static ProblemType MethodNotAllowed { get; } = new("method-not-allowed");

    /// <summary>A dataset name that is not valid or not configured; the two are one answer.</summary>
    public static ProblemType DatasetNotFound { get; } = new("dataset-not-found");

    /// <summary>A Graph Store request for a graph that holds no quad.</summary>
    public static ProblemType GraphNotFound { get; } = new("graph-not-found");

    /// <summary>An as-of position, or a feed's end, after the head.</summary>
    public static ProblemType PositionNotReached { get; } = new("position-not-reached");

    /// <summary>An as-of time before the first commit.</summary>
    public static ProblemType BeforeFirstCommit { get; } = new("before-first-commit");

    /// <summary>A position below the archive horizon (spec T3). Reserved: no horizon exists yet.</summary>
    public static ProblemType BelowArchiveHorizon { get; } = new("below-archive-horizon");

    /// <summary>The sequencer's <c>Conflict</c>: another commit came first.</summary>
    public static ProblemType Conflict { get; } = new("conflict");

    /// <summary>An <c>If-Match</c> position that is not the head.</summary>
    public static ProblemType PreconditionFailed { get; } = new("precondition-failed");

    /// <summary>A pre-commit validator rejected the commit; the body carries its report.</summary>
    public static ProblemType Rejected { get; } = new("rejected");

    /// <summary>The dataset cannot commit now: failed, closing, or behind.</summary>
    public static ProblemType Unavailable { get; } = new("unavailable");

    /// <summary>A read cut by its time or size limit.</summary>
    public static ProblemType ReadLimitExceeded { get; } = new("read-limit-exceeded");

    /// <summary>A request body over the limit.</summary>
    public static ProblemType RequestTooLarge { get; } = new("request-too-large");

    /// <summary>An update operation that failed, or a query that failed while evaluating.</summary>
    public static ProblemType OperationFailed { get; } = new("operation-failed");

    /// <summary>An RDF body that does not parse.</summary>
    public static ProblemType RdfSyntax { get; } = new("rdf-syntax");

    /// <summary>The server is shutting down.</summary>
    public static ProblemType ShuttingDown { get; } = new("shutting-down");

    /// <summary>
    /// The problem type an IRI under <see cref="Namespace"/> names, known or
    /// not: a client reading a newer server's error keeps its type.
    /// </summary>
    public static bool TryParse(string? iri, out ProblemType type)
    {
        if (iri is not null && iri.Length > Namespace.Length && iri.StartsWith(Namespace, StringComparison.Ordinal))
        {
            type = new ProblemType(iri[Namespace.Length..]);
            return true;
        }

        type = default;
        return false;
    }

    /// <summary>Whether this is the type <paramref name="iri"/> names.</summary>
    public bool Is(ReadOnlySpan<char> iri) => iri.SequenceEqual(Value);

    /// <summary>Renders as the IRI.</summary>
    public override string ToString() => Value;
}
