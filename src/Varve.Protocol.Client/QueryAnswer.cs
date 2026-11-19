// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DecisionDriven;
using DecisionDriven.Ledger.Varve;
using Varve.Protocol.Client.Model;
using Varve.Rdf;
using Varve.Sparql.Results;
using Varve.Turtle;

namespace Varve.Protocol.Client;

/// <summary>
/// A query's answer as it streams (ADR 0203, amended 2026-11-19): what the
/// response's head says, then its solutions or statements in batches as they
/// arrive. Disposing it closes the response, which the server sees as the
/// client ending the request; that is how a reader stops at its ceiling or
/// cancels.
/// </summary>
/// <remarks>
/// SELECT is read as SPARQL 1.1 TSV and a graph as N-Triples, both line
/// formats: a line is a solution or a statement, because both escape a line
/// feed inside a term, so the body is cut into whole lines and each batch
/// parsed alone (ADR 0249).
/// </remarks>
[Contract(typeof(TypedReadsInTheProtocolClient.StreamedAnswer), Role = "a streamed query answer")]
public sealed class QueryAnswer : IAsyncDisposable
{
    private readonly HttpResponseMessage _response;
    private readonly Stream _body;
    private readonly IAsyncEnumerator<ReadOnlyMemory<byte>> _lines;
    private readonly byte[] _header;
    private readonly ReadOnlyMemory<byte> _first;
    private bool _started;

    private QueryAnswer(HttpResponseMessage response, Stream body, IAsyncEnumerator<ReadOnlyMemory<byte>> lines, QueryForm form, byte[] header, ReadOnlyMemory<byte> first, IReadOnlyList<VariableName> variables)
    {
        _response = response;
        _body = body;
        _lines = lines;
        Form = form;
        _header = header;
        _first = first;
        Variables = variables;
        Facts = ResponseFacts.Of(response);
        Entailment = response.Headers.TryGetValues("Varve-Entailment", out IEnumerable<string>? values) && string.Join(",", values) is { Length: > 0 } echoed ? new Entailment(echoed) : null;
        CommitTime = response.Content.Headers.LastModified is DateTimeOffset modified ? new CommitTime(modified) : null;
    }

    /// <summary>The response's status, position and request id.</summary>
    public ResponseFacts Facts { get; }

    /// <summary>Solutions or a graph.</summary>
    public QueryForm Form { get; }

    /// <summary>The regime that answered, as <c>Varve-Entailment</c> echoed it.</summary>
    public Entailment? Entailment { get; }

    /// <summary>The commit time of the position read, to the second (<c>Last-Modified</c>).</summary>
    public CommitTime? CommitTime { get; }

    /// <summary>A SELECT's variables, in its order, without their <c>?</c>.</summary>
    public IReadOnlyList<VariableName> Variables { get; }

    /// <summary>How the answer ended, once read.</summary>
    public QueryEnd End { get; private set; } = QueryEnd.Reading;

    internal static async Task<QueryAnswer> StartAsync(HttpResponseMessage response, QueryForm form, CancellationToken cancellationToken)
    {
        Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        IAsyncEnumerator<ReadOnlyMemory<byte>> lines = LineBatches.ReadAsync(body, CancellationToken.None).GetAsyncEnumerator(cancellationToken);

        if (form != QueryForm.Solutions)
        {
            return new QueryAnswer(response, body, lines, form, [], ReadOnlyMemory<byte>.Empty, []);
        }

        // The TSV header is the first line; every batch is parsed with it in front.
        ReadOnlyMemory<byte> first = await lines.MoveNextAsync().ConfigureAwait(false) ? lines.Current : ReadOnlyMemory<byte>.Empty;
        int end = first.Span.IndexOf((byte)'\n') + 1;
        byte[] header = end > 0 ? first[..end].ToArray() : first.ToArray();
        List<VariableName> variables = [];

        foreach (string name in Encoding.UTF8.GetString(header).TrimEnd('\n', '\r').Split('\t', StringSplitOptions.RemoveEmptyEntries))
        {
            variables.Add(new VariableName(name.TrimStart('?', '$')));
        }

        return new QueryAnswer(response, body, lines, form, header, end > 0 ? first[end..] : ReadOnlyMemory<byte>.Empty, variables);
    }

    /// <summary>The solutions, in batches as they arrive.</summary>
    /// <exception cref="InvalidOperationException">The answer is a graph, or is already being read.</exception>
    public async IAsyncEnumerable<IReadOnlyList<Solution>> ReadSolutionsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Start(QueryForm.Solutions);

        if (!_first.IsEmpty)
        {
            yield return Solutions(_first);
        }

        while (await NextAsync(cancellationToken).ConfigureAwait(false) is ReadOnlyMemory<byte> batch)
        {
            yield return Solutions(batch);
        }
    }

    /// <summary>The statements, in batches as they arrive.</summary>
    /// <exception cref="InvalidOperationException">The answer is solutions, or is already being read.</exception>
    public async IAsyncEnumerable<IReadOnlyList<Statement>> ReadStatementsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Start(QueryForm.Graph);

        while (await NextAsync(cancellationToken).ConfigureAwait(false) is ReadOnlyMemory<byte> batch)
        {
            yield return Statements(batch);
        }
    }

    /// <summary>Closes the response: a reader done before the end has closed it.</summary>
    public async ValueTask DisposeAsync()
    {
        if (End == QueryEnd.Reading)
        {
            End = QueryEnd.Closed;
        }

        await _lines.DisposeAsync().ConfigureAwait(false);
        await _body.DisposeAsync().ConfigureAwait(false);
        _response.Dispose();
    }

    private void Start(QueryForm form)
    {
        if (Form != form)
        {
            throw new InvalidOperationException("This answer is " + Form + ", not " + form + ".");
        }

        if (_started)
        {
            throw new InvalidOperationException("An answer is read once.");
        }

        _started = true;
    }

    // The next batch; null at the end. A body that breaks before its end is a
    // cut (#351), never an exception: the reader shows what it has, and says so.
    private async Task<ReadOnlyMemory<byte>?> NextAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await _lines.MoveNextAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _lines.Current;
            }

            End = QueryEnd.Complete;
            return null;
        }
        catch (Exception broken) when (broken is IOException or HttpRequestException && !cancellationToken.IsCancellationRequested)
        {
            End = QueryEnd.Cut;
            return null;
        }
    }

    private List<Solution> Solutions(ReadOnlyMemory<byte> lines)
    {
        byte[] document = new byte[_header.Length + lines.Length];
        _header.CopyTo(document, 0);
        lines.Span.CopyTo(document.AsSpan(_header.Length));
        SparqlResultsReader reader = new(document, SparqlResultsFormat.Tsv);
        List<Solution> solutions = [];

        if (!reader.ReadHead())
        {
            throw new FormatException("The answer is not SPARQL 1.1 TSV: " + reader.Error);
        }

        while (reader.Read())
        {
            SolutionView row = reader.Current;
            RdfTerm?[] values = new RdfTerm?[Variables.Count];

            for (int i = 0; i < values.Length; i++)
            {
                values[i] = row.TryGet(i, out RdfTermView term) ? Terms.Own(term) : null;
            }

            solutions.Add(new Solution(values));
        }

        return solutions;
    }

    private static List<Statement> Statements(ReadOnlyMemory<byte> lines)
    {
        List<Statement> statements = [];
        ParseResult result = NQuadsParser.Parse(
            lines.Span,
            (in QuadView quad) => statements.Add(new Statement(Terms.Own(quad.Subject), Terms.Own(quad.Predicate), Terms.Own(quad.Object))),
            new ParseOptions { Syntax = RdfSyntax.NTriples });

        return result.Succeeded ? statements : throw new FormatException("The answer is not N-Triples: " + result.FirstError);
    }
}
