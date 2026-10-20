// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// What a write's preconditions decided, before anything was read: the
/// position the client expects, and, for a graph or the graphs an update
/// named, the scope it expects it over (ADR 0126).
/// </summary>
internal readonly record struct WritePlan(bool Proceed, Position? Expected, IReadOnlyList<GraphPrecondition>? Graphs = null, GraphPresence Presence = GraphPresence.Any);

/// <summary>
/// What a Graph Store write's precondition requires of the graph, decided by
/// the pin the write itself takes (ADR 0126): <c>If-Match</c> needs a graph
/// that holds a quad, <c>If-None-Match: *</c> one that does not.
/// </summary>
internal enum GraphPresence : byte
{
    Any,
    Present,
    Absent,
}

/// <summary>
/// Where a conflict's evidence is read from (ADR 0126): the commits after
/// <see cref="Since"/> that touched <see cref="Scope"/>, or, with no scope,
/// every commit after it, its graphs the caller may read.
/// </summary>
internal readonly record struct ConflictEvidence(Position Since, IReadOnlyList<RequestTerm>? Scope);

/// <summary>
/// What every write shares (ADR 0094): the preconditions, the commit's
/// metadata, and the mapping of the store's outcome onto HTTP.
/// </summary>
internal static class Writes
{
    /// <summary>
    /// Checks a write's preconditions: no <c>Varve-As-Of</c>, no
    /// <c>If-None-Match</c>, and an <c>If-Match</c> that names the head. A
    /// stale <c>If-Match</c> is <c>412</c> with the head, before anything is
    /// read or parsed; a matching one is the commit's expected position.
    /// </summary>
    internal static async Task<WritePlan> CheckAsync(Exchange exchange, bool update = false)
    {
        HttpContext context = exchange.Context;

        if (!Preconditions.TryReadGraphs(context.Request.Headers[Preconditions.IfGraphsMatchHeader], out List<GraphPrecondition>? graphs))
        {
            await HttpProblems.BadRequest(context, "Varve-If-Graphs-Match names each graph once, as <iri>=<version> or default=<version>, separated by commas.").ConfigureAwait(false);
            return default;
        }

        if (graphs is not null)
        {
            if (!update)
            {
                await HttpProblems.BadRequest(context, "Varve-If-Graphs-Match is for SPARQL Update; a Graph Store write takes If-Match, the graph's version.").ConfigureAwait(false);
                return default;
            }

            if (!StringValues.IsNullOrEmpty(context.Request.Headers.IfMatch))
            {
                await HttpProblems.BadRequest(context, "An update states If-Match or Varve-If-Graphs-Match, not both (ADR 0126).").ConfigureAwait(false);
                return default;
            }
        }

        if (!StringValues.IsNullOrEmpty(context.Request.Headers[Preconditions.AsOfHeader]))
        {
            await HttpProblems.BadRequest(context, "A write is made at the head; Varve-As-Of is for reads.").ConfigureAwait(false);
            return default;
        }

        if (!StringValues.IsNullOrEmpty(context.Request.Headers.IfNoneMatch))
        {
            await HttpProblems.BadRequest(context, "A write takes If-Match, the position it expects; If-None-Match is for reads.").ConfigureAwait(false);
            return default;
        }

        switch (Preconditions.Read(context.Request.Headers.IfMatch, out Preconditions.TagList tags))
        {
            case TagCondition.Absent when graphs is not null:
                return await CheckGraphsAsync(exchange, graphs).ConfigureAwait(false);
            case TagCondition.Absent:
            case TagCondition.Any:
                return new WritePlan(true, null);
            case TagCondition.Positions when tags.TrySingle(out Position expected):
                Position head = exchange.Dataset.Head;

                if (expected != head)
                {
                    await PreconditionFailedAsync(exchange, head, expected).ConfigureAwait(false);
                    return default;
                }

                return new WritePlan(true, expected);
            default:
                await HttpProblems.BadRequest(context, "If-Match names one position, as the ETag of every response does.").ConfigureAwait(false);
                return default;
        }
    }

    // Every graph the update names is at the version the client read, at the
    // head, or 412 with the ones that are not — before anything is parsed.
    private static async Task<WritePlan> CheckGraphsAsync(Exchange exchange, List<GraphPrecondition> graphs)
    {
        List<(GraphPrecondition Graph, Position Version)> stale = [];

        foreach (GraphPrecondition graph in graphs)
        {
            Position version = exchange.Dataset.GraphVersion(Term(graph.Graph));

            if (version != graph.Version)
            {
                stale.Add((graph, version));
            }
        }

        if (stale.Count > 0)
        {
            await GraphPreconditionFailedAsync(exchange, stale).ConfigureAwait(false);
            return default;
        }

        return new WritePlan(true, null, graphs);
    }

    /// <summary>
    /// A Graph Store write's preconditions (ADR 0126), checked against the
    /// graph: <c>If-Match</c> names its version, compared strongly — an empty
    /// graph has no representation and matches no tag; <c>If-None-Match: *</c>
    /// on a <c>PUT</c> creates only, and fails when the graph holds a quad.
    /// A matching tag becomes the expected position, the graph its scope.
    /// </summary>
    internal static async Task<WritePlan> CheckGraphAsync(Exchange exchange, RdfTerm? graph, bool put)
    {
        HttpContext context = exchange.Context;

        if (!StringValues.IsNullOrEmpty(context.Request.Headers[Preconditions.AsOfHeader]))
        {
            await HttpProblems.BadRequest(context, "A write is made at the head; Varve-As-Of is for reads.").ConfigureAwait(false);
            return default;
        }

        if (!StringValues.IsNullOrEmpty(context.Request.Headers[Preconditions.IfGraphsMatchHeader]))
        {
            await HttpProblems.BadRequest(context, "Varve-If-Graphs-Match is for SPARQL Update; a Graph Store write takes If-Match, the graph's version.").ConfigureAwait(false);
            return default;
        }

        RequestTerm term = Term(graph);
        GraphPrecondition[] scope = [new GraphPrecondition(graph, default)];
        GraphPresence presence = GraphPresence.Any;

        switch (Preconditions.Read(context.Request.Headers.IfNoneMatch, out _))
        {
            case TagCondition.Absent:
                break;
            case TagCondition.Any when put:
                presence = GraphPresence.Absent;
                break;
            default:
                await HttpProblems.BadRequest(context, "A Graph Store write takes If-Match, the graph's version; If-None-Match: * only on PUT, to create a graph.").ConfigureAwait(false);
                return default;
        }

        switch (Preconditions.Read(context.Request.Headers.IfMatch, out Preconditions.TagList tags))
        {
            case TagCondition.Absent:
                return new WritePlan(true, null, scope, presence);
            case TagCondition.Any:
            case TagCondition.Positions when tags.TrySingle(out _):
                Position current = exchange.Dataset.GraphVersion(term);

                if (tags.Values is { Length: 1 } single && single[0] != current.Value)
                {
                    await GraphPreconditionFailedAsync(exchange, [(new GraphPrecondition(graph, new Position(single[0])), current)]).ConfigureAwait(false);
                    return default;
                }

                // The default graph always has a representation; a named one
                // only while it holds a quad, which the write's pin decides.
                return new WritePlan(true, tags.Values is { Length: 1 } ? current : null, scope, graph is null ? GraphPresence.Any : GraphPresence.Present);
            default:
                await HttpProblems.BadRequest(context, "If-Match names one version, as the ETag of a graph does.").ConfigureAwait(false);
                return default;
        }
    }

    /// <summary>
    /// Whether the graph's presence, as the write's own pin found it, meets
    /// the plan's precondition; when not, <c>412</c> has been written.
    /// </summary>
    internal static async Task<bool> PresenceHoldsAsync(Exchange exchange, WritePlan plan, RdfTerm? graph, bool present)
    {
        if (plan.Presence == GraphPresence.Any || (plan.Presence == GraphPresence.Present) == present)
        {
            return true;
        }

        Position version = exchange.Dataset.GraphVersion(Term(graph));
        await GraphPreconditionFailedAsync(exchange, [(new GraphPrecondition(graph, plan.Expected ?? version), version)]).ConfigureAwait(false);
        return false;
    }

    /// <summary>A graph as the store names it in a scope: <see cref="RequestTerm.None"/> for the default graph.</summary>
    internal static RequestTerm Term(RdfTerm? graph) => graph is null ? RequestTerm.None : RequestTerm.FromTerm(graph);

    /// <summary>The store's scope for a plan's graphs.</summary>
    internal static List<RequestTerm>? Scope(IReadOnlyList<GraphPrecondition>? graphs)
    {
        if (graphs is null)
        {
            return null;
        }

        List<RequestTerm> scope = new(graphs.Count);

        foreach (GraphPrecondition graph in graphs)
        {
            scope.Add(Term(graph.Graph));
        }

        return scope;
    }

    /// <summary><c>412</c> for graphs whose version is not the one named (ADR 0126): each with the version it has.</summary>
    private static Task GraphPreconditionFailedAsync(Exchange exchange, IReadOnlyList<(GraphPrecondition Graph, Position Version)> stale)
    {
        Position head = exchange.Dataset.Head;
        Preconditions.Describe(exchange.Response, head);
        return HttpProblems.WriteAsync(exchange.Context, ProblemType.PreconditionFailed, "A graph is not at the version named.", members =>
        {
            members.Number("position", head.Value);
            members.Number("headPosition", head.Value);
            Utf8JsonWriter json = members.Raw("graphs");
            json.WriteStartArray("graphs");

            foreach ((GraphPrecondition graph, Position version) in stale)
            {
                json.WriteStartObject();
                json.WriteString("graph", Name(graph.Graph));
                json.WriteNumber("version", version.Value);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        });
    }

    private static string Name(RdfTerm? graph) => graph is null ? "default" : Encoding.UTF8.GetString(graph.Lexical);

    /// <summary>The commit's agent — the caller, as the host names it — and its cause, the request id.</summary>
    internal static CommitMetadata Metadata(Exchange exchange)
    {
        string id = exchange.Context.TraceIdentifier;
        exchange.Response.Headers[Preconditions.RequestIdHeader] = id;
        return new CommitMetadata
        {
            Agent = exchange.Options.Identity.AgentOf(exchange.Context.User),
            Cause = RdfTerm.Literal(Encoding.UTF8.GetBytes(id)),
            Attachments = exchange.Authorised is { } position ? [ManagementPositionTerm(position)] : [],
        };
    }

    /// <summary>
    /// The validators a data commit carries into the sequencer for the host:
    /// the order of the management positions its dataset records (ADR 0165).
    /// </summary>
    internal static IReadOnlyList<ICommitValidator> Validators(Exchange exchange) =>
        exchange.Options.Management is { } management && exchange.Authorised is { } position
            ? [management.OrderOf(exchange.Name, position)]
            : [];

    /// <summary>The attachment a commit records its management position as (ADR 0165).</summary>
    internal static RdfTerm ManagementPositionTerm(Management.Model.ManagementPosition position) =>
        RdfTerm.Literal(Encoding.UTF8.GetBytes(position.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)), ManagementPositionDatatype);

    private static readonly RdfTerm ManagementPositionDatatype = RdfTerm.Iri(Encoding.UTF8.GetBytes(ManagementOrder.PositionDatatype));

    /// <summary>
    /// Commits the request; null when the dataset's storage no longer holds
    /// the writer epoch (ADR 0136), after answering <c>503 writer-superseded</c>:
    /// nothing was committed.
    /// </summary>
    internal static async Task<CommitResult?> CommitAsync(Exchange exchange, CommitRequest request)
    {
        try
        {
            return await exchange.Dataset.CommitAsync(request, exchange.Context.RequestAborted).ConfigureAwait(false);
        }
        catch (WriterSupersededException superseded)
        {
            await HttpProblems.WriteAsync(exchange.Context, ProblemType.WriterSuperseded, superseded.Message).ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>
    /// Answers a write's result: <c>204</c> (or <paramref name="created"/>'s
    /// <c>201</c>) for a commit or no change, <c>409</c>, <c>422</c>, <c>503</c>.
    /// Every one carries the position it leaves the dataset at.
    /// </summary>
    internal static async Task AnswerAsync(
        Exchange exchange, CommitResult result, int created = StatusCodes.Status204NoContent, Position? expected = null, ConflictEvidence? evidence = null, RdfTerm? taggedGraph = null, bool tagGraph = false)
    {
        HttpContext context = exchange.Context;
        Preconditions.Describe(context.Response, result.Position);

        // A Graph Store response is tagged with the graph's version (ADR 0126).
        if (tagGraph)
        {
            Preconditions.DescribeGraph(context.Response, result.Outcome == CommitOutcome.Committed ? result.Position : exchange.Dataset.GraphVersion(Term(taggedGraph)));
        }

        CommitConflicts? conflicts = null;

        if (result.Outcome == CommitOutcome.Conflict && evidence is { } read)
        {
            conflicts = await exchange.Dataset.ConflictsAsync(read.Since, read.Scope, 100, exchange.Options.Limits.MaxAsOfDistance, context.RequestAborted).ConfigureAwait(false);
        }

        await AnswerCoreAsync(exchange, result, created, expected, conflicts).ConfigureAwait(false);
    }

    private static Task AnswerCoreAsync(Exchange exchange, CommitResult result, int created, Position? expected, CommitConflicts? conflicts)
    {
        HttpContext context = exchange.Context;

        switch (result.Outcome)
        {
            case CommitOutcome.Committed:
                context.Response.StatusCode = created;
                ProtocolLog.Committed(exchange.Options.Logger, context.Request.Method + " " + context.Request.Path.Value, exchange.Name.Value, result.Position.Value, context.TraceIdentifier);
                return Task.CompletedTask;

            case CommitOutcome.NoChange:
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;

            case CommitOutcome.Conflict:
                return HttpProblems.WriteAsync(context, ProblemType.Conflict, "The head is " + result.Position.ToString() + ".", members =>
                {
                    members.Number("position", result.Position.Value);
                    members.Number("headPosition", result.Position.Value);

                    if (expected is Position wanted)
                    {
                        members.Number("expectedPosition", wanted.Value);
                    }

                    if (conflicts is not null)
                    {
                        WriteConflicts(exchange, members, conflicts);
                    }
                });

            case CommitOutcome.Rejected:
                return ValidatorProblem.TryWriteAsync(context, result.Report)
                    ?? HttpProblems.WriteAsync(context, ProblemType.Rejected, result.Reason, members => members.String("report", Report(result)));

            default:
                return QueryRun.WriteUnavailableAsync(exchange, result.Reason ?? "The dataset cannot commit now.");
        }
    }

    // The commits that caused a conflict (ADR 0126): newest first, each with
    // the graphs of the scope it touched — or, with no scope, those of its
    // graphs the caller may read, so the list names no graph it cannot see.
    private static void WriteConflicts(Exchange exchange, ProblemMembers members, CommitConflicts conflicts)
    {
        Utf8JsonWriter json = members.Raw("graphs");
        json.WriteStartArray("graphs");

        foreach (ConflictingCommit commit in conflicts.Commits)
        {
            json.WriteStartObject();
            json.WriteNumber("position", commit.Position.Value);
            json.WriteStartArray("graphs");

            foreach (RequestTerm graph in commit.Graphs)
            {
                RdfTerm? term = graph.IsNone ? null : graph.Term;

                if (exchange.Scope.Readable.Allows(term))
                {
                    json.WriteStringValue(Name(term));
                }
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        members.Raw("graphsTruncated").WriteBoolean("graphsTruncated", conflicts.Truncated);
    }

    /// <summary><c>403</c> <c>graph-not-writable</c> (ADR 0107): the request changes a graph outside the caller's writable scope; nothing was committed.</summary>
    internal static Task GraphNotWritableAsync(Exchange exchange, RdfTerm? graph)
    {
        string name = graph is null ? "the default graph" : "<" + Encoding.UTF8.GetString(graph.Lexical) + ">";
        return HttpProblems.WriteAsync(exchange.Context, ProblemType.GraphNotWritable, "Nothing was committed; the graph is " + name + ".",
            members => members.String("graph", graph is null ? "default" : Encoding.UTF8.GetString(graph.Lexical)));
    }

    /// <summary><c>412</c>: the expected position is not the head.</summary>
    internal static Task PreconditionFailedAsync(Exchange exchange, Position head, Position expected)
    {
        Preconditions.Describe(exchange.Response, head);
        return HttpProblems.WriteAsync(exchange.Context, ProblemType.PreconditionFailed, "The head is " + head.ToString() + ".", members =>
        {
            members.Number("position", head.Value);
            members.Number("headPosition", head.Value);
            members.Number("expectedPosition", expected.Value);
        });
    }

    // The validator's report, as N-Triples-style term lines.
    private static string Report(CommitResult result)
    {
        System.Buffers.ArrayBufferWriter<byte> text = new();

        foreach (RdfTerm term in result.Report)
        {
            TermLines.WriteTerm(text, term);
            TermLines.Write(text, "\n"u8);
        }

        return Encoding.UTF8.GetString(text.WrittenSpan);
    }
}
