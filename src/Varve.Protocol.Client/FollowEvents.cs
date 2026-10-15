// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Varve.Delta.Model;
using Varve.Protocol.Client.Model;

namespace Varve.Protocol.Client;

public sealed partial class SparqlHttpClient
{
    /// <summary>The problem type of an event that could not be rendered (ADR 0143).</summary>
    private const string EventNotRendered = "https://w3id.org/varve/problems/event-not-rendered";

    /// <summary>
    /// Follows the dataset's rendered events from <paramref name="from"/>
    /// (exclusive), or from the position <paramref name="checkpoint"/> holds,
    /// handing each commit's events to <paramref name="handler"/> in position
    /// order and saving the commit's position only after the handler returns
    /// (ADRs 0129, 0143). It is <see cref="FollowAsync"/>'s sibling over the
    /// events resource, with the same contract: at least once, a handler
    /// idempotent by position, transient failures waited out, any other refusal
    /// ending it with <see cref="FollowRefusedException"/>.
    /// </summary>
    /// <remarks>
    /// The events arrive as the server rendered them, as canonical bytes the
    /// client never re-renders, so that a consumer forwarding them forwards
    /// exactly what was rendered. A commit with no event the caller may see is
    /// not handed over; resuming reads it again and skips it again.
    /// </remarks>
    public Task FollowEventsAsync(FeedPosition from, Func<CommitEvents, CancellationToken, ValueTask> handler, IFollowCheckpoint checkpoint, FollowOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(options);

        return RunAsync(
            from,
            checkpoint,
            options,
            (cursor, token) => CatchUpEventsAsync(cursor, handler, checkpoint, token),
            (cursor, delivered, token) => TailEventsAsync(cursor, handler, checkpoint, options, delivered, token),
            cancellationToken);
    }

    private async Task<FeedPosition> CatchUpEventsAsync(FeedPosition cursor, Func<CommitEvents, CancellationToken, ValueTask> handler, IFollowCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        long head = await HeadAsync(cancellationToken).ConfigureAwait(false);

        while (cursor.Value < head)
        {
            using HttpResponseMessage page = await EventsAsync(cursor.Value, head, type: null, eventStream: false, cancellationToken).ConfigureAwait(false);
            await EnsureFollowableAsync(page, cancellationToken).ConfigureAwait(false);
            long end = NextPage(page) ?? head;
            byte[] body = await page.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            foreach (CommitEvents commit in ByCommit(ReadBatch(body)))
            {
                if (commit.Position > cursor)
                {
                    await DeliverEventsAsync(commit, handler, checkpoint, cancellationToken).ConfigureAwait(false);
                    cursor = commit.Position;
                }
            }

            if (end > cursor.Value)
            {
                cursor = new FeedPosition(end);
            }
        }

        return cursor;
    }

    private async Task<FeedPosition> TailEventsAsync(FeedPosition cursor, Func<CommitEvents, CancellationToken, ValueTask> handler, IFollowCheckpoint checkpoint, FollowOptions options, Action delivered, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Get, new Uri(Dataset, "events?from=" + cursor.Value.ToString(CultureInfo.InvariantCulture)));
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
                    throw new FollowInterruptedException(message.Name);
                }

                if (message.Name != "events" || message.Data.Length == 0)
                {
                    continue;
                }

                foreach (CommitEvents commit in ByCommit(ReadBatch(Encoding.UTF8.GetBytes(message.Data))))
                {
                    if (commit.Position > cursor)
                    {
                        await DeliverEventsAsync(commit, handler, checkpoint, cancellationToken).ConfigureAwait(false);
                        cursor = commit.Position;
                        delivered();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (idle.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new FollowInterruptedException("the live tail was silent for " + options.IdleTimeout.ToString() + ", heartbeat included");
        }
    }

    private static async Task DeliverEventsAsync(CommitEvents commit, Func<CommitEvents, CancellationToken, ValueTask> handler, IFollowCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        try
        {
            await handler(commit, cancellationToken).ConfigureAwait(false);
            await checkpoint.SaveAsync(commit.Position, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new CallerFailedException(failure);
        }
    }

    /// <summary>A canonical CloudEvents batch, each event's own bytes cut out of it.</summary>
    internal static List<ReceivedEvent> ReadBatch(byte[] batch)
    {
        List<ReceivedEvent> events = [];
        Utf8JsonReader reader = new(batch);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
        {
            throw new FollowRefusedException(200, "the events are not a CloudEvents batch");
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            int start = (int)reader.TokenStartIndex;
            reader.Skip();
            ReadOnlyMemory<byte> bytes = batch.AsMemory(start, (int)reader.BytesConsumed - start);
            using JsonDocument cloudEvent = JsonDocument.Parse(bytes);
            JsonElement root = cloudEvent.RootElement;
            string type = root.GetProperty("type").GetString()!;
            events.Add(new ReceivedEvent(
                root.GetProperty("id").GetString()!,
                type,
                root.GetProperty("sequence").GetString()!,
                type == EventNotRendered,
                bytes));
        }

        return events;
    }

    /// <summary>Events grouped by the position their id starts with, in order.</summary>
    internal static List<CommitEvents> ByCommit(List<ReceivedEvent> events)
    {
        List<CommitEvents> commits = [];
        List<ReceivedEvent> current = [];
        long? position = null;

        foreach (ReceivedEvent received in events)
        {
            long at = long.Parse(received.Id.AsSpan(0, received.Id.IndexOf(' ', StringComparison.Ordinal)), NumberStyles.None, CultureInfo.InvariantCulture);

            if (position is long p && p != at)
            {
                commits.Add(new CommitEvents(new FeedPosition(p), current));
                current = [];
            }

            position = at;
            current.Add(received);
        }

        if (position is long last)
        {
            commits.Add(new CommitEvents(new FeedPosition(last), current));
        }

        return commits;
    }
}
