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
using Microsoft.Extensions.Primitives;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;

namespace Varve.Protocol.Endpoints;

/// <summary>
/// The commits resource (ADRs 0097, 0118; <c>change-feed.md</c> §4):
/// <c>GET /commits</c> is the closed commits in a range, read through the
/// subscription contract and nothing else, a bounded range a finite body and
/// an open one a live tail with heartbeats until the client leaves or the
/// host stops; <c>GET /commits/{position}</c> is one commit as one record.
/// </summary>
internal static class CommitsEndpoint
{
    internal const string DeltaContentType = MediaTypes.Delta + "; version=1";
    internal const string PositionRouteValue = "position";

    /// <summary><c>GET /commits/{position}</c>: one commit, <c>404</c> above the head.</summary>
    internal static async Task HandleOneAsync(HttpContext context, ProtocolOptions options)
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

        if (!Negotiation.TryChoose(context.Request.Headers.Accept, MediaTypes.Feeds, out Offer<FeedFraming> framing) || framing.Format != FeedFraming.Delta)
        {
            await HttpProblems.NotAcceptable(context, "A commit is application/vnd.varve.delta.").ConfigureAwait(false);
            return;
        }

        if (!context.Request.RouteValues.TryGetValue(PositionRouteValue, out object? value)
            || value is not string text || !Instants.IsDecimal(text) || !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long number) || number < 1)
        {
            await HttpProblems.BadRequest(context, "A commit is named by its position, a positive integer.").ConfigureAwait(false);
            return;
        }

        Position position = new(number);
        Dataset dataset = exchange.Dataset;
        Position head = dataset.Head;

        if (position > head)
        {
            Preconditions.Describe(context.Response, head);
            await HttpProblems.WriteAsync(context, ProblemType.PositionNotReached, "The head is " + head.ToString() + ".",
                members => members.Number("headPosition", head.Value)).ConfigureAwait(false);
            return;
        }

        if (await Reads.NotModifiedAsync(context, exchange.Dataset, position).ConfigureAwait(false))
        {
            return;
        }

        // A closed position never changes: the commit is immutable (ADR 0119).
        Preconditions.Describe(context.Response, position);
        Reads.DescribeClosed(context, dataset, position);
        context.Response.ContentType = DeltaContentType;
        ScopeFilter scope = new(exchange.Scope.Readable);

        await BoundedReads.RunAsync(context, options, async (output, cancellationToken) =>
        {
            await using IAsyncEnumerator<Commit> commits = dataset.Subscribe(new Position(number - 1), SubscriptionFilter.All, cancellationToken).GetAsyncEnumerator(cancellationToken);

            if (await commits.MoveNextAsync().ConfigureAwait(false) && commits.Current.Position == position)
            {
                Commit commit = commits.Current;
                QuadDelta delta = scope.Apply(commit.Delta, commit.TryExternalise);

                if (commit.Kind == CommitKind.Data || exchange.Scope.IsAdmin)
                {
                    DeltaLines.WriteCommit(output, commit, delta, commit.TryExternalise);
                }
            }
        }).ConfigureAwait(false);
    }

    /// <summary><c>GET /commits</c>: a bounded range, or a live tail.</summary>
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

        if (!Negotiation.TryChoose(context.Request.Headers.Accept, MediaTypes.Feeds, out Offer<FeedFraming> framing))
        {
            await HttpProblems.NotAcceptable(context, "The commits are application/vnd.varve.delta, or text/event-stream for a live tail.").ConfigureAwait(false);
            return;
        }

        IQueryCollection query = context.Request.Query;

        if (!FeedFilter.TryParse(query["graph"], query["pattern"], out FeedFilter filter, out string? invalid))
        {
            await HttpProblems.BadRequest(context, invalid!).ConfigureAwait(false);
            return;
        }

        StringValues resume = context.Request.Headers["Last-Event-ID"];

        if (await Ranges.ResolveAsync(context, exchange.Dataset, endDefaultsToHead: false, resume.Count == 1 ? resume[0] : null).ConfigureAwait(false) is not FeedRange range)
        {
            return;
        }

        HttpResponse response = context.Response;
        response.Headers[Preconditions.PositionHeader] = range.From.Value.ToString(CultureInfo.InvariantCulture);
        response.Headers.CacheControl = "no-store";

        if (range.To is Position empty && empty <= range.From)
        {
            response.ContentType = framing.Format == FeedFraming.EventStream ? MediaTypes.EventStream : DeltaContentType;
            return;
        }

        IDisposable? slot = null;

        if (range.To is null)
        {
            // A live tail per client, bounded (ADR 0114); the slot is held
            // for the tail's life.
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
            // A page of the range, decided before the first byte, so that the
            // header can say where the next one starts (ADRs 0114, 0118).
            Position last = new(range.From.Value + options.Limits.CommitsPageSize);
            response.Headers.Append("Link", "<" + NextPage(context, last) + ">; rel=\"next\"");
            range = range with { To = last };
        }

        response.ContentType = framing.Format == FeedFraming.EventStream ? MediaTypes.EventStream : DeltaContentType;

        using (slot)
        {
            if (!filter.IsAll)
            {
                using DatasetView pin = exchange.Dataset.Pin();
                filter.Resolve(pin);
            }

            await StreamAsync(exchange, range, filter, framing.Format).ConfigureAwait(false);
        }
    }

    // The request's own address with from replaced by the page's last
    // position, every other parameter kept, so the next page is the same
    // request continued.
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

    private static async Task StreamAsync(Exchange exchange, FeedRange range, FeedFilter filter, FeedFraming framing)
    {
        HttpContext context = exchange.Context;
        ProtocolOptions options = exchange.Options;
        bool live = range.To is null;

        // A live tail is no pinned read and has no time limit (ADR 0095); a
        // bounded range is a read, bounded as one.
        using CancellationTokenSource limit = live
            ? new CancellationTokenSource()
            : new CancellationTokenSource(Min(options.Limits.QueryTimeout, options.Limits.PinnedReadLifetime), options.Clock);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(limit.Token, context.RequestAborted, options.Stopping);
        CancellationToken cancellationToken = linked.Token;

        // The store filters when it can, which keeps a live tail's reads to
        // what it delivers (ADR 0042). A bounded range reads every commit so
        // that it sees its end even when the commits before it are filtered out.
        bool storeFilters = live && filter.IsResolved;
        SubscriptionFilter subscription = storeFilters ? filter.ToSubscription() : SubscriptionFilter.All;
        PipeWriter body = context.Response.BodyWriter;
        ArrayBufferWriter<byte> record = new(4096);
        long written = 0;
        Position last = range.From;
        ScopeFilter scope = new(exchange.Scope.Readable);
        bool admin = exchange.Scope.IsAdmin;

        try
        {
            await using IAsyncEnumerator<Commit> commits = exchange.Dataset
                .Subscribe(range.From, subscription, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            // An enumerator is not disposed while a MoveNextAsync is in flight:
            // the store refuses it, and its exception would replace the one
            // that ended the stream (a heartbeat's flush cancelled at shutdown,
            // say), so the stream would end with no record saying why.
            Task<bool>? pending = null;

            try
            {
                if (live)
                {
                    // Headers out at once, so that a client sees the stream open.
                    await body.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                while (true)
                {
                    Task<bool> next = commits.MoveNextAsync().AsTask();
                    pending = next;

                    while (live && await Task.WhenAny(next, Task.Delay(options.Limits.FeedHeartbeat, options.Clock, cancellationToken)).ConfigureAwait(false) != next)
                    {
                        TermLines.Write(body, framing == FeedFraming.EventStream ? ":\n\n"u8 : "#\n"u8);
                        await body.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (!await next.ConfigureAwait(false))
                    {
                        return;
                    }

                    Commit commit = commits.Current;

                    if (range.To is Position end && commit.Position > end)
                    {
                        return;
                    }

                    // The caller's scope (ADR 0107): a data commit's delta cut
                    // to the readable graphs, dropped when nothing is left; a
                    // settings or erasure commit for an admin alone. The next
                    // record delivered carries its true position, so a client
                    // resumes as before (change-feed.md §8).
                    QuadDelta delta = scope.Apply(storeFilters ? commit.Delta : filter.Apply(commit), commit.TryExternalise);
                    bool deliver = commit.Kind == CommitKind.Data ? !delta.IsEmpty : admin;

                    if (deliver)
                    {
                        record.ResetWrittenCount();
                        DeltaLines.WriteCommit(record, commit, delta, commit.TryExternalise);
                        written += Frame(body, framing, commit.Position, record.WrittenSpan);
                        last = commit.Position;

                        if (!live && written > options.Limits.ResultSizeCap.Value)
                        {
                            throw new ReadLimitExceededException();
                        }

                        if (live || written >= ResponseOutput.Threshold)
                        {
                            await body.FlushAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }

                    if (range.To is Position to && commit.Position >= to)
                    {
                        return;
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
            // The client left; nothing to say and no one to say it to.
        }
        catch (OperationCanceledException) when (options.Stopping.IsCancellationRequested)
        {
            await EndAsync(body, framing, ProblemType.ShuttingDown, last).ConfigureAwait(false);
        }
        catch (Exception cut) when (cut is OperationCanceledException or ReadLimitExceededException)
        {
            await EndAsync(body, framing, ProblemType.ReadLimitExceeded, last).ConfigureAwait(false);
        }
    }

    // A cut stream's last record (ADR 0095): an error record, or in SSE a
    // shutdown or error event naming the last position delivered.
    private static async Task EndAsync(PipeWriter body, FeedFraming framing, ProblemType problem, Position last)
    {
        ArrayBufferWriter<byte> record = new(256);
        DeltaLines.WriteError(record, problem);

        if (framing == FeedFraming.EventStream)
        {
            DeltaLines.WriteEvent(body, problem == ProblemType.ShuttingDown ? "shutdown"u8 : "error"u8, last, record.WrittenSpan);
        }
        else
        {
            TermLines.Write(body, record.WrittenSpan);
        }

        await body.FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static long Frame(PipeWriter body, FeedFraming framing, Position position, ReadOnlySpan<byte> record)
    {
        long before = body.UnflushedBytes;

        if (framing == FeedFraming.EventStream)
        {
            DeltaLines.WriteEvent(body, "commit"u8, position, record);
        }
        else
        {
            TermLines.Write(body, record);
        }

        return body.UnflushedBytes - before;
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
