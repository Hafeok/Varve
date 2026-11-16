// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Varve.Delta;
using Varve.Delta.Model;
using Varve.Protocol.Client.Model;

namespace Varve.Protocol.Client;

public sealed partial class SparqlHttpClient
{
    /// <summary>
    /// The commits after <paramref name="from"/> up to <paramref name="to"/>,
    /// read whole from the line format (ADRs 0097, 0118, 0203): each commit's
    /// header and its changes, and where the next page starts when the server
    /// paged the range.
    /// </summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    /// <exception cref="ChangeFeedFormatException">The body is not the line format.</exception>
    public async Task<VarveResult<CommitRange>> ReadCommitsAsync(FeedPosition from, FeedPosition to, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await CommitsAsync(from.Value, to.Value, null, eventStream: false, cancellationToken).ConfigureAwait(false);
        (ResponseFacts facts, byte[] body) = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            Problem? problem = Problem.Parse(body, facts.Status);
            return new VarveResult<CommitRange>(null, problem, facts.With(problem));
        }

        List<FeedRecord> records = await RecordsAsync(body, cancellationToken).ConfigureAwait(false);
        return new VarveResult<CommitRange>(new CommitRange(records, NextPage(response) is long next ? new FeedPosition(next) : null), null, facts);
    }

    /// <summary>One commit, its header and its changes (ADR 0118).</summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    /// <exception cref="ChangeFeedFormatException">The body is not the line format.</exception>
    public async Task<VarveResult<FeedRecord>> ReadCommitAsync(FeedPosition position, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await CommitAsync(position.Value, cancellationToken).ConfigureAwait(false);
        (ResponseFacts facts, byte[] body) = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            Problem? problem = Problem.Parse(body, facts.Status);
            return new VarveResult<FeedRecord>(null, problem, facts.With(problem));
        }

        List<FeedRecord> records = await RecordsAsync(body, cancellationToken).ConfigureAwait(false);
        return new VarveResult<FeedRecord>(records.Count == 1 ? records[0] : null, null, facts);
    }

    private async Task<(ResponseFacts Facts, byte[] Body)> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ResponseFacts facts = ResponseFacts.Of(response);
        (byte[]? body, string? failure) = await ResponseBodies.ReadAsync(response.Content, _limits.MaxResponseBytes, cancellationToken).ConfigureAwait(false);
        return body is null ? throw new HttpRequestException(failure) : (facts, body);
    }

    private static async Task<List<FeedRecord>> RecordsAsync(byte[] body, CancellationToken cancellationToken)
    {
        List<FeedRecord> records = [];
        using MemoryStream stream = new(body, writable: false);

        await foreach (FeedRecord record in ChangeFeedReader.ReadAllAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            records.Add(record);
        }

        return records;
    }
}
