// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Delta.Model;
using Varve.Protocol.Client.Model;
using Varve.Store;
using Xunit;

namespace Varve.Protocol.Client.Tests;

/// <summary>
/// The explorer's reads (ADRs 0203 and 0249): a streamed SELECT and graph
/// answer, an ASK, a download, the diff and the service description, against
/// a real protocol host; and the line batching that streaming rests on, held
/// to the chunk-boundary oracle (docs/testing.md §2).
/// </summary>
public class QueryReadTests
{
    [Fact]
    public async Task a_select_streams_its_variables_and_solutions_at_the_position_read()
    {
        await using Dataset dataset = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(dataset);
        using HttpClient http = new();
        SparqlHttpClient client = new(http, new Uri(host.Address + "datasets/d/"), ClientLimits.Default);

        VarveResult<QueryAnswer> started = await client.StartQueryAsync(
            "SELECT ?o ?missing WHERE { <http://ex/s> <http://ex/p> ?o OPTIONAL { ?o <http://ex/q> ?missing } } ORDER BY ?o",
            QueryForm.Solutions,
            new QueryReadOptions(AsOfSelector.At(new FeedPosition(1))),
            C.Ct);

        await using QueryAnswer answer = started.Value!;
        Assert.Equal([new VariableName("o"), new VariableName("missing")], answer.Variables);
        Assert.Equal(new FeedPosition(1), answer.Facts.Position);
        Assert.Equal(Entailment.Simple, answer.Entailment);

        List<Solution> rows = [];

        await foreach (IReadOnlyList<Solution> batch in answer.ReadSolutionsAsync(C.Ct))
        {
            rows.AddRange(batch);
        }

        Assert.Equal(QueryEnd.Complete, answer.End);
        Assert.Equal(["1", "2"], rows.Select(r => Encoding.UTF8.GetString(r.Values[0]!.Lexical)));
        Assert.All(rows, r => Assert.Null(r.Values[1]));
    }

    [Fact]
    public async Task a_graph_answer_streams_its_statements()
    {
        await using Dataset dataset = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(dataset);
        using HttpClient http = new();
        SparqlHttpClient client = new(http, new Uri(host.Address + "datasets/d/"), ClientLimits.Default);

        VarveResult<QueryAnswer> started = await client.StartQueryAsync(
            "CONSTRUCT { ?s <http://ex/p> ?o } WHERE { ?s <http://ex/p> ?o }", QueryForm.Graph, new QueryReadOptions(AsOfSelector.Head), C.Ct);

        await using QueryAnswer answer = started.Value!;
        List<Statement> statements = [];

        await foreach (IReadOnlyList<Statement> batch in answer.ReadStatementsAsync(C.Ct))
        {
            statements.AddRange(batch);
        }

        Assert.Equal(QueryEnd.Complete, answer.End);
        Assert.Equal(["1", "2"], statements.Select(s => Encoding.UTF8.GetString(s.Object.Lexical)).Order());
    }

    [Fact]
    public async Task a_problem_before_the_body_is_the_results_problem()
    {
        await using Dataset dataset = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(dataset);
        using HttpClient http = new();
        SparqlHttpClient client = new(http, new Uri(host.Address + "datasets/d/"), ClientLimits.Default);

        VarveResult<QueryAnswer> refused = await client.StartQueryAsync("SELECT * WHERE {", QueryForm.Solutions, new QueryReadOptions(AsOfSelector.Head), C.Ct);
        Assert.Null(refused.Value);
        Assert.Equal(HttpStatusCode.BadRequest, refused.Facts.Status);
        Assert.EndsWith("sparql-syntax", refused.Problem!.Type.ToString(), StringComparison.Ordinal);

        VarveResult<QueryAnswer> early = await client.StartQueryAsync(
            "SELECT * WHERE { ?s ?p ?o }", QueryForm.Solutions, new QueryReadOptions(AsOfSelector.AtTime(new AsOfTime(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)))), C.Ct);
        Assert.Equal(HttpStatusCode.NotFound, early.Facts.Status);
        Assert.EndsWith("before-first-commit", early.Problem!.Type.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task an_ask_answers_a_boolean_and_its_regime()
    {
        await using Dataset dataset = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(dataset);
        using HttpClient http = new();
        SparqlHttpClient client = new(http, new Uri(host.Address + "datasets/d/"), ClientLimits.Default);

        VarveResult<AskAnswer> yes = await client.AskAsync("ASK { <http://ex/s> <http://ex/p> ?o }", new QueryReadOptions(AsOfSelector.Head), C.Ct);
        VarveResult<AskAnswer> no = await client.AskAsync("ASK { <http://ex/nothing> ?p ?o }", new QueryReadOptions(AsOfSelector.Head), C.Ct);

        Assert.Equal(AskResult.True, yes.Value!.Result);
        Assert.Equal(AskResult.False, no.Value!.Result);
        Assert.Equal(Entailment.Simple, yes.Value.Entailment);
    }

    [Fact]
    public async Task a_download_copies_the_servers_bytes_in_the_format_asked()
    {
        await using Dataset dataset = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(dataset);
        using HttpClient http = new();
        SparqlHttpClient client = new(http, new Uri(host.Address + "datasets/d/"), ClientLimits.Default);
        using MemoryStream file = new();

        VarveResult<Download> downloaded = await client.DownloadAsync("SELECT ?o WHERE { <http://ex/s> <http://ex/p> ?o } ORDER BY ?o", "text/csv", new QueryReadOptions(AsOfSelector.At(new FeedPosition(1))), file, C.Ct);

        Assert.Equal("text/csv", downloaded.Value!.MediaType!.MediaType);
        Assert.Equal("o\r\n1\r\n2\r\n", Encoding.UTF8.GetString(file.ToArray()));
    }

    [Fact]
    public async Task the_diff_against_the_head_is_one_record_of_what_changed()
    {
        await using Dataset dataset = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(dataset);
        using HttpClient http = new();
        SparqlHttpClient client = new(http, new Uri(host.Address + "datasets/d/"), ClientLimits.Default);
        using HttpResponseMessage written = await client.UpdateAsync("INSERT DATA { <http://ex/s> <http://ex/p> \"9\" }", (long?)null, C.Ct);
        Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);

        VarveResult<FeedRecord> diff = await client.ReadDiffAsync(AsOfSelector.At(new FeedPosition(1)), AsOfSelector.Head, C.Ct);

        Assert.Equal(HttpStatusCode.OK, diff.Facts.Status);
        Assert.Equal(new FeedPosition(2), diff.Facts.Position);
        Assert.NotNull(diff.Value);
    }

    [Fact]
    public async Task the_service_description_names_the_readable_graphs_and_the_as_of_limit()
    {
        await using Dataset dataset = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(dataset);
        using HttpClient http = new();
        SparqlHttpClient client = new(http, new Uri(host.Address + "datasets/d/"), ClientLimits.Default);

        VarveResult<DatasetDescription> described = await client.ServiceDescriptionAsync(C.Ct);

        Assert.Contains(C.Iri("http://ex/g"), described.Value!.Graphs);
        Assert.NotNull(described.Value.MaxAsOfDistance);
        Assert.Equal(Entailment.Simple, Entailment.OfRegime(described.Value.DefaultEntailment!));
        Assert.Empty(described.Value.ReportGraphs);
    }

    [Fact]
    public async Task an_answer_closed_before_its_end_says_so_and_one_broken_mid_body_is_cut()
    {
        string body = "?o\n" + string.Concat(Enumerable.Range(0, 5000).Select(i => "\"" + i + "\"\n"));

        QueryAnswer closed = await Streamed(body, breakAfter: null);

        await foreach (IReadOnlyList<Solution> _ in closed.ReadSolutionsAsync(C.Ct))
        {
            break;
        }

        await closed.DisposeAsync();
        Assert.Equal(QueryEnd.Closed, closed.End);

        QueryAnswer cut = await Streamed(body, breakAfter: 20_000);
        int rows = 0;

        await foreach (IReadOnlyList<Solution> batch in cut.ReadSolutionsAsync(C.Ct))
        {
            rows += batch.Count;
        }

        Assert.Equal(QueryEnd.Cut, cut.End);
        Assert.InRange(rows, 1, 4999);
        await cut.DisposeAsync();
    }

    [Fact]
    public async Task line_batches_are_the_body_cut_at_line_feeds_wherever_the_reads_split_it()
    {
        byte[] body = Encoding.UTF8.GetBytes("?s\t?o\n<http://ex/a>\t\"one\\ttab\"\n<http://ex/b>\t\"line\\nfeed\"@en\n<http://ex/c>\t\"é😀\"\n<http://ex/d>\t");

        for (int split = 0; split <= body.Length; split++)
        {
            List<byte> joined = [];
            List<ReadOnlyMemory<byte>> batches = [];

            await foreach (ReadOnlyMemory<byte> batch in LineBatches.ReadAsync(new SplitStream(body, split), C.Ct))
            {
                batches.Add(batch);
                joined.AddRange(batch.ToArray());
            }

            Assert.Equal(body, joined);

            for (int i = 0; i < batches.Count - 1; i++)
            {
                Assert.Equal((byte)'\n', batches[i].Span[^1]);
            }
        }
    }

    private static async Task<QueryAnswer> Streamed(string body, int? breakAfter)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StreamContent(new SplitStream(Encoding.UTF8.GetBytes(body), 8192, breakAfter)),
        };
        return await QueryAnswer.StartAsync(response, QueryForm.Solutions, C.Ct);
    }

    // A body read in two parts split at one offset, small reads after that,
    // and, if asked, an IOException at a given byte, as a connection breaks.
    private sealed class SplitStream(byte[] bytes, int split, int? breakAfter = null) : Stream
    {
        private int _at;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => _at; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (breakAfter is int limit && _at >= limit)
            {
                throw new IOException("The connection broke.");
            }

            int end = _at < split ? split : Math.Min(bytes.Length, _at + 4096);
            int n = Math.Min(buffer.Length, Math.Min(bytes.Length, end) - _at);
            bytes.AsSpan(_at, n).CopyTo(buffer);
            _at += n;
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
