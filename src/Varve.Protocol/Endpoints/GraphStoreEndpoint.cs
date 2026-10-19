// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// The SPARQL 1.1 Graph Store HTTP Protocol (ADRs 0092, 0093, 0094): direct
/// identification by the request's own address, indirect by <c>?graph=</c>
/// and <c>?default</c>, and a <c>POST</c> to the store itself that makes a
/// new graph. A named graph exists when it holds a quad. Every write is one
/// commit.
/// </summary>
internal static class GraphStoreEndpoint
{
    internal const string PathValue = "path";

    internal static async Task HandleAsync(HttpContext context, ProtocolOptions options)
    {
        string method = context.Request.Method;
        bool read = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);

        if (!read && !HttpMethods.IsPut(method) && !HttpMethods.IsPost(method) && !HttpMethods.IsDelete(method))
        {
            await HttpProblems.MethodNotAllowed(context, "GET, HEAD, PUT, POST, DELETE").ConfigureAwait(false);
            return;
        }

        // A write to a dataset another instance writes goes there, before it
        // is authorised here (ADR 0168).
        if (!read && Exchange.RouteTarget(context) is { } target0 && await Forwarding.RouteAsync(context, options, target0).ConfigureAwait(false))
        {
            return;
        }

        if (await Exchange.BeginAsync(context, options, read ? DatasetPermissions.Read : DatasetPermissions.Write).ConfigureAwait(false) is not { } exchange)
        {
            return;
        }

        Tracing.Operation(method + " graph");
        Target target = await TargetOfAsync(exchange).ConfigureAwait(false);

        if (target.Kind == TargetKind.Invalid)
        {
            return;
        }

        if (read)
        {
            await GetAsync(exchange, target).ConfigureAwait(false);
            return;
        }

        if (target.Kind == TargetKind.Store && !HttpMethods.IsPost(method))
        {
            await HttpProblems.BadRequest(context, "Name a graph: ?graph=<IRI>, ?default, or the graph's own address.").ConfigureAwait(false);
            return;
        }

        if (HttpMethods.IsDelete(method))
        {
            if (await CheckAsync(exchange, target, put: false).ConfigureAwait(false) is { Proceed: true } deleting)
            {
                await DeleteAsync(exchange, target, deleting).ConfigureAwait(false);
            }

            return;
        }

        if (target.Kind == TargetKind.Store)
        {
            // §5.5: a POST to the store makes a new graph, named by the server.
            string minted = exchange.Address().TrimEnd('/') + "/" + context.TraceIdentifier.Replace(':', '-');
            target = new Target(TargetKind.Named, RdfTerm.Iri(Encoding.UTF8.GetBytes(minted)), minted);

            // A name the server just made reveals nothing: the one question
            // is whether the caller may write it (ADR 0107).
            if (!exchange.Scope.Writable.Allows(target.Graph))
            {
                await Writes.GraphNotWritableAsync(exchange, target.Graph).ConfigureAwait(false);
                return;
            }
        }
        else if (!await MayWriteAsync(exchange, target).ConfigureAwait(false))
        {
            return;
        }

        if (await CheckAsync(exchange, target, put: HttpMethods.IsPut(method)).ConfigureAwait(false) is not { Proceed: true } plan)
        {
            return;
        }

        List<BodyTriple>? triples = await ReadBodyAsync(exchange, target, allowMultipart: HttpMethods.IsPost(method)).ConfigureAwait(false);

        if (triples is null)
        {
            return;
        }

        await WriteAsync(exchange, target, plan, triples, replace: HttpMethods.IsPut(method)).ConfigureAwait(false);
    }

    // A write's preconditions, against the graph (ADR 0126): before the body
    // is read; whether the graph holds a quad is asked of the write's own pin.
    private static Task<WritePlan> CheckAsync(Exchange exchange, Target target, bool put) =>
        Writes.CheckGraphAsync(exchange, GraphOf(target), put);

    private static RdfTerm? GraphOf(Target target) => target.Kind == TargetKind.Default ? null : target.Graph;

    private static async Task GetAsync(Exchange exchange, Target target)
    {
        HttpContext context = exchange.Context;

        if (target.Kind == TargetKind.Store)
        {
            await HttpProblems.BadRequest(context, "Name a graph: ?graph=<IRI>, ?default, or the graph's own address.").ConfigureAwait(false);
            return;
        }

        if (!Negotiation.TryChoose(context.Request.Headers.Accept, MediaTypes.Graphs, out Offer<GraphFormat> syntax))
        {
            await HttpProblems.NotAcceptable(context, "A graph is written as N-Triples, Turtle, N-Quads, TriG, RDF/XML or JSON-LD.").ConfigureAwait(false);
            return;
        }

        if (await Reads.ResolveAsync(context, exchange.Dataset).ConfigureAwait(false) is not Position position)
        {
            return;
        }

        // The graph's version at the read's position is its tag (ADR 0126),
        // compared before any pin; as of a position, only within the as-of
        // bound, past which the read itself is refused below.
        RequestTerm graph = Writes.Term(GraphOf(target));
        bool withinBound = !Reads.IsAsOf(context) || position == exchange.Dataset.Head || Reads.AsOfDistance(exchange.Dataset, position) <= exchange.Options.Limits.MaxAsOfDistance;

        if (withinBound
            && await Reads.NotModifiedAsync(context, exchange.Dataset, position, await exchange.Dataset.GraphVersionAsync(graph, position, context.RequestAborted).ConfigureAwait(false)).ConfigureAwait(false))
        {
            return;
        }

        DatasetView? opened;

        try
        {
            opened = await Reads.OpenAsync(context, exchange.Dataset, position, Reads.IsAsOf(context), exchange.Options.Limits, context.RequestAborted).ConfigureAwait(false);
        }
        catch (DatasetUnavailableException failed)
        {
            await QueryRun.WriteUnavailableAsync(exchange, failed.Message).ConfigureAwait(false);
            return;
        }

        if (opened is not { } view)
        {
            return;
        }

        Position version = await exchange.Dataset.GraphVersionAsync(graph, view.Position, context.RequestAborted).ConfigureAwait(false);
        Preconditions.DescribeGraph(context.Response, version);

        // The graph was last modified by the commit that is its version.
        if (Reads.LastModified(exchange.Dataset, version) is DateTimeOffset modified)
        {
            context.Response.Headers.LastModified = Microsoft.Net.Http.Headers.HeaderUtilities.FormatDate(modified);
        }
        IQuadSource source = exchange.Readable(view);
        IQuadCursor? cursor = Open(source, target);

        if (cursor is null || !cursor.MoveNext())
        {
            cursor?.Dispose();

            if (target.Kind == TargetKind.Default)
            {
                // The default graph always exists, empty or not (GSP §5.2).
                // Empty is no bytes in the line syntaxes, and an empty
                // document in RDF/XML and JSON-LD, whose grammars have one.
                context.Response.ContentType = syntax.MediaType;

                if (syntax.Format is GraphFormat.RdfXml or GraphFormat.JsonLd && !HttpMethods.IsHead(context.Request.Method))
                {
                    await BoundedReads.RunAsync(context, exchange.Options, (output, _) =>
                    {
                        using GraphOutput empty = new(output, syntax.Format, exchange.Options.Limits);
                        empty.Complete();
                        return ValueTask.CompletedTask;
                    }).ConfigureAwait(false);
                }

                return;
            }

            await HttpProblems.WriteAsync(context, ProblemType.GraphNotFound).ConfigureAwait(false);
            return;
        }

        context.Response.RegisterForDispose(cursor);
        context.Response.ContentType = syntax.MediaType;

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await BoundedReads.RunAsync(context, exchange.Options, async (output, cancellationToken) =>
        {
            using GraphOutput graph = new(output, syntax.Format, exchange.Options.Limits);

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                Quad current = cursor.Current;
                graph.Write(in current, source);
                await output.FlushIfDueAsync(cancellationToken).ConfigureAwait(false);
            }
            while (cursor.MoveNext());

            graph.Complete();
        }).ConfigureAwait(false);
    }

    // A graph the caller cannot read is answered as a graph that is not
    // there, whatever the write; one it reads but may not write is 403
    // (ADR 0107). Asked before the body is read for a PUT or POST, and
    // before the view is pinned: the answers cost nothing.
    private static async Task<bool> MayWriteAsync(Exchange exchange, Target target)
    {
        RdfTerm? graph = target.Kind == TargetKind.Default ? null : target.Graph;

        if (!exchange.Scope.Readable.Allows(graph))
        {
            Preconditions.Describe(exchange.Response, exchange.Dataset.Head);
            await HttpProblems.WriteAsync(exchange.Context, ProblemType.GraphNotFound).ConfigureAwait(false);
            return false;
        }

        if (!exchange.Scope.Writable.Allows(graph))
        {
            await Writes.GraphNotWritableAsync(exchange, graph).ConfigureAwait(false);
            return false;
        }

        return true;
    }

    private static async Task DeleteAsync(Exchange exchange, Target target, WritePlan plan)
    {
        if (!await MayWriteAsync(exchange, target).ConfigureAwait(false))
        {
            return;
        }

        CommitRequest request;
        bool exists;

        using (DatasetView view = exchange.Dataset.Pin())
        {
            // It read the graph alone: the graph is its scope (ADR 0126).
            request = new CommitRequest { ExpectedPosition = plan.Expected ?? view.Position, ExpectedGraphs = Writes.Scope(plan.Graphs), Metadata = Writes.Metadata(exchange), Validators = Writes.Validators(exchange) };
            exists = Retract(view, target, request);
        }

        if (!await Writes.PresenceHoldsAsync(exchange, plan, GraphOf(target), exists).ConfigureAwait(false))
        {
            return;
        }

        if (!exists)
        {
            Preconditions.Describe(exchange.Response, exchange.Dataset.Head);
            await HttpProblems.WriteAsync(exchange.Context, ProblemType.GraphNotFound).ConfigureAwait(false);
            return;
        }

        if (await Writes.CommitAsync(exchange, request).ConfigureAwait(false) is not CommitResult result)
        {
            return;
        }

        await Writes.AnswerAsync(exchange, result, expected: plan.Expected, evidence: new ConflictEvidence(request.ExpectedPosition!.Value, request.ExpectedGraphs), taggedGraph: GraphOf(target), tagGraph: true).ConfigureAwait(false);
    }

    private static async Task WriteAsync(Exchange exchange, Target target, WritePlan plan, List<BodyTriple> triples, bool replace)
    {
        CommitRequest request;
        bool existed;

        using (DatasetView view = exchange.Dataset.Pin())
        {
            // A PUT replaces what it read, so it expects the position it read
            // at; a POST only adds, and expects a position only when asked.
            request = new CommitRequest
            {
                ExpectedPosition = plan.Expected ?? (replace ? view.Position : null),
                ExpectedGraphs = plan.Expected is not null || replace ? Writes.Scope(plan.Graphs) : null,
                Metadata = Writes.Metadata(exchange),
                Validators = Writes.Validators(exchange),
            };
            existed = replace ? Retract(view, target, request) : Exists(view, target);
        }

        if (!await Writes.PresenceHoldsAsync(exchange, plan, GraphOf(target), existed).ConfigureAwait(false))
        {
            return;
        }

        foreach (BodyTriple triple in triples)
        {
            if (target.Kind == TargetKind.Default)
            {
                request.Assert(triple.Subject, triple.Predicate, triple.Object);
            }
            else
            {
                request.Assert(triple.Subject, triple.Predicate, triple.Object, target.Graph!);
            }
        }

        if (await Writes.CommitAsync(exchange, request).ConfigureAwait(false) is not CommitResult result)
        {
            return;
        }

        // §5.3: new graph content is 201, the default graph's included.
        bool created = !existed && triples.Count > 0 && result.Outcome == CommitOutcome.Committed;

        if (created && target.Kind == TargetKind.Named && target.Address is { } location)
        {
            exchange.Response.Headers.Location = location;
        }

        ConflictEvidence? evidence = request.ExpectedPosition is Position since ? new ConflictEvidence(since, request.ExpectedGraphs) : null;
        await Writes.AnswerAsync(exchange, result, created ? StatusCodes.Status201Created : StatusCodes.Status204NoContent, plan.Expected, evidence, GraphOf(target), tagGraph: true).ConfigureAwait(false);
    }

    // Retracts every quad of the target graph, by handle. True when it held one.
    private static bool Retract(DatasetView view, Target target, CommitRequest request)
    {
        using IQuadCursor? cursor = Open(view, target);
        bool any = false;

        while (cursor is not null && cursor.MoveNext())
        {
            any = true;
            Quad quad = cursor.Current;
            RequestTerm subject = RequestTerm.Existing(quad.Subject);
            RequestTerm predicate = RequestTerm.Existing(quad.Predicate);
            RequestTerm @object = RequestTerm.Existing(quad.Object);

            if (target.Kind == TargetKind.Default)
            {
                request.Retract(subject, predicate, @object);
            }
            else
            {
                request.Retract(subject, predicate, @object, RequestTerm.Existing(quad.Graph));
            }
        }

        return any;
    }

    private static bool Exists(DatasetView view, Target target)
    {
        using IQuadCursor? cursor = Open(view, target);
        return cursor is not null && cursor.MoveNext();
    }

    private static IQuadCursor? Open(IQuadSource view, Target target)
    {
        if (target.Kind == TargetKind.Default)
        {
            return view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.DefaultGraph);
        }

        return view.TryInternalise(target.Graph!, out TermHandle graph)
            ? view.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(graph))
            : null;
    }

    private static async Task<List<BodyTriple>?> ReadBodyAsync(Exchange exchange, Target target, bool allowMultipart)
    {
        HttpContext context = exchange.Context;
        string? contentType = context.Request.ContentType;

        if (!MediaTypes.TryReadContentType(contentType, out string mediaType, out bool utf8))
        {
            await HttpProblems.BadRequest(context, "A Graph Store write carries a Content-Type.").ConfigureAwait(false);
            return null;
        }

        if (!utf8)
        {
            await HttpProblems.UnsupportedMediaType(context, "Every request body is UTF-8.").ConfigureAwait(false);
            return null;
        }

        Body body = await RequestBodies.ReadAsync(context, exchange.Options.Limits).ConfigureAwait(false);

        if (body.TooLarge)
        {
            await HttpProblems.RequestTooLarge(context, exchange.Options.Limits.MaxRequestBody.Value).ConfigureAwait(false);
            return null;
        }

        string baseIri = target.Address ?? exchange.Address();
        List<BodyTriple> triples = [];

        if (mediaType == MediaTypes.Multipart && allowMultipart)
        {
            return await ReadPartsAsync(exchange, contentType!, body.Bytes!, baseIri, triples).ConfigureAwait(false);
        }

        if (!MediaTypes.TryReadFormat(mediaType, out GraphFormat syntax))
        {
            await HttpProblems.UnsupportedMediaType(context, "A graph is read as N-Triples, Turtle, N-Quads, TriG, RDF/XML or JSON-LD.").ConfigureAwait(false);
            return null;
        }

        if (!RdfBodies.TryParse(body.Bytes, syntax, baseIri, triples, out string? error))
        {
            await HttpProblems.WriteAsync(context, ProblemType.RdfSyntax, error).ConfigureAwait(false);
            return null;
        }

        return triples;
    }

    // §5.5: each part of a multipart/form-data POST is a graph to merge.
    private static async Task<List<BodyTriple>?> ReadPartsAsync(Exchange exchange, string contentType, byte[] body, string baseIri, List<BodyTriple> triples)
    {
        HttpContext context = exchange.Context;
        MediaTypeHeaderValue type = MediaTypeHeaderValue.Parse(contentType);
        string boundary = HeaderUtilities.RemoveQuotes(type.Boundary).Value ?? string.Empty;

        if (boundary.Length == 0)
        {
            await HttpProblems.BadRequest(context, "A multipart body names its boundary.").ConfigureAwait(false);
            return null;
        }

        using MemoryStream stream = new(body, writable: false);
        MultipartReader reader = new(boundary, stream);

        while (await reader.ReadNextSectionAsync(context.RequestAborted).ConfigureAwait(false) is { } section)
        {
            if (!MediaTypes.TryReadContentType(section.ContentType, out string partType, out bool utf8) || !utf8
                || !MediaTypes.TryReadFormat(partType, out GraphFormat syntax))
            {
                await HttpProblems.UnsupportedMediaType(context, "Each part is N-Triples, Turtle, N-Quads, TriG, RDF/XML or JSON-LD, in UTF-8.").ConfigureAwait(false);
                return null;
            }

            using MemoryStream part = new();
            await section.Body.CopyToAsync(part, context.RequestAborted).ConfigureAwait(false);

            if (!RdfBodies.TryParse(part.GetBuffer().AsMemory(0, (int)part.Length), syntax, baseIri, triples, out string? error))
            {
                await HttpProblems.WriteAsync(context, ProblemType.RdfSyntax, "A part does not parse: " + error).ConfigureAwait(false);
                return null;
            }
        }

        return triples;
    }

    // Which graph the request names: its own address (direct), ?graph= or
    // ?default (indirect), or the store itself.
    private static async Task<Target> TargetOfAsync(Exchange exchange)
    {
        HttpContext context = exchange.Context;
        IQueryCollection query = context.Request.Query;
        bool direct = context.Request.RouteValues.TryGetValue(PathValue, out object? path) && path is string { Length: > 0 };
        StringValues graph = query["graph"];
        bool isDefault = query.ContainsKey("default");

        if ((direct ? 1 : 0) + (graph.Count > 0 ? 1 : 0) + (isDefault ? 1 : 0) > 1 || graph.Count > 1)
        {
            await HttpProblems.BadRequest(context, "A request names one graph: by its own address, by ?graph=, or ?default.").ConfigureAwait(false);
            return new Target(TargetKind.Invalid, null, null);
        }

        if (isDefault)
        {
            return new Target(TargetKind.Default, null, null);
        }

        if (graph.Count == 1)
        {
            if (!SparqlRequest.TryIri(graph[0], out RdfTerm iri))
            {
                await HttpProblems.BadRequest(context, "?graph= is an absolute IRI.").ConfigureAwait(false);
                return new Target(TargetKind.Invalid, null, null);
            }

            return new Target(TargetKind.Named, iri, graph[0]);
        }

        if (direct)
        {
            string address = exchange.Address();

            if (!SparqlRequest.TryIri(address, out RdfTerm iri))
            {
                await HttpProblems.BadRequest(context, "The request's address is not an IRI.").ConfigureAwait(false);
                return new Target(TargetKind.Invalid, null, null);
            }

            return new Target(TargetKind.Named, iri, address);
        }

        return new Target(TargetKind.Store, null, null);
    }

    private enum TargetKind : byte
    {
        Invalid,
        Store,
        Default,
        Named,
    }

    private readonly record struct Target(TargetKind Kind, RdfTerm? Graph, string? Address);
}
