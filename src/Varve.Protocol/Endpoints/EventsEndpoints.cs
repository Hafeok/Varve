// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Varve.Delta;
using Varve.Delta.Model;
using Varve.Events;
using Varve.Events.Model;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// The events of a dataset over HTTP (ADRs 0143–0145, docs/spec/events.md §8):
/// a range of rendered CloudEvents, the types list, the schemas by version and
/// revision, the AsyncAPI document, and the server-wide list. Every view is the
/// caller's: a type whose catalogue graph the caller cannot read is in none of
/// them, and an event in a graph it cannot read is not rendered for it.
/// </summary>
internal static class EventsEndpoints
{
    internal const string TypeRouteValue = "type";
    internal const string VersionRouteValue = "version";
    internal const string RevisionRouteValue = "revision";

    internal const string BatchMediaType = "application/cloudevents-batch+json";
    internal const string SchemaMediaType = "application/schema+json";

    /// <summary><c>GET …/events</c>: a range as a canonical CloudEvents batch, or an open range as server-sent events.</summary>
    internal static async Task HandleAsync(HttpContext context, ProtocolOptions options)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "GET").ConfigureAwait(false);
            return;
        }

        if (await Exchange.BeginAsync(context, options, DatasetPermissions.Read).ConfigureAwait(false) is not { } exchange)
        {
            return;
        }

        bool stream = Accepts(context.Request.Headers.Accept, MediaTypes.EventStream);

        if (!stream && !Accepts(context.Request.Headers.Accept, BatchMediaType) && !Accepts(context.Request.Headers.Accept, "application/json"))
        {
            await HttpProblems.NotAcceptable(context, "The events are application/cloudevents-batch+json, or text/event-stream for an open range.").ConfigureAwait(false);
            return;
        }

        StringValues resume = context.Request.Headers["Last-Event-ID"];

        if (await Ranges.ResolveAsync(context, exchange.Dataset, endDefaultsToHead: !stream, resume.Count == 1 ? resume[0] : null).ConfigureAwait(false) is not FeedRange range)
        {
            return;
        }

        string? type = context.Request.Query["type"].Count == 1 ? context.Request.Query["type"][0] : null;
        HttpResponse response = context.Response;
        response.Headers[Preconditions.PositionHeader] = range.From.Value.ToString(CultureInfo.InvariantCulture);
        response.Headers.CacheControl = "no-store";

        IDisposable? slot = null;

        if (range.To is null)
        {
            slot = options.Tails.TryOpen(context, options, options.Limits.MaxLiveTailsPerClient);

            if (slot is null)
            {
                await HttpProblems.WriteAsync(context, ProblemType.TooManyLiveTails,
                    "The client holds " + options.Limits.MaxLiveTailsPerClient.ToString(CultureInfo.InvariantCulture) + " live tails open already; close one before opening another.",
                    members => members.Number("limit", options.Limits.MaxLiveTailsPerClient)).ConfigureAwait(false);
                return;
            }
        }
        else if (range.To.Value.Value - range.From.Value > options.Limits.CommitsPageSize)
        {
            Position last = new(range.From.Value + options.Limits.CommitsPageSize);
            response.Headers.Append("Link", "<" + NextPage(context, last) + ">; rel=\"next\"");
            range = range with { To = last };
        }

        EventEndpoints endpoints = Endpoints(context, options, exchange.Name);

        using (slot)
        {
            if (stream)
            {
                response.ContentType = MediaTypes.EventStream;
                await StreamAsync(exchange, range, type, endpoints).ConfigureAwait(false);
            }
            else
            {
                await BatchAsync(exchange, range, type, endpoints).ConfigureAwait(false);
            }
        }
    }

    /// <summary>A bounded range, rendered whole before the first byte, so that a limit is a problem response and never half a batch.</summary>
    private static async Task BatchAsync(Exchange exchange, FeedRange range, string? type, EventEndpoints endpoints)
    {
        HttpContext context = exchange.Context;
        ProtocolOptions options = exchange.Options;
        using CancellationTokenSource limit = new(Min(options.Limits.QueryTimeout, options.Limits.PinnedReadLifetime), options.Clock);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(limit.Token, context.RequestAborted, options.Stopping);
        ArrayBufferWriter<byte> batch = new();
        batch.Write("["u8);
        bool first = true;

        if (range.To is not Position to || to > range.From)
        {
            EventCatalogueHistory history = await options.Catalogues.UpToAsync(exchange.Dataset, range.From, linked.Token).ConfigureAwait(false);
            EventRenderer renderer = new();

            await foreach (Commit commit in exchange.Dataset.Subscribe(range.From, SubscriptionFilter.All, linked.Token).ConfigureAwait(false))
            {
                if (range.To is Position end && commit.Position > end)
                {
                    break;
                }

                EventCatalogues.Apply(history, commit);
                Rendered rendered = await RenderAsync(exchange, renderer, history, commit, type, endpoints, linked.Token).ConfigureAwait(false);

                if (rendered.Distance is long distance)
                {
                    await DistanceExceededAsync(context, options, distance).ConfigureAwait(false);
                    return;
                }

                foreach (RenderedEvent cloudEvent in rendered.Events)
                {
                    if (!first)
                    {
                        batch.Write(","u8);
                    }

                    first = false;
                    batch.Write(cloudEvent.Json.Span);
                }

                if (batch.WrittenCount > options.Limits.ResultSizeCap.Value)
                {
                    await HttpProblems.WriteAsync(context, ProblemType.ReadLimitExceeded, "The events of the range are over the server's result size; read a shorter range.").ConfigureAwait(false);
                    return;
                }

                if (range.To is Position stop && commit.Position >= stop)
                {
                    break;
                }
            }
        }

        batch.Write("]"u8);
        context.Response.ContentType = BatchMediaType;
        await context.Response.BodyWriter.WriteAsync(batch.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>An open range: one server-sent event per commit with an event, its id the position, its data the commit's events.</summary>
    private static async Task StreamAsync(Exchange exchange, FeedRange range, string? type, EventEndpoints endpoints)
    {
        HttpContext context = exchange.Context;
        ProtocolOptions options = exchange.Options;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, options.Stopping);
        CancellationToken cancellationToken = linked.Token;
        PipeWriter body = context.Response.BodyWriter;
        ArrayBufferWriter<byte> data = new(4096);
        Position last = range.From;

        try
        {
            EventCatalogueHistory history = await options.Catalogues.UpToAsync(exchange.Dataset, range.From, cancellationToken).ConfigureAwait(false);
            EventRenderer renderer = new();
            await body.FlushAsync(cancellationToken).ConfigureAwait(false);
            await using IAsyncEnumerator<Commit> commits = exchange.Dataset.Subscribe(range.From, SubscriptionFilter.All, cancellationToken).GetAsyncEnumerator(cancellationToken);
            Task<bool>? pending = null;

            try
            {
                while (true)
                {
                    Task<bool> next = commits.MoveNextAsync().AsTask();
                    pending = next;

                    while (await Task.WhenAny(next, Task.Delay(options.Limits.FeedHeartbeat, options.Clock, cancellationToken)).ConfigureAwait(false) != next)
                    {
                        body.Write(":\n\n"u8);
                        await body.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (!await next.ConfigureAwait(false))
                    {
                        return;
                    }

                    Commit commit = commits.Current;
                    EventCatalogues.Apply(history, commit);
                    Rendered rendered = await RenderAsync(exchange, renderer, history, commit, type, endpoints, cancellationToken).ConfigureAwait(false);

                    if (rendered.Distance is not null)
                    {
                        await EndAsync(body, ProblemType.AsOfDistanceExceeded, last).ConfigureAwait(false);
                        return;
                    }

                    if (rendered.Events.Count > 0)
                    {
                        data.ResetWrittenCount();
                        data.Write("["u8);

                        for (int i = 0; i < rendered.Events.Count; i++)
                        {
                            if (i > 0)
                            {
                                data.Write(","u8);
                            }

                            data.Write(rendered.Events[i].Json.Span);
                        }

                        data.Write("]"u8);
                        DeltaWriter.WriteEvent(body, "events"u8, new FeedPosition(commit.Position.Value), data.WrittenSpan);
                        last = commit.Position;
                        await body.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                if (pending is { IsCompleted: false })
                {
                    await linked.CancelAsync().ConfigureAwait(false);
                    await ((Task)pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client left.
        }
        catch (OperationCanceledException) when (options.Stopping.IsCancellationRequested)
        {
            await EndAsync(body, ProblemType.ShuttingDown, last).ConfigureAwait(false);
        }
    }

    private readonly record struct Rendered(IReadOnlyList<RenderedEvent> Events, long? Distance);

    /// <summary>
    /// Renders one commit for the caller: the catalogue at the commit, cut to
    /// the catalogue graphs the caller reads (and to one type when asked); the
    /// commit's type assertions cut to the graphs it reads; then, when an event
    /// is left, one as-of read at the commit for the closures.
    /// </summary>
    private static async Task<Rendered> RenderAsync(
        Exchange exchange, EventRenderer renderer, EventCatalogueHistory history, Commit commit, string? type, EventEndpoints endpoints, CancellationToken cancellationToken)
    {
        if (commit.Kind != CommitKind.Data)
        {
            return new Rendered([], null);
        }

        GraphScope readable = exchange.Scope.Readable;
        EventCatalogueSnapshot snapshot = Visible(history.At(new FeedPosition(commit.Position.Value)), readable, type);
        List<TypeAssertion> assertions = [];

        foreach (TypeAssertion assertion in EventCatalogues.TypeAssertions(commit))
        {
            if (readable.Allows(assertion.Graph))
            {
                assertions.Add(assertion);
            }
        }

        EventCommit input = new(new FeedPosition(commit.Position.Value), new FeedTimestamp(commit.Timestamp.Value), assertions);

        if (!EventRenderer.HasEvents(input, snapshot))
        {
            return new Rendered([], null);
        }

        Dataset dataset = exchange.Dataset;

        if (commit.Position != dataset.Head)
        {
            long distance = Reads.AsOfDistance(dataset, commit.Position);

            if (distance > exchange.Options.Limits.MaxAsOfDistance)
            {
                return new Rendered([], distance);
            }
        }

        using DatasetView view = await dataset.AsOfAsync(commit.Position, cancellationToken).ConfigureAwait(false);
        return new Rendered(renderer.Render(input, snapshot, exchange.Readable(view), endpoints, cancellationToken), null);
    }

    /// <summary><c>GET …/events/types</c>: the dataset's types at the read's position.</summary>
    internal static async Task TypesAsync(HttpContext context, ProtocolOptions options)
    {
        if (await CatalogueAsync(context, options) is not { } read)
        {
            return;
        }

        List<ListedType> listed = [];

        foreach (CataloguedType type in read.Snapshot.Types)
        {
            listed.Add(new ListedType(read.Endpoints, type));
        }

        await WriteJsonAsync(context, read.Position, "application/json", EventViews.DatasetList(listed)).ConfigureAwait(false);
    }

    /// <summary><c>GET …/events/asyncapi</c>: the AsyncAPI 3.0 document at the read's position.</summary>
    internal static async Task AsyncApiAsync(HttpContext context, ProtocolOptions options)
    {
        if (await CatalogueAsync(context, options) is not { } read)
        {
            return;
        }

        byte[] document = EventViews.AsyncApi(read.Endpoints, read.Snapshot.Types, new FeedPosition(read.Position.Value), options.EventHubs);
        await WriteJsonAsync(context, read.Position, "application/json", document).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>GET …/events/types/{type}[/versions/{n}[/revisions/{r}]]</c>: a
    /// revision is immutable and answered as such; a version or the bare type
    /// resolves to its latest revision, named by <c>Content-Location</c>.
    /// </summary>
    internal static async Task SchemaAsync(HttpContext context, ProtocolOptions options)
    {
        if (await CatalogueAsync(context, options) is not { } read)
        {
            return;
        }

        RouteValueDictionary values = context.Request.RouteValues;
        string name = (string)values[TypeRouteValue]!;

        if (!read.Snapshot.TryGet(name, out CataloguedType? type))
        {
            await HttpProblems.WriteAsync(context, ProblemType.NotFound, "No event type by that name.").ConfigureAwait(false);
            return;
        }

        SchemaRevision? chosen = type.Current;

        if (values.TryGetValue(VersionRouteValue, out object? versionValue))
        {
            chosen = null;

            if (!int.TryParse((string?)versionValue, NumberStyles.None, CultureInfo.InvariantCulture, out int version))
            {
                await HttpProblems.WriteAsync(context, ProblemType.NotFound, "No such version.").ConfigureAwait(false);
                return;
            }

            long? wanted = null;

            if (values.TryGetValue(RevisionRouteValue, out object? revisionValue))
            {
                if (!long.TryParse((string?)revisionValue, NumberStyles.None, CultureInfo.InvariantCulture, out long revision))
                {
                    await HttpProblems.WriteAsync(context, ProblemType.NotFound, "No such revision.").ConfigureAwait(false);
                    return;
                }

                wanted = revision;
            }

            foreach (SchemaRevision candidate in type.Revisions)
            {
                if (candidate.Version == version && (wanted is null || candidate.Revision.Value == wanted))
                {
                    chosen = candidate;
                }
            }
        }

        if (chosen is not SchemaRevision found)
        {
            await HttpProblems.WriteAsync(context, ProblemType.NotFound, "No such version or revision of the type.").ConfigureAwait(false);
            return;
        }

        EventCatalogueHistory history = await options.Catalogues.UpToAsync(read.Dataset, read.Position, context.RequestAborted).ConfigureAwait(false);
        history.At(found.Revision).TryGet(name, out CataloguedType? then);
        string id = read.Endpoints.Schema(name, found.Version, found.Revision);
        byte[] schema = EventSchemas.Generate(then!.Declaration, id);

        if (!values.ContainsKey(RevisionRouteValue))
        {
            context.Response.Headers.ContentLocation = id;
        }
        else
        {
            // A revision's bytes never change (ADR 0145): cacheable for ever.
            context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        }

        await WriteJsonAsync(context, read.Position, SchemaMediaType, schema, cacheControl: !values.ContainsKey(RevisionRouteValue)).ConfigureAwait(false);
    }

    /// <summary><c>GET /events/types</c>: every dataset's types the caller reads, each with its dataset.</summary>
    internal static async Task ServerTypesAsync(HttpContext context, ProtocolOptions options)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "GET").ConfigureAwait(false);
            return;
        }

        if (options.Administration is null)
        {
            await HttpProblems.WriteAsync(context, ProblemType.NotFound, "This host lists no datasets.").ConfigureAwait(false);
            return;
        }

        List<ListedType> listed = [];

        foreach (DatasetEntry entry in options.Administration.List())
        {
            if (!await Exchange.MayAsync(context, options, DatasetPermissions.Read, entry.Name).ConfigureAwait(false)
                || !options.Datasets.TryResolve(entry.Name, out Dataset? dataset))
            {
                continue;
            }

            CallerScope scope = options.AccessScopes.ScopesOf(context.User, entry.Name);
            Position head = dataset.Head;
            EventCatalogueHistory history = await options.Catalogues.UpToAsync(dataset, head, context.RequestAborted).ConfigureAwait(false);
            EventEndpoints endpoints = Endpoints(context, options, entry.Name);

            foreach (CataloguedType type in Visible(history.At(new FeedPosition(head.Value)), scope.Readable, null).Types)
            {
                listed.Add(new ListedType(endpoints, type));
            }
        }

        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.BodyWriter.WriteAsync(EventViews.ServerList(listed), context.RequestAborted).ConfigureAwait(false);
    }

    private sealed record CatalogueRead(Dataset Dataset, Position Position, EventCatalogueSnapshot Snapshot, EventEndpoints Endpoints);

    /// <summary>The catalogue as of the read's position (<c>Varve-As-Of</c>), cut to what the caller reads.</summary>
    private static async Task<CatalogueRead?> CatalogueAsync(HttpContext context, ProtocolOptions options)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            await HttpProblems.MethodNotAllowed(context, "GET, HEAD").ConfigureAwait(false);
            return null;
        }

        if (await Exchange.BeginAsync(context, options, DatasetPermissions.Read).ConfigureAwait(false) is not { } exchange)
        {
            return null;
        }

        if (await Reads.ResolveAsync(context, exchange.Dataset).ConfigureAwait(false) is not Position position)
        {
            return null;
        }

        EventCatalogueHistory history = await options.Catalogues.UpToAsync(exchange.Dataset, position, context.RequestAborted).ConfigureAwait(false);
        EventCatalogueSnapshot snapshot = Visible(history.At(new FeedPosition(position.Value)), exchange.Scope.Readable, null);
        return new CatalogueRead(exchange.Dataset, position, snapshot, Endpoints(context, options, exchange.Name));
    }

    /// <summary>The types whose catalogue graph the caller reads, and of one name when asked.</summary>
    private static EventCatalogueSnapshot Visible(EventCatalogueSnapshot snapshot, GraphScope readable, string? type) =>
        readable.IsAll && type is null ? snapshot : snapshot.Where(t => readable.Allows(t.CatalogueGraph) && (type is null || t.Declaration.Name == type));

    /// <summary>The URLs of a dataset's events: under <see cref="ProtocolOptions.EventSource"/>, or the request's base.</summary>
    internal static EventEndpoints Endpoints(HttpContext context, ProtocolOptions options, DatasetName name)
    {
        string source = options.EventSource
            ?? string.Concat(context.Request.Scheme, "://", context.Request.Host.ToUriComponent(), context.Request.PathBase.ToUriComponent(), "/");
        return new EventEndpoints(source, name.Value);
    }

    private static async Task WriteJsonAsync(HttpContext context, Position position, string mediaType, byte[] body, bool cacheControl = true)
    {
        HttpResponse response = context.Response;
        response.ContentType = mediaType;
        response.Headers[Preconditions.PositionHeader] = position.Value.ToString(CultureInfo.InvariantCulture);

        if (cacheControl)
        {
            response.Headers.CacheControl = "no-cache";
        }

        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await response.BodyWriter.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
        }
    }

    private static Task DistanceExceededAsync(HttpContext context, ProtocolOptions options, long distance) =>
        HttpProblems.WriteAsync(context, ProblemType.AsOfDistanceExceeded,
            "A commit of the range is " + distance.ToString(CultureInfo.InvariantCulture) + " commits above its nearest checkpoint; the server replays at most "
            + options.Limits.MaxAsOfDistance.ToString(CultureInfo.InvariantCulture) + ". Take a checkpoint, or read a range one covers.",
            members =>
            {
                members.Number("limit", options.Limits.MaxAsOfDistance);
                members.Number("actual", distance);
            });

    private static async Task EndAsync(PipeWriter body, ProblemType problem, Position last)
    {
        ArrayBufferWriter<byte> record = new(256);
        DeltaLines.WriteError(record, problem);
        DeltaLines.WriteEvent(body, problem == ProblemType.ShuttingDown ? "shutdown"u8 : "error"u8, last, record.WrittenSpan);
        await body.FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static bool Accepts(StringValues accept, string mediaType)
    {
        if (accept.Count == 0)
        {
            return mediaType == BatchMediaType;
        }

        foreach (string? value in accept)
        {
            foreach (string part in (value ?? string.Empty).Split(','))
            {
                string type = part.Split(';')[0].Trim();

                if (type.Equals(mediaType, StringComparison.OrdinalIgnoreCase) || (type == "*/*" && mediaType == BatchMediaType))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string NextPage(HttpContext context, Position from)
    {
        System.Text.StringBuilder next = new();
        next.Append(context.Request.PathBase.ToUriComponent()).Append(context.Request.Path.ToUriComponent()).Append("?from=").Append(from.Value.ToString(CultureInfo.InvariantCulture));

        foreach (KeyValuePair<string, StringValues> parameter in context.Request.Query)
        {
            if (parameter.Key is "from" or "fromTime")
            {
                continue;
            }

            foreach (string? value in parameter.Value)
            {
                next.Append('&').Append(Uri.EscapeDataString(parameter.Key)).Append('=').Append(Uri.EscapeDataString(value ?? string.Empty));
            }
        }

        return next.ToString();
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
