// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Varve.Delta.Model;
using Varve.Protocol.Client;
using Varve.Protocol.Client.Model;

namespace Varve.EventHubs.Sample;

/// <summary>
/// Publishes a dataset's rendered events to an event hub (ADRs 0143, 0146).
/// </summary>
/// <remarks>
/// <para>
/// Each event goes as it was received: the structured CloudEvent's canonical
/// bytes are the body, <c>application/cloudevents+json</c> its content type,
/// and the event's <c>id</c> its message id, so that a consumer can drop a
/// duplicate by it. Events that could not be rendered go too, in their places:
/// they are CloudEvents whose type is the problem's (ADR 0143), and a consumer
/// that sees one knows an event is missing and why.
/// </para>
/// <para>
/// Every event carries one partition key, the dataset's name, so every event
/// of the dataset lands in one partition, where Event Hubs keeps the order it
/// was sent in. A commit's events are sent before the commit's position is
/// saved (<see cref="SparqlHttpClient.FollowEventsAsync"/> saves it after the
/// handler returns), so a bridge stopped between the two sends the commit
/// again when it resumes: at least once, in order, never a gap.
/// </para>
/// </remarks>
public sealed class EventBridge
{
    /// <summary>The structured CloudEvents content type (CloudEvents' AMQP binding).</summary>
    public const string ContentType = "application/cloudevents+json";

    private readonly SparqlHttpClient _varve;
    private readonly EventHubProducerClient _producer;
    private readonly CreateBatchOptions _batch;

    /// <summary>A bridge from <paramref name="varve"/>'s dataset to <paramref name="producer"/>'s hub.</summary>
    public EventBridge(SparqlHttpClient varve, EventHubProducerClient producer, string partitionKey)
    {
        ArgumentNullException.ThrowIfNull(varve);
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentException.ThrowIfNullOrEmpty(partitionKey);
        _varve = varve;
        _producer = producer;
        _batch = new CreateBatchOptions { PartitionKey = partitionKey };
    }

    /// <summary>
    /// Follows the dataset from the position <paramref name="checkpoint"/>
    /// holds, or from its start, until <paramref name="cancellationToken"/>
    /// is cancelled.
    /// </summary>
    public Task RunAsync(IFollowCheckpoint checkpoint, FollowOptions options, CancellationToken cancellationToken) =>
        _varve.FollowEventsAsync(new FeedPosition(0), ForwardAsync, checkpoint, options, cancellationToken);

    /// <summary>Sends one commit's events, in order, in as few batches as they fit.</summary>
    private async ValueTask ForwardAsync(CommitEvents commit, CancellationToken cancellationToken)
    {
        EventDataBatch batch = await _producer.CreateBatchAsync(_batch, cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (ReceivedEvent received in commit.Events)
            {
                EventData data = new(received.Json)
                {
                    ContentType = ContentType,
                    MessageId = received.Id,
                };

                if (batch.TryAdd(data))
                {
                    continue;
                }

                if (batch.Count == 0)
                {
                    throw new InvalidOperationException("The event " + received.Id + " is larger than the hub's largest message.");
                }

                await _producer.SendAsync(batch, cancellationToken).ConfigureAwait(false);
                batch.Dispose();
                batch = await _producer.CreateBatchAsync(_batch, cancellationToken).ConfigureAwait(false);

                if (!batch.TryAdd(data))
                {
                    throw new InvalidOperationException("The event " + received.Id + " is larger than the hub's largest message.");
                }
            }

            if (batch.Count > 0)
            {
                await _producer.SendAsync(batch, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            batch.Dispose();
        }
    }
}
