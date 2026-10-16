// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Varve.Delta;
using Varve.Delta.Model;

namespace Varve.Protocol.Client;

public sealed partial class SparqlHttpClient
{
    /// <summary>
    /// Follows the dataset's commits from <paramref name="from"/> (exclusive), or
    /// from the position <paramref name="checkpoint"/> holds when it holds one,
    /// handing each commit to <paramref name="handler"/> in position order and
    /// saving its position to <paramref name="checkpoint"/> only after the
    /// handler returns (ADR 0129). It runs until <paramref name="cancellationToken"/>
    /// is cancelled, and throws <see cref="OperationCanceledException"/> then.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>At least once.</b> A crash, or a cancellation, between the handler's
    /// return and the checkpoint's save hands the same commit to the handler
    /// again on the next follow. A handler must be idempotent by position; a
    /// consumer that saves the position in the same transaction as its effect
    /// gets exactly once in effect. Without a crash, each commit is handed over
    /// once per checkpoint advance, in order.
    /// </para>
    /// <para>
    /// History is read as ranges of the delta format up to the head a read
    /// reports, page by page through <c>Link: rel="next"</c> (ADR 0114); then
    /// the live tail, as server-sent events resumed by <c>Last-Event-ID</c>. A
    /// dropped connection, a <c>shutdown</c> or <c>error</c> event, <c>429</c>,
    /// <c>503</c> or another <c>5xx</c>, and a tail silent past
    /// <see cref="FollowOptions.IdleTimeout"/> are waited out and resumed. Any
    /// other refusal ends the follow with <see cref="FollowRefusedException"/>,
    /// and a body that is not the format with <see cref="ChangeFeedFormatException"/>.
    /// </para>
    /// <para>
    /// Commits the caller cannot read are not delivered (ADR 0107), so the
    /// positions handed over may have gaps; the checkpoint holds the last one
    /// handled, and resuming reads the gap again, which the server drops again.
    /// Settings and erasure commits reach only a dataset administrator.
    /// </para>
    /// </remarks>
    public async Task FollowAsync(FeedPosition from, Func<FeedRecord, CancellationToken, ValueTask> handler, IFollowCheckpoint checkpoint, FollowOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(options);

        await RunAsync(
            from,
            checkpoint,
            options,
            (cursor, token) => CatchUpAsync(cursor, handler, checkpoint, options, token),
            (cursor, delivered, token) => TailAsync(cursor, handler, checkpoint, options, delivered, token),
            cancellationToken).ConfigureAwait(false);
    }

    // The loop FollowAsync and FollowEventsAsync share (ADR 0129): from the
    // checkpoint or the start, history then the live tail, transient failures
    // waited out with backoff, the caller's own failures thrown.
    private static async Task RunAsync(
        FeedPosition from,
        IFollowCheckpoint checkpoint,
        FollowOptions options,
        Func<FeedPosition, CancellationToken, Task<FeedPosition>> catchUp,
        Func<FeedPosition, Action, CancellationToken, Task<FeedPosition>> tail,
        CancellationToken cancellationToken)
    {
        FeedPosition cursor = await checkpoint.LoadAsync(cancellationToken).ConfigureAwait(false) ?? from;
        int failures = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                cursor = await catchUp(cursor, cancellationToken).ConfigureAwait(false);
                failures = 0;
                cursor = await tail(cursor, () => failures = 0, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (Transient(failure, cancellationToken))
            {
                // Ridden out: waited, then resumed where the cursor stands.
            }
            catch (CallerFailedException failure)
            {
                // The handler's or the checkpoint's own failure is the caller's,
                // never waited out: a retry would hand the commit over again.
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.InnerException!).Throw();
            }

            await Task.Delay(Backoff(options, failures++), options.Clock, cancellationToken).ConfigureAwait(false);
        }
    }

    // History up to the head a read reports, page by page. Answers the
    // position the history was read to, which may be past the last commit
    // handled when the commits after it were not the caller's to read.
    private async Task<FeedPosition> CatchUpAsync(FeedPosition cursor, Func<FeedRecord, CancellationToken, ValueTask> handler, IFollowCheckpoint checkpoint, FollowOptions options, CancellationToken cancellationToken)
    {
        long head = await HeadAsync(cancellationToken).ConfigureAwait(false);

        while (cursor.Value < head)
        {
            using HttpResponseMessage page = await CommitsAsync(cursor.Value, head, options.Graph, eventStream: false, cancellationToken).ConfigureAwait(false);
            await EnsureFollowableAsync(page, cancellationToken).ConfigureAwait(false);
            long end = NextPage(page) ?? head;
            Stream body = await page.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            await foreach (FeedRecord record in ChangeFeedReader.ReadAllAsync(body, cancellationToken).ConfigureAwait(false))
            {
                if (record.Kind == FeedRecordKind.Error)
                {
                    // A cut range (ADR 0095): what was delivered stands.
                    throw new FollowInterruptedException(record.Problem.Value);
                }

                if (record.Kind == FeedRecordKind.Commit && record.Position > cursor)
                {
                    await DeliverAsync(record, handler, checkpoint, cancellationToken).ConfigureAwait(false);
                    cursor = record.Position;
                }
            }

            if (end > cursor.Value)
            {
                cursor = new FeedPosition(end);
            }
        }

        return cursor;
    }

    // The live tail from the cursor, until the stream ends or goes silent.
    private async Task<FeedPosition> TailAsync(FeedPosition cursor, Func<FeedRecord, CancellationToken, ValueTask> handler, IFollowCheckpoint checkpoint, FollowOptions options, Action delivered, CancellationToken cancellationToken)
    {
        StringBuilder query = new("commits?from=");
        query.Append(cursor.Value.ToString(CultureInfo.InvariantCulture));

        if (options.Graph is not null)
        {
            query.Append("&graph=").Append(Uri.EscapeDataString(options.Graph));
        }

        HttpRequestMessage request = new(HttpMethod.Get, new Uri(Dataset, query.ToString()));
        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
        request.Headers.TryAddWithoutValidation("Last-Event-ID", cursor.Value.ToString(CultureInfo.InvariantCulture));
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureFollowableAsync(response, cancellationToken).ConfigureAwait(false);

        using CancellationTokenSource idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using ServerSentEvents events = new(body);

        try
        {
            while (true)
            {
                idle.CancelAfter(options.IdleTimeout);
                ServerSentEvent? next = await events.NextAsync(idle.Token).ConfigureAwait(false);

                if (next is not ServerSentEvent message)
                {
                    return cursor;
                }

                if (message.Name is "shutdown" or "error")
                {
                    // A cut tail (ADR 0095): its id is the last position delivered.
                    throw new FollowInterruptedException(message.Name);
                }

                if (message.Name != "commit" || message.Data.Length == 0)
                {
                    continue;
                }

                FeedRecord record = Record(message.Data);

                if (record.Kind == FeedRecordKind.Commit && record.Position > cursor)
                {
                    await DeliverAsync(record, handler, checkpoint, cancellationToken).ConfigureAwait(false);
                    cursor = record.Position;
                    delivered();
                }
            }
        }
        catch (OperationCanceledException) when (idle.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new FollowInterruptedException("the live tail was silent for " + options.IdleTimeout.ToString() + ", heartbeat included");
        }
    }

    // One commit to the handler, then its position to the checkpoint; what
    // either throws is the caller's, carried past the transient filter.
    private static async Task DeliverAsync(FeedRecord record, Func<FeedRecord, CancellationToken, ValueTask> handler, IFollowCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        try
        {
            await handler(record, cancellationToken).ConfigureAwait(false);
            await checkpoint.SaveAsync(record.Position, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new CallerFailedException(failure);
        }
    }

    // The head, as a read reports it: Varve-Position on any answer. A query
    // needs read alone, where the status endpoint needs admin (ADR 0106).
    private async Task<long> HeadAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await QueryAsync("ASK {}", "application/sparql-results+json", null, cancellationToken).ConfigureAwait(false);
        await EnsureFollowableAsync(response, cancellationToken).ConfigureAwait(false);
        return PositionOf(response) ?? throw new FollowRefusedException((int)response.StatusCode, "the answer carries no Varve-Position; this is not a Varve dataset");
    }

    private static async Task EnsureFollowableAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        int status = (int)response.StatusCode;

        if (status is >= 200 and < 300)
        {
            return;
        }

        string problem = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable || status >= 500)
        {
            throw new FollowInterruptedException(status.ToString(CultureInfo.InvariantCulture) + " " + problem);
        }

        throw new FollowRefusedException(status, problem);
    }

    // The next page's start, from Link: <…?from=n…>; rel="next".
    private static long? NextPage(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out IEnumerable<string>? links))
        {
            return null;
        }

        foreach (string link in links)
        {
            Match next = NextLink().Match(link);

            if (next.Success && long.TryParse(next.Groups["from"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long from))
            {
                return from;
            }
        }

        return null;
    }

    private static FeedRecord Record(string data)
    {
        ReadOnlySequence<byte> bytes = new(Encoding.UTF8.GetBytes(data + "\n\n"));
        ChangeFeedReader reader = new();
        return reader.TryRead(ref bytes, isFinalBlock: true, out FeedRecord? record)
            ? record
            : throw new ChangeFeedFormatException("A commit event carries no record.");
    }

    // A dropped connection, a cut stream, a busy server or a request timeout:
    // never the caller's own cancellation.
    private static bool Transient(Exception failure, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && failure is FollowInterruptedException or HttpRequestException or IOException or OperationCanceledException;

    private static TimeSpan Backoff(FollowOptions options, int failures)
    {
        double factor = Math.Pow(2, Math.Min(failures, 16));
        TimeSpan wait = TimeSpan.FromTicks((long)Math.Min(options.FirstBackoff.Ticks * factor, options.MaxBackoff.Ticks));

        if (options.Jitter is Func<double> jitter)
        {
            wait = TimeSpan.FromTicks((long)(wait.Ticks * (0.5 + (Math.Clamp(jitter(), 0, 1) / 2))));
        }

        return wait;
    }

    [GeneratedRegex(@"<[^>]*[?&]from=(?<from>\d+)[^>]*>\s*;\s*rel=""?next""?", RegexOptions.CultureInvariant)]
    private static partial Regex NextLink();

    // A failure of the handler or the checkpoint, on its way out.
    private sealed class CallerFailedException(Exception inner) : Exception(inner.Message, inner)
    {
    }

    // A failure the follow waits out and resumes from.
    private sealed class FollowInterruptedException(string reason) : Exception("The follow was interrupted: " + reason)
    {
    }
}
