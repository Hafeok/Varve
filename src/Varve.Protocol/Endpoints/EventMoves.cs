// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Varve.Delta.Model;
using Varve.Events;
using Varve.Events.Model;
using Varve.Management;
using Varve.Management.Model;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// The move of a dataset's event declarations to the management plane (ADR
/// 0167), driven by the dataset's writer:
/// <list type="number">
/// <item>the freeze, <c>varve:eventDeclarations varve:Moving</c>, a management
/// write committed by the system writer at m₁;</item>
/// <item>the barrier: one probe commit through the dataset's sequencer,
/// authorised at the applied position, refused by design, after which no
/// commit can change a catalogue graph;</item>
/// <item>the copy and the switch: the catalogue at the head, translated into
/// the dataset's graph with <c>varve:Moved</c>, a management write under the
/// graph's version, committed at m₂.</item>
/// </list>
/// Each management write is local when this instance writes the system
/// dataset, and otherwise sent to the system writer under the caller's token,
/// with the hop header, so that it is never forwarded again (ADR 0168). The
/// move is idempotent: a run that finds <c>Moving</c> starts at the barrier,
/// one that finds <c>Moved</c> has nothing to do.
/// </summary>
internal static class EventMoves
{
    // Rounds a move makes when a write finds the graph's version moved on.
    private const int Rounds = 5;

    private static readonly RdfTerm Barrier = RdfTerm.Iri("urn:x-varve:event-declarations-move-barrier"u8);

    /// <summary><c>GET /manage/datasets/{name}/events</c>: where the dataset's event declarations are, and, once moved, them, as TriG on request.</summary>
    internal static async Task EventsAsync(HttpContext context, ProtocolOptions options, IManagementPlane plane)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "GET, HEAD").ConfigureAwait(false);
            return;
        }

        if (Name(context) is not { } name)
        {
            await HttpProblems.BadRequest(context, "A dataset name is 1 to 63 characters of [A-Za-z0-9._-], starting with a letter or a digit.").ConfigureAwait(false);
            return;
        }

        if (!await Exchange.AuthorizeDatasetAsync(context, options, DatasetPermissions.Admin, name).ConfigureAwait(false))
        {
            return;
        }

        GrantTable table = plane.Table;

        if (!table.Declarations.Datasets.TryGetValue(name.Value, out DatasetDeclaration? declared))
        {
            await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
            return;
        }

        Dataset system = plane.System;
        RdfTerm graph = ManagementVocabulary.DatasetGraph(name.Value);
        context.Response.Headers.CacheControl = "no-cache";
        Preconditions.Describe(context.Response, system.Head);
        Preconditions.DescribeGraph(context.Response, system.GraphVersion(RequestTerm.FromTerm(graph)));

        if (context.Request.Headers.Accept.ToString().Contains("application/trig", StringComparison.OrdinalIgnoreCase))
        {
            List<(RdfTerm, RdfTerm, RdfTerm)> nodes;

            using (DatasetView view = system.Pin())
            {
                nodes = EventNodes(Triples(view, graph));
            }

            InMemoryDatasetBuilder builder = new();

            foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in nodes)
            {
                builder.Add(s, p, o, graph);
            }

            byte[] trig = ManagementTrig.Write(builder.ToDataset(), [graph]);
            context.Response.ContentType = "application/trig; charset=utf-8";
            context.Response.ContentLength = trig.Length;

            if (!HttpMethods.IsHead(context.Request.Method))
            {
                await context.Response.Body.WriteAsync(trig, context.RequestAborted).ConfigureAwait(false);
            }

            return;
        }

        int types;

        using (DatasetView view = system.Pin())
        {
            types = Triples(view, graph).Count(t => t.Predicate.Equals(Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type")) && t.Object.Equals(Iri(ManagementVocabulary.Namespace + "EventType")));
        }

        Freeze? since = declared.EventDeclarations == EventDeclarationState.Moving ? await FreezeOfAsync(system, graph, context.RequestAborted).ConfigureAwait(false) : null;
        await WriteJsonAsync(context, StatusCodes.Status200OK, State(declared.EventDeclarations), table.Position.Value, types, since?.Since).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>POST /manage/datasets/{name}/events/move</c>, by an operator or the
    /// dataset's admin: sent to the dataset's writer, which moves the
    /// declarations, or finishes a move that stopped, and answers where they
    /// are.
    /// </summary>
    internal static async Task MoveAsync(HttpContext context, ProtocolOptions options, IManagementPlane plane)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "POST").ConfigureAwait(false);
            return;
        }

        if (Name(context) is not { } name)
        {
            await HttpProblems.BadRequest(context, "A dataset name is 1 to 63 characters of [A-Za-z0-9._-], starting with a letter or a digit.").ConfigureAwait(false);
            return;
        }

        // The dataset's writer drives the move (ADR 0167 point 3).
        if (await Forwarding.RouteAsync(context, options, new WriterTarget(name)).ConfigureAwait(false))
        {
            return;
        }

        if (!await Exchange.AuthorizeDatasetAsync(context, options, DatasetPermissions.Admin, name).ConfigureAwait(false))
        {
            return;
        }

        Outcome outcome = await RunAsync(new Move(options, plane, name, context), context.RequestAborted).ConfigureAwait(false);

        if (outcome.Problem is { } problem)
        {
            await problem(context).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(context, StatusCodes.Status200OK, "moved", outcome.Position, outcome.Types).ConfigureAwait(false);
    }

    /// <summary>
    /// Finishes a move this instance can finish alone (ADR 0167 point 3): it
    /// writes the dataset and the system dataset both, so no caller's token is
    /// needed. True when the declarations are moved after it.
    /// </summary>
    internal static async Task<bool> ResumeAsync(ProtocolOptions options, IManagementPlane plane, DatasetName name, CancellationToken cancellationToken)
    {
        Outcome outcome = await RunAsync(new Move(options, plane, name, null), cancellationToken).ConfigureAwait(false);
        return outcome.Problem is null;
    }

    // ---- the driver ----

    private sealed record Move(ProtocolOptions Options, IManagementPlane Plane, DatasetName Name, HttpContext? Context);

    private sealed record Outcome(long Position, int Types, Func<HttpContext, Task>? Problem);

    private static Outcome Refused(ProblemType type, string detail, Action<ProblemMembers>? members = null) =>
        new(0, 0, context => HttpProblems.WriteAsync(context, type, detail, members));

    private static async Task<Outcome> RunAsync(Move move, CancellationToken cancellationToken)
    {
        ProtocolOptions options = move.Options;
        IManagementPlane plane = move.Plane;
        RdfTerm graph = ManagementVocabulary.DatasetGraph(move.Name.Value);

        for (int round = 0; round < Rounds; round++)
        {
            if (options.Management is { } management)
            {
                await management.AdvanceAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!options.Datasets.TryResolve(move.Name, out Dataset? dataset))
            {
                return Refused(ProblemType.DatasetNotFound, "No dataset by that name is open here.");
            }

            if (dataset.IsFollower)
            {
                return Refused(ProblemType.WriterMoved, "This instance no longer writes the dataset; send the move again.");
            }

            Dataset system = plane.System;
            List<Triple> current;
            Position version;
            long applied;

            using (DatasetView view = system.Pin())
            {
                current = Triples(view, graph);
                version = system.GraphVersion(RequestTerm.FromTerm(graph));
                applied = view.Position.Value;
            }

            if (DatasetNode(current) is not { } node)
            {
                return Refused(ProblemType.DatasetNotFound, "The dataset is not declared at the applied management position.");
            }

            EventDeclarationState state = StateOf(current, node);

            if (state == EventDeclarationState.Moved)
            {
                return new Outcome(applied, EventNodes(current).Count(t => t.Item2.Equals(RdfTypeTerm) && t.Item3.Equals(EventTypeTerm)), null);
            }

            if (state == EventDeclarationState.InDataset)
            {
                // The freeze.
                List<Triple> frozen = current.FindAll(t => !t.Predicate.Equals(ManagementVocabulary.EventDeclarationsProperty));
                frozen.Add(new Triple(node, ManagementVocabulary.EventDeclarationsProperty, ManagementVocabulary.MovingState));
                Written freeze = await WriteAsync(move, graph, frozen, version, cancellationToken).ConfigureAwait(false);

                if (freeze.Problem is { } problem)
                {
                    return new Outcome(0, 0, problem);
                }

                await AppliedAsync(move, freeze.Position, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // The barrier: after it, every commit is authorised at the applied
            // position or later, where the declarations say Moving.
            if (!await BarrierAsync(move, dataset, applied, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            // The copy, from the catalogue the freeze made final.
            List<FeedChange> catalogue;

            using (DatasetView view = dataset.Pin())
            {
                catalogue = Catalogue(view);
            }

            HashSet<RdfTerm> held = [.. current.Select(t => t.Subject)];

            if (!ManagedEventDeclarations.TryFromCatalogue(catalogue, out List<(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object)> nodes, out DeclarationProblem? untranslatable)
                || nodes.Find(n => held.Contains(n.Subject)) is { Subject: not null } taken)
            {
                // A catalogue that does not translate one-to-one: the move is
                // cancelled, and catalogue writes are accepted again.
                DeclarationProblem problem = untranslatable ?? new DeclarationProblem(
                    nodes.Find(n => held.Contains(n.Subject)).Subject, "graph", "the node is already a node of the dataset's declarations; the catalogue cannot be copied into them");
                // The cancel is a commit of its own, m₃: catalogue writes authorised there or later are accepted again.
                List<Triple> cancelled = current.FindAll(t => !t.Predicate.Equals(ManagementVocabulary.EventDeclarationsProperty));
                cancelled.Add(new Triple(node, ManagementVocabulary.EventDeclarationsProperty, ManagementVocabulary.InDatasetState));
                Written cancel = await WriteAsync(move, graph, cancelled, version, cancellationToken).ConfigureAwait(false);

                if (cancel.Stale)
                {
                    continue;
                }

                // Answered once the plane has applied the cancel, as the freeze and
                // the copy are, so the state read after the answer is in-dataset.
                await AppliedAsync(move, cancel.Position, cancellationToken).ConfigureAwait(false);

                return Refused(ProblemType.EventDeclarationInvalid, "The move was cancelled: the catalogue does not translate one-to-one into the dataset's declarations (ADR 0167). " + problem.Detail + ".", members =>
                {
                    members.String("declaration", Encoding.UTF8.GetString(problem.Declaration.Lexical));
                    members.String("reason", problem.Reason);
                });
            }

            // The switch.
            List<Triple> moved = current.FindAll(t => !t.Predicate.Equals(ManagementVocabulary.EventDeclarationsProperty));
            moved.Add(new Triple(node, ManagementVocabulary.EventDeclarationsProperty, ManagementVocabulary.MovedState));
            moved.AddRange(nodes.Select(n => new Triple(n.Subject, n.Predicate, n.Object)));
            Written copy = await WriteAsync(move, graph, moved, version, cancellationToken).ConfigureAwait(false);

            if (copy.Stale)
            {
                continue;
            }

            if (copy.Problem is { } refused)
            {
                return new Outcome(0, 0, refused);
            }

            await AppliedAsync(move, copy.Position, cancellationToken).ConfigureAwait(false);
            return new Outcome(copy.Position, nodes.Count(n => n.Predicate.Equals(RdfTypeTerm) && n.Object.Equals(EventTypeTerm)), null);
        }

        return Refused(ProblemType.Conflict, "The dataset's declarations kept changing while the move ran; send it again.");
    }

    // One probe through the dataset's sequencer, authorised at `applied`:
    // the order check raises the dataset's watermark to it (ADR 0165), then
    // the probe refuses. False when the dataset records a later position, so
    // that the move reads the declarations again.
    private static async Task<bool> BarrierAsync(Move move, Dataset dataset, long applied, CancellationToken cancellationToken)
    {
        List<ICommitValidator> validators = [];

        if (move.Options.Management is { } management)
        {
            validators.Add(management.OrderOf(move.Name, new ManagementPosition(applied)));
        }

        validators.Add(new Probe());
        CommitRequestBuilder probe = new CommitRequestBuilder { Validators = validators }.Assert(Barrier, Barrier, Barrier, Barrier);
        CommitResult result = await dataset.CommitAsync(probe, cancellationToken).ConfigureAwait(false);

        // Refused by the probe: every commit staged before it is in the log,
        // and none after it can change a catalogue graph.
        return result.Outcome == CommitOutcome.Rejected && result.Report.Count == 1 && result.Report[0].Equals(Barrier);
    }

    private sealed class Probe : ICommitValidator
    {
        public ValidationVerdict Validate(IQuadSource proposed, QuadDelta delta) => ValidationVerdict.Reject([Barrier]);
    }

    // Waits, by advancing, until this instance applies `position`, so that
    // the next step reads what the write committed.
    private static async Task AppliedAsync(Move move, long position, CancellationToken cancellationToken)
    {
        for (int i = 0; i < Rounds && move.Plane.System.Head.Value < position; i++)
        {
            if (move.Options.Management is { } management)
            {
                await management.AdvanceAsync(cancellationToken).ConfigureAwait(false);
            }

            if (move.Plane.System.Head.Value < position)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 << i), move.Options.Clock, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // ---- writes to the system dataset ----

    private sealed record Written(long Position, bool Stale, Func<HttpContext, Task>? Problem);

    // Replaces the dataset's graph with `desired` under `version`: locally
    // when this instance writes the system dataset, or at the system writer.
    private static async Task<Written> WriteAsync(Move move, RdfTerm graph, List<Triple> desired, Position version, CancellationToken cancellationToken)
    {
        Dataset system = move.Plane.System;

        if (!system.IsFollower && move.Plane.Status.Role == ManagementRole.Writer)
        {
            return await WriteLocallyAsync(move, system, graph, desired, version, cancellationToken).ConfigureAwait(false);
        }

        if (move.Context is not { } context || move.Options.Writers is not { } directory || move.Options.Forwarder is not { } forwarder)
        {
            return new Written(0, false, c => HttpProblems.WriteAsync(c, ProblemType.Unavailable, "Only the system writer finishes a move with no caller.", m => m.String("reason", "no-writer")));
        }

        WriterRoute route = await directory.RouteAsync(WriterTarget.System, cancellationToken).ConfigureAwait(false);

        if (route.Kind != WriterRouteKind.Remote || route.Address is not { } address)
        {
            return new Written(0, false, c => HttpProblems.WriteAsync(c, ProblemType.Unavailable, "No instance holds the system dataset's writer epoch now.", m => m.String("reason", "no-writer")));
        }

        InMemoryDatasetBuilder builder = new();

        foreach (Triple triple in desired)
        {
            builder.Add(triple.Subject, triple.Predicate, triple.Object, graph);
        }

        string url = address.TrimEnd('/') + context.Request.PathBase.ToUriComponent() + "/manage/datasets/" + move.Name.Value;
        using HttpRequestMessage message = new(HttpMethod.Put, url);
        if (context.Request.Headers.Authorization.ToString() is { Length: > 0 } authorization)
        {
            message.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        message.Headers.TryAddWithoutValidation("If-Match", "\"" + version.Value.ToString(CultureInfo.InvariantCulture) + "\"");
        message.Headers.TryAddWithoutValidation(Forwarding.HopHeader, directory.Instance.Value);

        if (context.Request.Headers.TryGetValue("traceparent", out Microsoft.Extensions.Primitives.StringValues trace))
        {
            message.Headers.TryAddWithoutValidation("traceparent", trace.ToString());
        }

        message.Content = new ByteArrayContent(ManagementTrig.Write(builder.ToDataset(), [graph]));
        message.Content.Headers.TryAddWithoutValidation("Content-Type", "application/trig; charset=utf-8");

        HttpResponseMessage response;

        try
        {
            response = await forwarder.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException unreachable)
        {
            directory.Stale(WriterTarget.System);
            return new Written(0, false, c => HttpProblems.WriteAsync(c, ProblemType.Unavailable, "The system writer at " + address + " could not be reached: " + unreachable.Message,
                m => m.String("reason", "writer-unreachable")));
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return new Written(0, true, null);
            }

            if (response.IsSuccessStatusCode)
            {
                long position = response.Headers.TryGetValues(Preconditions.PositionHeader, out IEnumerable<string>? values)
                    && long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out long p) ? p : 0;
                return new Written(position, false, null);
            }

            // The system writer's answer, passed back as it came.
            int status = (int)response.StatusCode;
            string? type = response.Content.Headers.ContentType?.ToString();
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                directory.Stale(WriterTarget.System);
            }

            return new Written(0, false, async c =>
            {
                c.Response.StatusCode = status;
                c.Response.ContentType = type;
                await c.Response.Body.WriteAsync(body, c.RequestAborted).ConfigureAwait(false);
            });
        }
    }

    private static async Task<Written> WriteLocallyAsync(Move move, Dataset system, RdfTerm graph, List<Triple> desired, Position version, CancellationToken cancellationToken)
    {
        if (system.GraphVersion(RequestTerm.FromTerm(graph)) != version)
        {
            return new Written(0, true, null);
        }

        CommitMetadata metadata;

        if (move.Context is { } context)
        {
            metadata = Writes.Metadata(Exchange.ForSystem(context, move.Options, system));
        }
        else
        {
            // A move finished with no request is the act of the agent who
            // authorised the freeze, as the freeze commit's header names them,
            // never of the instance (ADR 0167 point 3).
            Freeze? freeze = await FreezeOfAsync(system, graph, cancellationToken).ConfigureAwait(false);
            metadata = new CommitMetadata
            {
                Agent = freeze?.Agent is { } agent ? RequestTerm.FromTerm(agent) : default,
                Cause = RdfTerm.Literal(Encoding.UTF8.GetBytes("varve: the move of " + move.Name.Value + "'s event declarations frozen at " + (freeze?.Since ?? 0).ToString(CultureInfo.InvariantCulture) + ", resumed (ADR 0167)")),
            };
        }

        CommitRequestBuilder request = new()
        {
            ExpectedPosition = version,
            ExpectedGraphs = [RequestTerm.FromTerm(graph)],
            Metadata = metadata,
            Validators = [move.Plane.Validator],
        };

        using (DatasetView view = system.Pin())
        {
            HashSet<Triple> wanted = [.. desired];

            foreach (Triple current in Triples(view, graph))
            {
                if (!wanted.Remove(current))
                {
                    request = request.Retract(current.Subject, current.Predicate, current.Object, graph);
                }
            }

            foreach (Triple triple in wanted)
            {
                request = request.Assert(triple.Subject, triple.Predicate, triple.Object, graph);
            }
        }

        CommitResult result = await system.CommitAsync(request, cancellationToken).ConfigureAwait(false);

        switch (result.Outcome)
        {
            case CommitOutcome.Committed:
                if (move.Options.Management is { } management)
                {
                    await management.AdvanceAsync(cancellationToken).ConfigureAwait(false);
                }

                return new Written(result.Position.Value, false, null);
            case CommitOutcome.NoChange:
                return new Written(result.Position.Value, false, null);
            case CommitOutcome.Conflict:
                return new Written(0, true, null);
            case CommitOutcome.Rejected:
                IReadOnlyList<RdfTerm> report = result.Report;
                return new Written(0, false, c => ValidatorProblem.TryWriteAsync(c, report) ?? HttpProblems.WriteAsync(c, ProblemType.Rejected, "The move's management write was refused."));
            default:
                string reason = result.Reason ?? "unavailable";
                return new Written(0, false, c => HttpProblems.WriteAsync(c, ProblemType.Unavailable, "The system dataset cannot take the move's write now: " + reason + ".", m => m.String("reason", "unavailable")));
        }
    }

    // ---- the freeze ----

    /// <summary>The freeze in force: the system position m₁ that froze the catalogue, and the agent its commit names, when it names one.</summary>
    internal sealed record Freeze(long Since, RdfTerm? Agent);

    /// <summary>
    /// The freeze of the dataset whose graph is <paramref name="graph"/>, when
    /// its declarations are <c>varve:Moving</c>: the system commit that made
    /// them so, read from the log, so that neither its position nor its agent
    /// is a value a declaration states.
    /// </summary>
    internal static async Task<Freeze?> FreezeOfAsync(Dataset system, RdfTerm graph, CancellationToken cancellationToken)
    {
        Freeze? freeze = null;
        HashSet<(RdfTerm, RdfTerm)> markers = [];
        long head = system.Head.Value;

        if (head == 0)
        {
            return null;
        }

        // A subscription tails the log: it is read to the head as it is now, and no further.
        await foreach (Commit commit in system.Subscribe(new Position(0), SubscriptionFilter.All, cancellationToken).ConfigureAwait(false))
        {
            if (commit.Position.Value > head)
            {
                break;
            }

            if (commit.Kind != CommitKind.Data)
            {
                if (commit.Position.Value >= head)
                {
                    break;
                }

                continue;
            }

            bool was = markers.Contains((graph, ManagementVocabulary.MovingState));
            Mark(commit, commit.Delta.Retracted, graph, markers, assert: false);
            Mark(commit, commit.Delta.Asserted, graph, markers, assert: true);
            bool now = markers.Contains((graph, ManagementVocabulary.MovingState));

            if (now && !was)
            {
                freeze = new Freeze(commit.Position.Value, !commit.Agent.IsNone && commit.TryExternalise(commit.Agent, out RdfTerm? agent) ? agent : null);
            }
            else if (!now)
            {
                freeze = null;
            }

            if (commit.Position.Value >= head)
            {
                break;
            }
        }

        return freeze;
    }

    // The values of varve:eventDeclarations in the dataset's graph, as (graph, value).
    private static void Mark(Commit commit, ReadOnlySpan<Quad> quads, RdfTerm graph, HashSet<(RdfTerm, RdfTerm)> markers, bool assert)
    {
        foreach (Quad quad in quads)
        {
            if (!quad.IsDefaultGraph
                && commit.TryExternalise(quad.Predicate, out RdfTerm? p) && p.Equals(ManagementVocabulary.EventDeclarationsProperty)
                && commit.TryExternalise(quad.Graph, out RdfTerm? g) && g.Equals(graph)
                && commit.TryExternalise(quad.Object, out RdfTerm? o))
            {
                _ = assert ? markers.Add((graph, o)) : markers.Remove((graph, o));
            }
        }
    }

    // ---- reading ----

    internal readonly record struct Triple(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object);

    private static readonly RdfTerm RdfTypeTerm = Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type");
    private static readonly RdfTerm EventTypeTerm = Iri(ManagementVocabulary.Namespace + "EventType");
    private static readonly RdfTerm DatasetTerm = Iri(ManagementVocabulary.Namespace + "Dataset");

    private static List<Triple> Triples(IQuadSource source, RdfTerm graph)
    {
        List<Triple> triples = [];

        if (!source.TryInternalise(graph, out TermHandle handle))
        {
            return triples;
        }

        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(handle));

        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;

            if (source.TryExternalise(quad.Subject, out RdfTerm? s) && source.TryExternalise(quad.Predicate, out RdfTerm? p) && source.TryExternalise(quad.Object, out RdfTerm? o))
            {
                triples.Add(new Triple(s, p, o));
            }
        }

        return triples;
    }

    // Every quad of the catalogue graphs, as assertions.
    private static List<FeedChange> Catalogue(IQuadSource source)
    {
        List<FeedChange> changes = [];

        foreach (RdfTerm graph in EventDeclarations.CatalogueGraphs(source))
        {
            foreach (Triple triple in Triples(source, graph))
            {
                changes.Add(new FeedChange(FeedChangeKind.Assert, triple.Subject, triple.Predicate, triple.Object, graph));
            }
        }

        return changes;
    }

    // The event nodes of a dataset's graph: their triples, as the catalogue form takes them.
    private static List<(RdfTerm, RdfTerm, RdfTerm)> EventNodes(List<Triple> graph)
    {
        List<(RdfTerm, RdfTerm, RdfTerm)> nodes = [];
        List<FeedChange> catalogue = ManagedEventDeclarations.ToCatalogue(graph.Select(t => (t.Subject, t.Predicate, t.Object)), []);
        HashSet<RdfTerm> subjects = [.. catalogue.Select(c => c.Subject)];

        foreach (Triple triple in graph)
        {
            if (subjects.Contains(triple.Subject))
            {
                nodes.Add((triple.Subject, triple.Predicate, triple.Object));
            }
        }

        return nodes;
    }

    private static RdfTerm? DatasetNode(List<Triple> graph) =>
        graph.Find(t => t.Predicate.Equals(RdfTypeTerm) && t.Object.Equals(DatasetTerm)).Subject;

    private static EventDeclarationState StateOf(List<Triple> graph, RdfTerm node) =>
        graph.Find(t => t.Subject.Equals(node) && t.Predicate.Equals(ManagementVocabulary.EventDeclarationsProperty)).Object switch
        {
            { } value when value.Equals(ManagementVocabulary.MovingState) => EventDeclarationState.Moving,
            { } value when value.Equals(ManagementVocabulary.MovedState) => EventDeclarationState.Moved,
            _ => EventDeclarationState.InDataset,
        };

    private static string State(EventDeclarationState state) => state switch
    {
        EventDeclarationState.Moving => "moving",
        EventDeclarationState.Moved => "moved",
        _ => "in-dataset",
    };

    private static DatasetName? Name(HttpContext context) =>
        context.Request.RouteValues.TryGetValue(Exchange.DatasetRouteValue, out object? value) && DatasetName.TryParse(value as string, out DatasetName name) ? name : null;

    private static async Task WriteJsonAsync(HttpContext context, int status, string state, long position, int types, long? since = null)
    {
        System.Buffers.ArrayBufferWriter<byte> body = new(128);

        using (System.Text.Json.Utf8JsonWriter json = new(body))
        {
            json.WriteStartObject();
            json.WriteString("state", state);

            if (since is long frozen)
            {
                json.WriteNumber("since", frozen);
            }

            json.WriteNumber("position", position);
            json.WriteNumber("types", types);
            json.WriteEndObject();
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = MediaTypes.Json;
        context.Response.ContentLength = body.WrittenCount;

        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await context.Response.Body.WriteAsync(body.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
        }
    }

    private static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));
}
