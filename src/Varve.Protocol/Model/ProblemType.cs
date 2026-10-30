// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Varve.Protocol.Model;

/// <summary>
/// An RFC 9457 problem type: an IRI under <c>https://w3id.org/varve/problems/</c>
/// (ADR 0092). Every error the protocol answers carries one, and
/// <see cref="ProblemCatalogue"/> fixes each one's status, title and members
/// (ADR 0119).
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

    /// <summary>A change to an event catalogue graph is not a well-formed declaration (ADR 0145).</summary>
    public static ProblemType EventDeclarationInvalid { get; } = new("event-declaration-invalid");

    /// <summary>A declaration change is incompatible with the version it keeps (ADR 0145).</summary>
    public static ProblemType EventSchemaIncompatible { get; } = new("event-schema-incompatible");

    /// <summary>A commit introduces a violation under a <c>reject</c> shapes binding (ADR 0169).</summary>
    public static ProblemType ShapesViolated { get; } = new("shapes-violated");

    /// <summary>A <c>reject</c> binding's shapes include a full shape and the binding does not allow full evaluation (ADR 0169).</summary>
    public static ProblemType BindingNotIncremental { get; } = new("binding-not-incremental");

    /// <summary>A binding's shapes graph is refused, or a validation under it failed (ADR 0169).</summary>
    public static ProblemType ShapesInvalid { get; } = new("shapes-invalid");

    /// <summary>An event could not be rendered; never a response, always an in-band CloudEvent's data (ADR 0143).</summary>
    public static ProblemType EventNotRendered { get; } = new("event-not-rendered");

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

    /// <summary>A dataset to delete is declared open; declare it closed first (ADRs 0106, 0163).</summary>
    public static ProblemType DatasetOpen { get; } = new("dataset-open");

    /// <summary>A commit that records its agent — a settings change — from a caller with none (ADRs 0094, 0106).</summary>
    public static ProblemType AgentRequired { get; } = new("agent-required");

    /// <summary><c>403</c>: the request changes a graph outside the caller's writable scope (ADR 0107).</summary>
    public static ProblemType GraphNotWritable { get; } = new("graph-not-writable");

    /// <summary><c>401</c>, deliberately thin: no valid bearer token (ADR 0119).</summary>
    public static ProblemType Unauthorized { get; } = new("unauthorized");

    /// <summary><c>403</c>: the caller is authenticated and holds no permission the request needs (ADR 0119).</summary>
    public static ProblemType Forbidden { get; } = new("forbidden");

    /// <summary><c>404</c> for an address nothing is served at (ADR 0119).</summary>
    public static ProblemType NotFound { get; } = new("not-found");

    /// <summary><c>503</c> from readiness: a dataset is not open, failed, or behind (ADR 0113); the body lists them.</summary>
    public static ProblemType NotReady { get; } = new("not-ready");

    /// <summary><c>503</c>: a newer writer holds the dataset's epoch; this instance serves reads only (ADR 0136).</summary>
    public static ProblemType WriterSuperseded { get; } = new("writer-superseded");

    /// <summary><c>503</c>: the server has not applied the management position the request asked for, after one advance (ADR 0163).</summary>
    public static ProblemType ManagementBehind { get; } = new("management-behind");

    /// <summary><c>503</c>: a follower has not applied the dataset position the read asked for, after one advance (ADR 0168).</summary>
    public static ProblemType DatasetBehind { get; } = new("dataset-behind");

    /// <summary><c>503</c>: a read of a report graph at a position its validation projection has not reached, after one bounded wait (ADR 0174).</summary>
    public static ProblemType ValidationBehind { get; } = new("validation-behind");

    /// <summary><c>503</c>: a forwarded write reached an instance that does not write the dataset; it is never forwarded again (ADR 0168).</summary>
    public static ProblemType WriterMoved { get; } = new("writer-moved");

    /// <summary><c>409</c>: a write to a catalogue graph after the dataset's event declarations froze or moved to the management plane (ADR 0167).</summary>
    public static ProblemType EventCatalogueMoved { get; } = new("event-catalogue-moved");

    /// <summary><c>422</c>: a management declaration the code validator refuses (ADR 0166).</summary>
    public static ProblemType DeclarationInvalid { get; } = new("declaration-invalid");

    /// <summary><c>503</c>: this instance's reader record was forgotten while it read, and an extent its catalog names is gone; it attaches again (ADR 0160).</summary>
    public static ProblemType ReaderForgotten { get; } = new("reader-forgotten");

    /// <summary><c>503</c>: a read opened unannounced found an extent its catalog names gone (ADR 0160).</summary>
    public static ProblemType ReadUnannouncedExtentGone { get; } = new("read-unannounced-extent-gone");

    /// <summary><c>503</c>: a bulk load found one of its own spills damaged and ended with nothing committed; submit the same input again (ADR 0213).</summary>
    public static ProblemType LoadSpillDamaged { get; } = new("load-spill-damaged");

    /// <summary><c>503</c>: the write may or may not have been committed: closed in the log with its flush unconfirmed, settled by the commits resource at <c>position</c> by the cause <c>requestId</c>; or forwarded, with the writer's answer lost, without a position (ADR 0214).</summary>
    public static ProblemType CommitOutcomeUnknown { get; } = new("commit-outcome-unknown");

    /// <summary><c>507</c>: the dataset's storage has no space for the write; nothing was committed, and the next write tries again (ADR 0214).</summary>
    public static ProblemType StorageFull { get; } = new("storage-full");

    /// <summary><c>503</c>: as many reads in flight and queued as the server allows (ADR 0114).</summary>
    public static ProblemType ServerBusy { get; } = new("server-busy");

    /// <summary><c>422</c>: an evaluation over the per-request memory bound (ADR 0114).</summary>
    public static ProblemType MemoryLimitExceeded { get; } = new("memory-limit-exceeded");

    /// <summary><c>422</c>: an as-of position too far from its nearest checkpoint (ADR 0114).</summary>
    public static ProblemType AsOfDistanceExceeded { get; } = new("as-of-distance-exceeded");

    /// <summary><c>429</c>: the client holds too many live tails open (ADR 0114).</summary>
    public static ProblemType TooManyLiveTails { get; } = new("too-many-live-tails");

    /// <summary><c>429</c>: more probes of a rate-limited endpoint than its window allows (ADR 0113).</summary>
    public static ProblemType TooManyRequests { get; } = new("too-many-requests");

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
