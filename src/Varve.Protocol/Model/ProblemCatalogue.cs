// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;

namespace Varve.Protocol.Model;

/// <summary>
/// One problem type's fixed shape (ADR 0119): its <c>title</c>, its
/// <c>status</c>, and the extension members it may carry. Every problem the
/// protocol writes takes its shape from here, and its page in
/// <c>docs/problems/</c> states the same.
/// </summary>
public sealed class ProblemShape
{
    internal ProblemShape(ProblemType type, int status, string title, params string[] members)
    {
        Type = type;
        Status = status;
        Title = title;
        Members = [.. members];
    }

    /// <summary>The type, an IRI under <see cref="ProblemType.Namespace"/>.</summary>
    public ProblemType Type { get; }

    /// <summary>The HTTP status every problem of this type is answered with, as <c>HttpResponse.StatusCode</c> takes it.</summary>
    [DesignDecision(typeof(HeadersAndTheProblemCatalogue.ProblemShapeBoundaryPrimitives), Scope = ExceptionScope.Boundary)]
    public int Status { get; }

    /// <summary>The short, human-readable summary of the type, the same on every instance.</summary>
    [DesignDecision(typeof(HeadersAndTheProblemCatalogue.ProblemShapeBoundaryPrimitives), Scope = ExceptionScope.Boundary)]
    public string Title { get; }

    /// <summary>The extension members an instance may carry, and no other.</summary>
    public ImmutableArray<string> Members { get; }

    /// <summary>The page's name under <c>docs/problems/</c>: the IRI's last segment.</summary>
    [DesignDecision(typeof(HeadersAndTheProblemCatalogue.ProblemShapeBoundaryPrimitives), Scope = ExceptionScope.Boundary)]
    public string Name => Type.Value[ProblemType.Namespace.Length..];

    /// <summary>Whether an instance may carry <paramref name="member"/>.</summary>
    internal bool Allows(string member) => Members.Contains(member, StringComparer.Ordinal);
}

/// <summary>
/// The catalogue of every problem type the server can emit (ADR 0119), each
/// with its status, title and members fixed. The writer emits only from it;
/// a test enumerates it against <c>docs/problems/</c>.
/// </summary>
public static class ProblemCatalogue
{
    /// <summary>Every type, in status order.</summary>
    public static ImmutableArray<ProblemShape> All { get; } =
    [
        new(ProblemType.SparqlSyntax, 400, "The request does not parse.", "line", "column", "offset"),
        new(ProblemType.RdfSyntax, 400, "The RDF body does not parse."),
        new(ProblemType.BadRequest, 400, "The request is not one the protocol accepts."),
        new(ProblemType.OperationFailed, 400, "An operation failed; nothing was committed."),
        new(ProblemType.Unauthorized, 401, "The request carries no valid bearer token."),
        new(ProblemType.Forbidden, 403, "The caller does not hold the permission the request needs."),
        new(ProblemType.AgentRequired, 403, "The commit records its agent, and the caller has none."),
        new(ProblemType.GraphNotWritable, 403, "The request changes a graph outside the caller's writable scope.", "graph"),
        new(ProblemType.DatasetNotFound, 404, "No dataset by that name."),
        new(ProblemType.NotFound, 404, "Nothing is served at that address."),
        new(ProblemType.GraphNotFound, 404, "No graph by that name holds a quad."),
        new(ProblemType.PositionNotReached, 404, "The position is after the head.", "headPosition"),
        new(ProblemType.BeforeFirstCommit, 404, "The time is before the first commit."),
        new(ProblemType.BelowArchiveHorizon, 404, "The position is below the archive horizon.", "horizon"),
        new(ProblemType.MethodNotAllowed, 405, "The endpoint does not serve this method."),
        new(ProblemType.NotAcceptable, 406, "Nothing in Accept is a format this endpoint writes."),
        new(ProblemType.Conflict, 409, "Another commit came first.", "position", "expectedPosition", "headPosition"),
        new(ProblemType.DatasetExists, 409, "A dataset by that name exists."),
        new(ProblemType.DatasetOpen, 409, "The dataset is open; close it first."),
        new(ProblemType.PreconditionFailed, 412, "If-Match does not name the head.", "position", "expectedPosition", "headPosition"),
        new(ProblemType.RequestTooLarge, 413, "The request body is over the server's limit.", "limit"),
        new(ProblemType.UnsupportedMediaType, 415, "The request body is in a media type or charset this endpoint does not read."),
        new(ProblemType.Rejected, 422, "A validator rejected the commit.", "report"),
        new(ProblemType.MemoryLimitExceeded, 422, "The request needs more memory than the server allows one request.", "limit", "actual"),
        new(ProblemType.AsOfDistanceExceeded, 422, "The as-of position is too far from its nearest checkpoint.", "limit", "actual"),
        new(ProblemType.TooManyLiveTails, 429, "The client holds too many live tails open.", "limit"),
        new(ProblemType.Unavailable, 503, "The dataset cannot answer now."),
        new(ProblemType.ReadLimitExceeded, 503, "The read was cut by a server limit."),
        new(ProblemType.ShuttingDown, 503, "The server is shutting down."),
        new(ProblemType.NotReady, 503, "The server is not ready.", "datasets"),
        new(ProblemType.ServerBusy, 503, "The server has as many reads in flight as it allows.", "limit"),
    ];

    private static readonly FrozenDictionary<string, ProblemShape> ByType = Index();

    /// <summary>The shape of <paramref name="type"/>.</summary>
    /// <exception cref="KeyNotFoundException">The type is not catalogued, which is a defect: every type the protocol writes is.</exception>
    public static ProblemShape Of(ProblemType type) => ByType[type.Value];

    /// <summary>The shape of <paramref name="type"/>, when it is catalogued.</summary>
    public static bool TryGet(ProblemType type, out ProblemShape? shape) => ByType.TryGetValue(type.Value, out shape);

    /// <summary>
    /// Writes a problem of <paramref name="type"/> as a JSON object: the type,
    /// the catalogue's title and status, <paramref name="detail"/> when given,
    /// and <paramref name="instance"/>, the request id, when given. For a host
    /// writing a problem of its own (a route nobody serves, a drain, a refused
    /// caller) outside the protocol's endpoints.
    /// </summary>
    [DesignDecision(typeof(HeadersAndTheProblemCatalogue.ProblemShapeBoundaryPrimitives), Scope = ExceptionScope.Boundary)]
    public static void Write(Utf8JsonWriter json, ProblemType type, string? instance, string? detail)
    {
        ArgumentNullException.ThrowIfNull(json);
        ProblemShape shape = Of(type);
        json.WriteStartObject();
        json.WriteString("type", type.Value);
        json.WriteString("title", shape.Title);
        json.WriteNumber("status", shape.Status);

        if (detail is not null)
        {
            json.WriteString("detail", detail);
        }

        if (instance is not null)
        {
            json.WriteString("instance", instance);
        }

        json.WriteEndObject();
    }

    private static FrozenDictionary<string, ProblemShape> Index()
    {
        Dictionary<string, ProblemShape> index = new(StringComparer.Ordinal);

        foreach (ProblemShape shape in All)
        {
            index.Add(shape.Type.Value, shape);
        }

        return index.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
