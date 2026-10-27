// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Varve.Management;
using Varve.Management.Model;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// The management plane's resources (ADR 0163) over the fleet's system
/// dataset. A declaration is TriG: a dataset's graph, the fleet graph, or a
/// set of them under <c>apply</c>. A write goes to the system writer (ADR
/// 0168), is checked by the code validator (ADR 0166), and is one commit
/// replacing the graphs it owns under their versions (ADR 0125).
/// </summary>
internal static class ManagementEndpoints
{
    /// <summary>The header that lists management graphs with their versions, as <c>Varve-If-Graphs-Match</c> names them.</summary>
    internal const string GraphVersionsHeader = "Varve-Graph-Versions";

    private const string Trig = "application/trig";

    // ---- the list, the system ----

    /// <summary><c>GET /manage/datasets</c>: the datasets the caller administers, with this instance's role in each.</summary>
    internal static Task ListAsync(HttpContext context, ProtocolOptions options) => AdminEndpoints.ListAsync(context, options);

    /// <summary><c>GET /manage/system</c>: the system dataset's role, epoch, positions, bootstrap state and writer.</summary>
    internal static async Task SystemAsync(HttpContext context, ProtocolOptions options, IManagementPlane plane)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "GET").ConfigureAwait(false);
            return;
        }

        if (!await Exchange.AuthorizeServerAsync(context, options, DatasetPermissions.ServerAdmin).ConfigureAwait(false))
        {
            return;
        }

        ManagementStatus status = plane.Status;
        context.Response.Headers.CacheControl = "no-store";
        await WriteJsonAsync(context, StatusCodes.Status200OK, json =>
        {
            json.WriteString("role", status.Role switch { ManagementRole.Writer => "writer", ManagementRole.Follower => "follower", _ => "superseded" });
            json.WriteNumber("epoch", status.Epoch);
            json.WriteNumber("position", status.Applied.Value);
            json.WriteNumber("tail", status.Tail.Value);
            json.WriteNumber("lag", status.Lag);
            json.WriteString("bootstrap", status.Bootstrap switch { BootstrapState.Recorded => "recorded", BootstrapState.Absent => "absent", _ => "mismatch" });

            if (status.WriterAddress is { } address)
            {
                json.WriteString("writer", address);
            }
        }).ConfigureAwait(false);
    }

    // ---- graphs as resources ----

    /// <summary><c>/manage/fleet</c>: the fleet graph, read, replaced or merged as TriG, by an operator.</summary>
    internal static Task FleetAsync(HttpContext context, ProtocolOptions options, IManagementPlane plane) =>
        GraphAsync(context, options, plane, ManagementVocabulary.FleetGraph, null, settingsOnly: false);

    /// <summary>
    /// <c>/manage/datasets/{name}</c>: the dataset's graph, read, replaced or
    /// merged as TriG by its admin; <c>DELETE</c> removes a dataset declared
    /// closed, which its writer then deletes.
    /// </summary>
    internal static async Task DatasetAsync(HttpContext context, ProtocolOptions options, IManagementPlane plane)
    {
        if (DatasetOf(context) is not { } name)
        {
            await HttpProblems.BadRequest(context, "A dataset name is 1 to 63 characters of [A-Za-z0-9._-], starting with a letter or a digit.").ConfigureAwait(false);
            return;
        }

        if (HttpMethods.IsDelete(context.Request.Method))
        {
            await DeleteAsync(context, options, plane, name).ConfigureAwait(false);
            return;
        }

        await GraphAsync(context, options, plane, ManagementVocabulary.DatasetGraph(name.Value), name, settingsOnly: false).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>/manage/datasets/{name}/settings</c>: <c>GET</c> answers the declared
    /// settings as JSON with the graph's version as <c>ETag</c>; <c>PUT</c> and
    /// <c>PATCH</c> take TriG stating the dataset node's settings, which
    /// replace those it states (ADR 0163 point 10).
    /// </summary>
    internal static async Task SettingsAsync(HttpContext context, ProtocolOptions options, IManagementPlane plane)
    {
        if (DatasetOf(context) is not { } name)
        {
            await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
            return;
        }

        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        {
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

            DescribeGraph(context, plane.System, ManagementVocabulary.DatasetGraph(name.Value));
            await WriteJsonAsync(context, StatusCodes.Status200OK, json =>
                json.WriteString("defaultAccessScope", (declared.DefaultAccessScope ?? HistoryScope.AllHistory).ToString())).ConfigureAwait(false);
            return;
        }

        await GraphAsync(context, options, plane, ManagementVocabulary.DatasetGraph(name.Value), name, settingsOnly: true).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>GET</c> and <c>PUT /manage/datasets/{name}/state</c>: this instance's
    /// state and role in the dataset, and for an object-store dataset its
    /// epoch, warm, writer set and epoch history (ADR 0180 point 7);
    /// <c>{"state": "open" | "closed"}</c>
    /// declares it, and every instance converges to it.
    /// <c>{"takeover": {"epoch": n}}</c> asks this instance, a warm standby, to
    /// take the dataset over from epoch <c>n</c> (ADR 0179 point 6); the
    /// system dataset's path is <c>_fleet</c>.
    /// </summary>
    internal static async Task StateAsync(HttpContext context, ProtocolOptions options, IManagementPlane plane)
    {
        bool get = HttpMethods.IsGet(context.Request.Method);

        if (!get && !HttpMethods.IsPut(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "GET, PUT").ConfigureAwait(false);
            return;
        }

        // A takeover is the store's, answered by the instance that receives it
        // (ADR 0179 point 6); anything else PUT here is the system writer's.
        Body? put = null;

        if (!get)
        {
            put = await RequestBodies.ReadAsync(context, options.Limits).ConfigureAwait(false);

            if (put.Value.TooLarge)
            {
                await HttpProblems.RequestTooLarge(context, options.Limits.MaxRequestBody.Value).ConfigureAwait(false);
                return;
            }

            if (ReadTakeover(put.Value.Bytes) is long from)
            {
                await TakeoverAsync(context, options, plane, from).ConfigureAwait(false);
                return;
            }
        }

        if (DatasetOf(context) is not { } name)
        {
            await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
            return;
        }

        if (!get && await Forwarding.RouteAsync(context, options, WriterTarget.System, put!.Value.Bytes ?? []).ConfigureAwait(false))
        {
            return;
        }

        if (!await Exchange.AuthorizeDatasetAsync(context, options, DatasetPermissions.Admin, name).ConfigureAwait(false))
        {
            return;
        }

        if (get)
        {
            DatasetEntry? entry = null;

            foreach (DatasetEntry candidate in options.Administration?.List() ?? [])
            {
                entry = candidate.Name == name ? candidate : entry;
            }

            if (entry is null)
            {
                await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
                return;
            }

            System.Action<System.Text.Json.Utf8JsonWriter>? handover = await plane.DescribeHandoverAsync(name, context.RequestAborted).ConfigureAwait(false);
            context.Response.Headers.CacheControl = "no-store";
            await WriteJsonAsync(context, StatusCodes.Status200OK, json =>
            {
                json.WriteString("state", AdminEndpoints.StateName(entry.State));
                json.WriteString("role", entry.Role switch { DatasetRole.Writer => "writer", DatasetRole.Follower => "follower", DatasetRole.Standby => "standby", _ => "elsewhere" });

                if (entry.Reason is not null)
                {
                    json.WriteString("reason", entry.Reason);
                }

                handover?.Invoke(json);
            }).ConfigureAwait(false);
            return;
        }

        Body body = put!.Value;

        switch (ReadState(body.Bytes))
        {
            case StateRequest.Invalid:
                await HttpProblems.BadRequest(context, "The body is {\"state\": \"open\"} or {\"state\": \"closed\"}, or a takeover naming an epoch.").ConfigureAwait(false);
                return;
        }

        Dataset system = plane.System;
        RdfTerm graph = ManagementVocabulary.DatasetGraph(name.Value);

        if (!plane.Table.Declarations.Datasets.TryGetValue(name.Value, out DatasetDeclaration? declared))
        {
            await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
            return;
        }

        bool closed = ReadState(body.Bytes) == StateRequest.Closed;
        Dictionary<RdfTerm, List<Triple>> desired = new() { [graph] = Merge(Current(system, graph), [new Triple(declared.Node, Term("closed"), Boolean(closed))]) };
        await CommitAsync(context, options, plane, desired, null, StatusCodes.Status204NoContent).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>GET /manage/datasets/{name}/grants</c>: the grants on the dataset as
    /// JSON, at the system position <c>Varve-As-Of</c> names, or the applied
    /// one: who could read which graphs, then.
    /// </summary>
    internal static async Task GrantsAsync(HttpContext context, ProtocolOptions options, IManagementPlane plane)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "GET").ConfigureAwait(false);
            return;
        }

        if (DatasetOf(context) is not { } name)
        {
            await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
            return;
        }

        if (!await Exchange.AuthorizeDatasetAsync(context, options, DatasetPermissions.Admin, name).ConfigureAwait(false))
        {
            return;
        }

        Dataset system = plane.System;

        if (await Reads.ResolveAsync(context, system).ConfigureAwait(false) is not Position at)
        {
            return;
        }

        if (await Reads.OpenAsync(context, system, at, Reads.IsAsOf(context), options.Limits, context.RequestAborted).ConfigureAwait(false) is not { } view)
        {
            return;
        }

        Declarations declarations = DeclarationReader.Read(view);

        if (!declarations.Datasets.TryGetValue(name.Value, out DatasetDeclaration? dataset))
        {
            await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(context, StatusCodes.Status200OK, json =>
        {
            json.WriteNumber("position", at.Value);
            json.WriteStartArray("grants");

            foreach (GrantDeclaration grant in dataset.Grants)
            {
                WriteGrant(json, grant);
            }

            foreach (GrantDeclaration grant in declarations.FleetGrants)
            {
                WriteGrant(json, grant);
            }

            json.WriteEndArray();
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>GET /manage/graphs</c>: every management graph as TriG, with the
    /// system position as <c>Varve-Position</c> and each graph's version in
    /// <c>Varve-Graph-Versions</c>, which <c>varve apply --plan</c> diffs against.
    /// </summary>
    internal static async Task GraphsAsync(HttpContext context, ProtocolOptions options, IManagementPlane plane)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "GET").ConfigureAwait(false);
            return;
        }

        if (!await Exchange.AuthorizeServerAsync(context, options, DatasetPermissions.ServerAdmin).ConfigureAwait(false))
        {
            return;
        }

        Dataset system = plane.System;
        using DatasetView view = system.Pin();
        IReadOnlyList<RdfTerm> graphs = ManagementPlan.NamedGraphs(view);
        StringBuilder versions = new();

        foreach (RdfTerm graph in graphs)
        {
            versions.Append(versions.Length == 0 ? string.Empty : ", ").Append('<').Append(Encoding.UTF8.GetString(graph.Lexical)).Append(">=")
                .Append(system.GraphVersion(RequestTerm.FromTerm(graph)).Value.ToString(CultureInfo.InvariantCulture));
        }

        Preconditions.Describe(context.Response, view.Position);
        context.Response.Headers[GraphVersionsHeader] = versions.ToString();
        context.Response.Headers.CacheControl = "no-cache";
        await WriteTrigAsync(context, ManagementTrig.Write(view, graphs)).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>POST /manage/apply</c>: TriG of the graphs <c>Varve-If-Graphs-Match</c>
    /// names, each at the version a plan read, committed as one commit that
    /// replaces each named graph with what the body holds of it (ADR 0163
    /// point 5). A named graph the body holds nothing of is emptied; a quad in
    /// a graph the header does not name is refused.
    /// </summary>
    internal static async Task ApplyAsync(HttpContext context, ProtocolOptions options, IManagementPlane plane)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "POST").ConfigureAwait(false);
            return;
        }

        if (await Forwarding.RouteAsync(context, options, WriterTarget.System).ConfigureAwait(false))
        {
            return;
        }

        if (!Preconditions.TryReadGraphs(context.Request.Headers[Preconditions.IfGraphsMatchHeader], out List<GraphPrecondition>? owned) || owned is null || owned.Exists(static graph => graph.Graph is null))
        {
            await HttpProblems.BadRequest(context, "An apply names every graph it owns in Varve-If-Graphs-Match, each a management graph at the version its plan read.").ConfigureAwait(false);
            return;
        }

        if (!await AuthorizeOwnedAsync(context, options, owned).ConfigureAwait(false))
        {
            return;
        }

        if (await ReadTrigAsync(context, options) is not { } body)
        {
            return;
        }

        Dictionary<RdfTerm, List<Triple>> desired = [];

        foreach (GraphPrecondition graph in owned)
        {
            desired[graph.Graph!] = [];
        }

        foreach ((RdfTerm? graph, Triple triple) in Quads(body))
        {
            if (graph is null || !desired.TryGetValue(graph, out List<Triple>? triples))
            {
                await HttpProblems.BadRequest(context, "A quad is in " + (graph is null ? "the default graph" : "<" + Encoding.UTF8.GetString(graph.Lexical) + ">") + ", which Varve-If-Graphs-Match does not name.").ConfigureAwait(false);
                return;
            }

            triples.Add(triple);
        }

        await CommitAsync(context, options, plane, desired, owned, StatusCodes.Status204NoContent).ConfigureAwait(false);
    }

    /// <summary><c>POST /manage/datasets/{name}/checkpoints</c>: the dataset's writer takes the checkpoint (ADR 0168).</summary>
    internal static async Task CheckpointAsync(HttpContext context, ProtocolOptions options)
    {
        if (HttpMethods.IsPost(context.Request.Method) && Exchange.RouteTarget(context) is { } target && await Forwarding.RouteAsync(context, options, target).ConfigureAwait(false))
        {
            return;
        }

        await AdminEndpoints.CheckpointAsync(context, options).ConfigureAwait(false);
    }

    // ---- the core ----

    // GET, PUT or PATCH of one management graph.
    private static async Task GraphAsync(HttpContext context, ProtocolOptions options, IManagementPlane plane, RdfTerm graph, DatasetName? dataset, bool settingsOnly)
    {
        string method = context.Request.Method;
        bool get = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
        bool patch = HttpMethods.IsPatch(method);

        if (!get && !patch && !HttpMethods.IsPut(method))
        {
            await HttpProblems.MethodNotAllowed(context, dataset is null ? "GET, HEAD, PUT, PATCH" : "GET, HEAD, PUT, PATCH, DELETE").ConfigureAwait(false);
            return;
        }

        if (!get && await Forwarding.RouteAsync(context, options, WriterTarget.System).ConfigureAwait(false))
        {
            return;
        }

        bool allowed = dataset is { } name
            ? await Exchange.AuthorizeDatasetAsync(context, options, DatasetPermissions.Admin, name).ConfigureAwait(false)
            : await Exchange.AuthorizeServerAsync(context, options, DatasetPermissions.ServerAdmin).ConfigureAwait(false);

        if (!allowed)
        {
            return;
        }

        Dataset system = plane.System;

        if (get)
        {
            using DatasetView view = system.Pin();
            List<Triple> current = Current(view, graph);

            if (dataset is not null && current.Count == 0)
            {
                await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
                return;
            }

            DescribeGraph(context, system, graph);
            await WriteTrigAsync(context, HttpMethods.IsHead(method) ? [] : ManagementTrig.Write(view, [graph])).ConfigureAwait(false);
            return;
        }

        if (!TryExpected(context, system, graph, out List<GraphPrecondition>? expected))
        {
            await HttpProblems.BadRequest(context, "If-Match names the graph's version, as its ETag does.").ConfigureAwait(false);
            return;
        }

        if (await ReadTrigAsync(context, options) is not { } body)
        {
            return;
        }

        List<Triple> stated = [];

        foreach ((RdfTerm? quadGraph, Triple triple) in Quads(body))
        {
            if (quadGraph is not null && !quadGraph.Equals(graph))
            {
                await HttpProblems.BadRequest(context, "The body states <" + Encoding.UTF8.GetString(quadGraph.Lexical) + ">; this resource is <" + Encoding.UTF8.GetString(graph.Lexical) + ">, given as that graph or as the default graph.").ConfigureAwait(false);
                return;
            }

            if (settingsOnly && !triple.Predicate.Equals(Term("defaultAccessScope")))
            {
                await HttpProblems.BadRequest(context, "The settings resource takes the dataset's varve:defaultAccessScope alone; declare anything else in the dataset's graph.").ConfigureAwait(false);
                return;
            }

            stated.Add(triple);
        }

        List<Triple> desired = patch || settingsOnly ? Merge(Current(system, graph), stated) : stated;
        int created = Current(system, graph).Count == 0 ? StatusCodes.Status201Created : StatusCodes.Status204NoContent;
        await CommitAsync(context, options, plane, new Dictionary<RdfTerm, List<Triple>> { [graph] = desired }, expected, created).ConfigureAwait(false);
    }

    private static async Task DeleteAsync(HttpContext context, ProtocolOptions options, IManagementPlane plane, DatasetName name)
    {
        if (await Forwarding.RouteAsync(context, options, WriterTarget.System).ConfigureAwait(false))
        {
            return;
        }

        if (!await Exchange.AuthorizeDatasetAsync(context, options, DatasetPermissions.Admin, name).ConfigureAwait(false))
        {
            return;
        }

        if (!plane.Table.Declarations.Datasets.TryGetValue(name.Value, out DatasetDeclaration? declared))
        {
            await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
            return;
        }

        if (!declared.Closed)
        {
            await HttpProblems.WriteAsync(context, ProblemType.DatasetOpen, "Declare it closed first, with PUT /manage/datasets/" + name.Value + "/state.").ConfigureAwait(false);
            return;
        }

        RdfTerm graph = ManagementVocabulary.DatasetGraph(name.Value);

        if (!TryExpected(context, plane.System, graph, out List<GraphPrecondition>? expected))
        {
            await HttpProblems.BadRequest(context, "If-Match names the graph's version, as its ETag does.").ConfigureAwait(false);
            return;
        }

        await CommitAsync(context, options, plane, new Dictionary<RdfTerm, List<Triple>> { [graph] = [] }, expected, StatusCodes.Status204NoContent).ConfigureAwait(false);
    }

    // One commit replacing each graph of `desired` with its triples, under
    // the versions expected or, with none, the versions read now.
    private static async Task CommitAsync(HttpContext context, ProtocolOptions options, IManagementPlane plane, Dictionary<RdfTerm, List<Triple>> desired, List<GraphPrecondition>? expected, int created)
    {
        Dataset system = plane.System;
        Exchange exchange = Exchange.ForSystem(context, options, system);

        if (system.IsFollower)
        {
            // The route said local, and the epoch moved since: the client retries.
            await HttpProblems.WriteAsync(context, ProblemType.WriterMoved, "This instance no longer writes the system dataset.", members =>
            {
                members.Number("epoch", plane.Status.Epoch);
                members.String("requestId", context.TraceIdentifier);
            }).ConfigureAwait(false);
            return;
        }

        List<(GraphPrecondition Graph, Position Version)> stale = [];
        long since = 0;

        foreach (GraphPrecondition graph in expected ?? [])
        {
            Position version = system.GraphVersion(RequestTerm.FromTerm(graph.Graph!));
            since = Math.Max(since, graph.Version.Value);

            if (version != graph.Version)
            {
                stale.Add((graph, version));
            }
        }

        if (stale.Count > 0)
        {
            Preconditions.Describe(context.Response, system.Head);
            await HttpProblems.WriteAsync(context, ProblemType.PreconditionFailed, "A graph is not at the version named: re-plan.", members =>
            {
                members.Number("position", system.Head.Value);
                members.Number("headPosition", system.Head.Value);
                Utf8JsonWriter json = members.Raw("graphs");
                json.WriteStartArray("graphs");

                foreach ((GraphPrecondition graph, Position version) in stale)
                {
                    json.WriteStartObject();
                    json.WriteString("graph", Encoding.UTF8.GetString(graph.Graph!.Lexical));
                    json.WriteNumber("version", version.Value);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }).ConfigureAwait(false);
            return;
        }

        // ADR 0169's R4: a shapes binding is checked against its dataset before the commit, above the store.
        if (plane.CheckBindings(Proposed(desired)) is { } refusal)
        {
            await (ValidatorProblem.TryWriteAsync(context, refusal) ?? HttpProblems.WriteAsync(context, ProblemType.Rejected, "A shapes binding cannot be applied.")).ConfigureAwait(false);
            return;
        }

        CommitRequestBuilder request;

        using (DatasetView view = system.Pin())
        {
            // With no versions named, the graphs as they are now: a write
            // after this pin to one of them conflicts (ADR 0125).
            request = new CommitRequestBuilder
            {
                ExpectedPosition = expected is null ? view.Position : new Position(since),
                ExpectedGraphs = [.. Graphs(desired.Keys)],
                Metadata = Writes.Metadata(exchange),
                Validators = [plane.Validator],
            };

            foreach ((RdfTerm graph, List<Triple> triples) in desired)
            {
                HashSet<Triple> wanted = [.. triples];

                foreach (Triple current in Current(view, graph))
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
        }

        if (await Writes.CommitAsync(exchange, request).ConfigureAwait(false) is not CommitResult result)
        {
            return;
        }

        if (result.Outcome == CommitOutcome.Committed && options.Management is { } management)
        {
            // The writer applies its own write before it answers: the next
            // request here is decided by it, and its datasets converge to it.
            await management.AdvanceAsync(context.RequestAborted).ConfigureAwait(false);
        }

        await Writes.AnswerAsync(exchange, result, result.Outcome == CommitOutcome.Committed ? created : StatusCodes.Status204NoContent, evidence: new ConflictEvidence(request.ExpectedPosition!.Value, request.ExpectedGraphs)).ConfigureAwait(false);
    }

    // ---- helpers ----

    /// <summary>The declarations the owned graphs would hold, read on their own: what a binding check needs.</summary>
    private static Declarations Proposed(Dictionary<RdfTerm, List<Triple>> desired)
    {
        InMemoryDatasetBuilder builder = new();
        foreach ((RdfTerm graph, List<Triple> triples) in desired)
        {
            foreach (Triple triple in triples)
            {
                builder.Add(triple.Subject, triple.Predicate, triple.Object, graph);
            }
        }

        return DeclarationReader.Read(builder.ToDataset());
    }

    /// <summary>
    /// <c>GET /manage/datasets/{name}/bindings/{binding}</c>: a shapes binding at
    /// the applied position, its shapes' classification where this instance
    /// holds the dataset (ADRs 0169, 0171).
    /// </summary>
    internal static async Task BindingAsync(HttpContext context, ProtocolOptions options, IManagementPlane plane)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "GET").ConfigureAwait(false);
            return;
        }

        if (DatasetOf(context) is not { } name)
        {
            await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
            return;
        }

        if (!await Exchange.AuthorizeDatasetAsync(context, options, DatasetPermissions.Admin, name).ConfigureAwait(false))
        {
            return;
        }

        string? encoded = context.Request.RouteValues[BindingRouteValue] as string;
        if (string.IsNullOrEmpty(encoded) || plane.DescribeBinding(name, RdfTerm.Iri(Encoding.UTF8.GetBytes(Uri.UnescapeDataString(encoded)))) is not { } binding)
        {
            await HttpProblems.WriteAsync(context, ProblemType.NotFound, "The dataset declares no shapes binding by that IRI.").ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(context, StatusCodes.Status200OK, json =>
        {
            BindingDeclaration declared = binding.Declaration;
            json.WriteString("binding", Encoding.UTF8.GetString(declared.Node.Lexical));
            json.WriteString("shapesGraph", Encoding.UTF8.GetString(declared.BoundGraph.Lexical));
            json.WriteString("validationMode", declared.Mode.ToString());
            json.WriteBoolean("allowFullEvaluation", declared.AllowFullEvaluation);
            json.WriteBoolean("subClassOfInShapesGraph", declared.SubClassOfInShapesGraph);
            json.WriteNumber("position", binding.Position.Value);
            if (binding.Classification is { } classification)
            {
                json.WriteString("maintainability", classification.Worst.ToString());
                json.WriteStartArray("shapes");
                foreach (ShapeClassification shape in classification.Shapes)
                {
                    json.WriteStartObject();
                    json.WriteString("shape", shape.Shape.Kind == RdfTermKind.Iri ? Encoding.UTF8.GetString(shape.Shape.Lexical) : "_:" + Encoding.UTF8.GetString(shape.Shape.Lexical));
                    json.WriteString("maintainability", shape.Maintainability.ToString());
                    json.WriteStartArray("reasons");
                    foreach (ClassificationReason reason in shape.Reasons)
                    {
                        json.WriteStartObject();
                        json.WriteString("construct", reason.Construct);
                        json.WriteString("section", reason.Section);
                        json.WriteEndObject();
                    }

                    json.WriteEndArray();
                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            if (binding.Problems.Count > 0)
            {
                json.WriteStartArray("problems");
                foreach (ShaclProblem problem in binding.Problems)
                {
                    json.WriteStringValue(problem.Message);
                }

                json.WriteEndArray();
            }
        }).ConfigureAwait(false);
    }

    /// <summary>The route value naming a binding, its IRI percent-encoded.</summary>
    internal const string BindingRouteValue = "binding";

    internal readonly record struct Triple(RdfTerm Subject, RdfTerm Predicate, RdfTerm Object);

    private enum StateRequest : byte
    {
        Invalid,
        Open,
        Closed,
    }

    /// <summary>The system dataset's name in a management path, which no dataset name can take (ADR 0179 point 13).</summary>
    private const string SystemDataset = "_fleet";

    // A takeover of the dataset the path names, the system dataset's
    // included (ADR 0179 points 6 and 13): authorised as an operator's act at
    // this instance's applied position, and answered here.
    private static async Task TakeoverAsync(HttpContext context, ProtocolOptions options, IManagementPlane plane, long from)
    {
        bool system = context.Request.RouteValues.TryGetValue(Exchange.DatasetRouteValue, out object? value) && value as string == SystemDataset;
        DatasetName? dataset = system ? null : DatasetOf(context);

        if (!system && dataset is null)
        {
            await HttpProblems.DatasetNotFound(context).ConfigureAwait(false);
            return;
        }

        if (!await Exchange.AuthorizeServerAsync(context, options, DatasetPermissions.ServerAdmin).ConfigureAwait(false))
        {
            return;
        }

        if (await plane.TakeoverAsync(dataset, new TakeoverEpoch(from), context.RequestAborted).ConfigureAwait(false) is not { } outcome)
        {
            await HttpProblems.WriteAsync(context, ProblemType.TakeoverRefused, "This instance has no object store, so the dataset has no writer epoch to take over.", members =>
            {
                members.String("reason", "handover-off");
                members.Number("epoch", 0);
            }).ConfigureAwait(false);
            return;
        }

        if (outcome.Refusal == TakeoverRefusal.None)
        {
            context.Response.Headers.CacheControl = "no-store";
            await WriteJsonAsync(context, StatusCodes.Status200OK, json => json.WriteNumber("epoch", outcome.Epoch.Value)).ConfigureAwait(false);
            return;
        }

        (string reason, string detail) = outcome.Refusal switch
        {
            TakeoverRefusal.HandoverOff => ("handover-off", "The dataset does not declare varve:automaticHandover true."),
            TakeoverRefusal.NotInWriterSet => ("not-in-writer-set", "This instance is neither the dataset's placedOn nor one of its standbyOn."),
            TakeoverRefusal.Cold => ("cold", "This instance is not a warm standby of the dataset; readiness says why."),
            _ => ("epoch-moved", "The writer record no longer shows the epoch named: the takeover is a no-op."),
        };

        await HttpProblems.WriteAsync(context, ProblemType.TakeoverRefused, detail, members =>
        {
            members.String("reason", reason);
            members.Number("epoch", outcome.Epoch.Value);
        }).ConfigureAwait(false);
    }

    // The epoch a takeover body names, {"takeover": {"epoch": n}}; none for any other body.
    private static long? ReadTakeover(byte[]? body)
    {
        if (body is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement is { ValueKind: JsonValueKind.Object } root
                && root.TryGetProperty("takeover", out JsonElement takeover) && takeover.ValueKind == JsonValueKind.Object
                && takeover.TryGetProperty("epoch", out JsonElement epoch) && epoch.TryGetInt64(out long value) && value >= 0
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static StateRequest ReadState(byte[]? body)
    {
        if (body is not { Length: > 0 })
        {
            return StateRequest.Invalid;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return StateRequest.Invalid;
            }

            return root.TryGetProperty("state", out JsonElement state) ? state.GetString() switch
            {
                "open" => StateRequest.Open,
                "closed" => StateRequest.Closed,
                _ => StateRequest.Invalid,
            } : StateRequest.Invalid;
        }
        catch (JsonException)
        {
            return StateRequest.Invalid;
        }
    }

    // An apply that names only dataset graphs needs admin of each; anything
    // else, an operator.
    private static async Task<bool> AuthorizeOwnedAsync(HttpContext context, ProtocolOptions options, List<GraphPrecondition> owned)
    {
        List<DatasetName> datasets = [];

        foreach (GraphPrecondition graph in owned)
        {
            if (!ManagementVocabulary.TryGetDataset(graph.Graph!, out string? name) || !DatasetName.TryParse(name, out DatasetName parsed))
            {
                return await Exchange.AuthorizeServerAsync(context, options, DatasetPermissions.ServerAdmin).ConfigureAwait(false);
            }

            datasets.Add(parsed);
        }

        if (await Exchange.MayAsync(context, options, DatasetPermissions.ServerAdmin, null).ConfigureAwait(false))
        {
            return true;
        }

        foreach (DatasetName name in datasets)
        {
            if (!await Exchange.AuthorizeDatasetAsync(context, options, DatasetPermissions.Admin, name).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    private static DatasetName? DatasetOf(HttpContext context) =>
        context.Request.RouteValues.TryGetValue(Exchange.DatasetRouteValue, out object? value) && DatasetName.TryParse(value as string, out DatasetName name) ? name : null;

    private static bool TryExpected(HttpContext context, Dataset system, RdfTerm graph, out List<GraphPrecondition>? expected)
    {
        expected = null;

        switch (Preconditions.Read(context.Request.Headers.IfMatch, out Preconditions.TagList tags))
        {
            case TagCondition.Absent:
            case TagCondition.Any:
                return true;
            case TagCondition.Positions when tags.Values is { Length: 1 } single:
                expected = [new GraphPrecondition(graph, new Position(single[0]))];
                return true;
            default:
                return false;
        }
    }

    private static void DescribeGraph(HttpContext context, Dataset system, RdfTerm graph)
    {
        Preconditions.Describe(context.Response, system.Head);
        Preconditions.DescribeGraph(context.Response, system.GraphVersion(RequestTerm.FromTerm(graph)));
        context.Response.Headers.CacheControl = "no-cache";
    }

    private static List<RequestTerm> Graphs(IEnumerable<RdfTerm> graphs)
    {
        List<RequestTerm> terms = [];

        foreach (RdfTerm graph in graphs)
        {
            terms.Add(RequestTerm.FromTerm(graph));
        }

        return terms;
    }

    private static List<Triple> Current(Dataset system, RdfTerm graph)
    {
        using DatasetView view = system.Pin();
        return Current(view, graph);
    }

    private static List<Triple> Current(DatasetView view, RdfTerm graph)
    {
        List<Triple> triples = [];

        if (!view.TryInternalise(graph, out TermHandle handle))
        {
            return triples;
        }

        using IQuadCursor cursor = view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(handle));

        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;

            if (view.TryExternalise(quad.Subject, out RdfTerm? s) && view.TryExternalise(quad.Predicate, out RdfTerm? p) && view.TryExternalise(quad.Object, out RdfTerm? o))
            {
                triples.Add(new Triple(s, p, o));
            }
        }

        return triples;
    }

    // PATCH: every (subject, predicate) the body states replaces those values
    // in the graph; everything else is kept.
    private static List<Triple> Merge(List<Triple> current, List<Triple> stated)
    {
        HashSet<(RdfTerm, RdfTerm)> replaced = [];

        foreach (Triple triple in stated)
        {
            replaced.Add((triple.Subject, triple.Predicate));
        }

        List<Triple> merged = current.FindAll(triple => !replaced.Contains((triple.Subject, triple.Predicate)));
        merged.AddRange(stated);
        return merged;
    }

    private static List<(RdfTerm? Graph, Triple Triple)> Quads(InMemoryDataset body)
    {
        List<(RdfTerm? Graph, Triple Triple)> quads = [];
        using IQuadCursor cursor = body.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);

        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;
            RdfTerm? graph = quad.Graph.IsNone ? null : body.TryExternalise(quad.Graph, out RdfTerm? named) ? named : null;

            if (body.TryExternalise(quad.Subject, out RdfTerm? s) && body.TryExternalise(quad.Predicate, out RdfTerm? p) && body.TryExternalise(quad.Object, out RdfTerm? o))
            {
                quads.Add((graph, new Triple(s, p, o)));
            }
        }

        return quads;
    }

    // A TriG body, read whole (ADR 0168: never parsed before it is complete),
    // or null after the problem is written.
    private static async Task<InMemoryDataset?> ReadTrigAsync(HttpContext context, ProtocolOptions options)
    {
        if (!MediaTypes.TryReadContentType(context.Request.ContentType, out string mediaType, out bool utf8) || !utf8 || mediaType != Trig)
        {
            await HttpProblems.UnsupportedMediaType(context, "A management declaration is application/trig in UTF-8.").ConfigureAwait(false);
            return null;
        }

        Body body = await RequestBodies.ReadAsync(context, options.Limits).ConfigureAwait(false);

        if (body.TooLarge)
        {
            await HttpProblems.RequestTooLarge(context, options.Limits.MaxRequestBody.Value).ConfigureAwait(false);
            return null;
        }

        HttpRequest request = context.Request;
        string address = string.Concat(request.Scheme, "://", request.Host.ToUriComponent(), request.PathBase.ToUriComponent(), request.Path.ToUriComponent());

        if (ManagementTrig.TryParse(body.Bytes ?? [], address, out string? error) is not { } parsed)
        {
            await HttpProblems.WriteAsync(context, ProblemType.RdfSyntax, error).ConfigureAwait(false);
            return null;
        }

        return parsed;
    }

    private static RdfTerm Term(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes(ManagementVocabulary.Namespace + local));

    private static RdfTerm Boolean(bool value) =>
        RdfTerm.Literal(value ? "true"u8 : "false"u8, RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#boolean"u8));

    private static void WriteGrant(Utf8JsonWriter json, GrantDeclaration grant)
    {
        json.WriteStartObject();
        json.WriteString("grant", Encoding.UTF8.GetString(grant.Node.Lexical));
        json.WriteString("permission", grant.Access switch { GrantAccess.Read => "read", GrantAccess.Write => "write", GrantAccess.Admin => "admin", _ => "operator" });
        json.WriteStartObject("grantee");
        json.WriteString("kind", grant.Grantee.Kind switch { PrincipalKind.Agent => "agent", PrincipalKind.Role => "role", _ => "group" });

        if (grant.Grantee.Kind == PrincipalKind.Group)
        {
            json.WriteString("claimType", grant.Grantee.ClaimType);
        }

        json.WriteString("value", grant.Grantee.Value);
        json.WriteEndObject();
        json.WriteStartObject("graphs");
        json.WriteBoolean("all", grant.Graphs.All);
        json.WriteBoolean("defaultGraph", grant.Graphs.DefaultGraph);
        json.WriteStartArray("graphs");

        foreach (RdfTerm graph in grant.Graphs.Graphs)
        {
            json.WriteStringValue(Encoding.UTF8.GetString(graph.Lexical));
        }

        json.WriteEndArray();
        json.WriteStartArray("prefixes");

        foreach (string prefix in grant.Graphs.Prefixes)
        {
            json.WriteStringValue(prefix);
        }

        json.WriteEndArray();
        json.WriteStartArray("templates");

        foreach (GraphTemplate template in grant.Graphs.Templates)
        {
            json.WriteStartObject();
            json.WriteString("template", template.Template);
            json.WriteString("match", template.Match == TemplateMatch.Prefix ? "prefix" : "exact");
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static async Task WriteTrigAsync(HttpContext context, byte[] trig)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = Trig + "; charset=utf-8";
        context.Response.ContentLength = trig.Length;
        await context.Response.Body.WriteAsync(trig, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteJsonAsync(HttpContext context, int status, Action<Utf8JsonWriter> write)
    {
        ArrayBufferWriter<byte> body = new(512);

        using (Utf8JsonWriter json = new(body))
        {
            json.WriteStartObject();
            write(json);
            json.WriteEndObject();
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = MediaTypes.Json;
        context.Response.ContentLength = body.WrittenCount;
        await context.Response.Body.WriteAsync(body.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
    }
}
