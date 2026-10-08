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
using Varve.Turtle;

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

        if (await Exchange.BeginAsync(context, options, read ? DatasetPermissions.Read : DatasetPermissions.Write).ConfigureAwait(false) is not { } exchange)
        {
            return;
        }

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

        if (await Writes.CheckAsync(exchange).ConfigureAwait(false) is not { Proceed: true } plan)
        {
            return;
        }

        if (HttpMethods.IsDelete(method))
        {
            await DeleteAsync(exchange, target, plan).ConfigureAwait(false);
            return;
        }

        if (target.Kind == TargetKind.Store)
        {
            // §5.5: a POST to the store makes a new graph, named by the server.
            string minted = exchange.Address().TrimEnd('/') + "/" + context.TraceIdentifier.Replace(':', '-');
            target = new Target(TargetKind.Named, RdfTerm.Iri(Encoding.UTF8.GetBytes(minted)), minted);

            // A name the server just made reveals nothing: the one question
            // is whether the caller may write it (ADR 0106).
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

        List<BodyTriple>? triples = await ReadBodyAsync(exchange, target, allowMultipart: HttpMethods.IsPost(method)).ConfigureAwait(false);

        if (triples is null)
        {
            return;
        }

        await WriteAsync(exchange, target, plan, triples, replace: HttpMethods.IsPut(method)).ConfigureAwait(false);
    }

    private static async Task GetAsync(Exchange exchange, Target target)
    {
        HttpContext context = exchange.Context;

        if (target.Kind == TargetKind.Store)
        {
            await HttpProblems.BadRequest(context, "Name a graph: ?graph=<IRI>, ?default, or the graph's own address.").ConfigureAwait(false);
            return;
        }

        if (!Negotiation.TryChoose(context.Request.Headers.Accept, MediaTypes.Graphs, out Offer<RdfSyntax> syntax))
        {
            await HttpProblems.NotAcceptable(context, "A graph is written as N-Triples, Turtle, N-Quads or TriG.").ConfigureAwait(false);
            return;
        }

        if (await Reads.ResolveAsync(context, exchange.Dataset).ConfigureAwait(false) is not Position position
            || await Reads.NotModifiedAsync(context, position).ConfigureAwait(false))
        {
            return;
        }

        DatasetView view;

        try
        {
            view = await Reads.OpenAsync(context, exchange.Dataset, position, Reads.IsAsOf(context), context.RequestAborted).ConfigureAwait(false);
        }
        catch (DatasetUnavailableException failed)
        {
            await QueryRun.WriteUnavailableAsync(exchange, failed.Message).ConfigureAwait(false);
            return;
        }

        IQuadSource source = exchange.Readable(view);
        IQuadCursor? cursor = Open(source, target);

        if (cursor is null || !cursor.MoveNext())
        {
            cursor?.Dispose();

            if (target.Kind == TargetKind.Default)
            {
                // The default graph always exists, empty or not (GSP §5.2).
                context.Response.ContentType = syntax.MediaType;
                return;
            }

            await HttpProblems.WriteAsync(context, StatusCodes.Status404NotFound, ProblemType.GraphNotFound, "No graph by that name holds a quad.").ConfigureAwait(false);
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
            WriteOptions lines = new() { Syntax = RdfSyntax.NTriples };

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                Quad current = cursor.Current;
                Quad triple = new(current.Subject, current.Predicate, current.Object);
                NQuadsWriter.Write(output, in triple, source, in lines);
                await output.FlushIfDueAsync(cancellationToken).ConfigureAwait(false);
            }
            while (cursor.MoveNext());
        }).ConfigureAwait(false);
    }

    // A graph the caller cannot read is answered as a graph that is not
    // there, whatever the write; one it reads but may not write is 403
    // (ADR 0106). Asked before the body is read for a PUT or POST, and
    // before the view is pinned: the answers cost nothing.
    private static async Task<bool> MayWriteAsync(Exchange exchange, Target target)
    {
        RdfTerm? graph = target.Kind == TargetKind.Default ? null : target.Graph;

        if (!exchange.Scope.Readable.Allows(graph))
        {
            Preconditions.Describe(exchange.Response, exchange.Dataset.Head);
            await HttpProblems.WriteAsync(exchange.Context, StatusCodes.Status404NotFound, ProblemType.GraphNotFound, "No graph by that name holds a quad.").ConfigureAwait(false);
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
            request = new CommitRequest { ExpectedPosition = plan.Expected ?? view.Position, Metadata = Writes.Metadata(exchange) };
            exists = Retract(view, target, request);
        }

        if (!exists)
        {
            Preconditions.Describe(exchange.Response, exchange.Dataset.Head);
            await HttpProblems.WriteAsync(exchange.Context, StatusCodes.Status404NotFound, ProblemType.GraphNotFound, "No graph by that name holds a quad.").ConfigureAwait(false);
            return;
        }

        CommitResult result = await exchange.Dataset.CommitAsync(request, exchange.Context.RequestAborted).ConfigureAwait(false);
        await Writes.AnswerAsync(exchange, result).ConfigureAwait(false);
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
                Metadata = Writes.Metadata(exchange),
            };
            existed = replace ? Retract(view, target, request) : Exists(view, target);
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

        CommitResult result = await exchange.Dataset.CommitAsync(request, exchange.Context.RequestAborted).ConfigureAwait(false);
        // §5.3: new graph content is 201, the default graph's included.
        bool created = !existed && triples.Count > 0 && result.Outcome == CommitOutcome.Committed;

        if (created && target.Kind == TargetKind.Named && target.Address is { } location)
        {
            exchange.Response.Headers.Location = location;
        }

        await Writes.AnswerAsync(exchange, result, created ? StatusCodes.Status201Created : StatusCodes.Status204NoContent).ConfigureAwait(false);
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
            await HttpProblems.RequestTooLarge(context).ConfigureAwait(false);
            return null;
        }

        string baseIri = target.Address ?? exchange.Address();
        List<BodyTriple> triples = [];

        if (mediaType == MediaTypes.Multipart && allowMultipart)
        {
            return await ReadPartsAsync(exchange, contentType!, body.Bytes!, baseIri, triples).ConfigureAwait(false);
        }

        if (!MediaTypes.TryReadSyntax(mediaType, out RdfSyntax syntax))
        {
            await HttpProblems.UnsupportedMediaType(context, "A graph is read as N-Triples, Turtle, N-Quads or TriG.").ConfigureAwait(false);
            return null;
        }

        if (!RdfBodies.TryParse(body.Bytes, syntax, baseIri, triples, out string? error))
        {
            await HttpProblems.WriteAsync(context, StatusCodes.Status400BadRequest, ProblemType.RdfSyntax, "The body does not parse.", error).ConfigureAwait(false);
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
                || !MediaTypes.TryReadSyntax(partType, out RdfSyntax syntax))
            {
                await HttpProblems.UnsupportedMediaType(context, "Each part is N-Triples, Turtle, N-Quads or TriG, in UTF-8.").ConfigureAwait(false);
                return null;
            }

            using MemoryStream part = new();
            await section.Body.CopyToAsync(part, context.RequestAborted).ConfigureAwait(false);

            if (!RdfBodies.TryParse(part.GetBuffer().AsSpan(0, (int)part.Length), syntax, baseIri, triples, out string? error))
            {
                await HttpProblems.WriteAsync(context, StatusCodes.Status400BadRequest, ProblemType.RdfSyntax, "A part does not parse.", error).ConfigureAwait(false);
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
