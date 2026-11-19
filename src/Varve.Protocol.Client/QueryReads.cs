// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Delta;
using Varve.Delta.Model;
using Varve.Protocol.Client.Model;
using Varve.Rdf;
using Varve.Sparql.Results;
using Varve.Turtle;

namespace Varve.Protocol.Client;

public sealed partial class SparqlHttpClient
{
    private const string Sd = "http://www.w3.org/ns/sparql-service-description#";
    private const string ReportGraphPrefix = "https://w3id.org/varve/ns#validation/";

    /// <summary>
    /// A SELECT or a graph query, streamed (ADRs 0203 and 0249): the answer's
    /// head now, its solutions or statements as they arrive. SELECT is asked
    /// for as SPARQL 1.1 TSV and a graph as N-Triples. A problem before the
    /// body is the result's problem; dispose the answer to end the request.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="form"/> is <see cref="QueryForm.Boolean"/>: use <see cref="AskAsync"/>.</exception>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout.</exception>
    public async Task<VarveResult<QueryAnswer>> StartQueryAsync(string query, QueryForm form, QueryReadOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        string accept = form switch
        {
            QueryForm.Solutions => "text/tab-separated-values",
            QueryForm.Graph => "application/n-triples",
            _ => throw new ArgumentException("An ASK query is read with AskAsync.", nameof(form)),
        };

        HttpResponseMessage response = await QueryAsync(query, accept, options.AsOf.HeaderValue, options.Entailment?.Value, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            using (response)
            {
                (ResponseFacts facts, byte[] body) = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
                Problem? problem = Problem.Parse(body, facts.Status);
                return new VarveResult<QueryAnswer>(null, problem, facts.With(problem));
            }
        }

        QueryAnswer answer = await QueryAnswer.StartAsync(response, form, cancellationToken).ConfigureAwait(false);
        return new VarveResult<QueryAnswer>(answer, null, answer.Facts);
    }

    /// <summary>An ASK query's answer, read whole as SPARQL results JSON.</summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    /// <exception cref="FormatException">The body is not a boolean answer.</exception>
    public async Task<VarveResult<AskAnswer>> AskAsync(string query, QueryReadOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        using HttpResponseMessage response = await QueryAsync(query, "application/sparql-results+json", options.AsOf.HeaderValue, options.Entailment?.Value, cancellationToken).ConfigureAwait(false);
        (ResponseFacts facts, byte[] body) = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            Problem? problem = Problem.Parse(body, facts.Status);
            return new VarveResult<AskAnswer>(null, problem, facts.With(problem));
        }

        SparqlResultsReader reader = new(body, SparqlResultsFormat.Json);

        if (!reader.ReadHead() || !reader.IsBoolean)
        {
            throw new FormatException("The answer is not a SPARQL results boolean.");
        }

        Entailment? echoed = response.Headers.TryGetValues("Varve-Entailment", out IEnumerable<string>? values) && string.Join(",", values) is { Length: > 0 } value ? new Entailment(value) : null;
        return new VarveResult<AskAnswer>(new AskAnswer(reader.Boolean ? AskResult.True : AskResult.False, echoed), null, facts);
    }

    /// <summary>
    /// A query's whole answer in <paramref name="accept"/>, copied to
    /// <paramref name="destination"/> as it arrives and never held whole: the
    /// download a reader pins to the position its view read (ADR 0249).
    /// </summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout.</exception>
    public async Task<VarveResult<Download>> DownloadAsync(string query, string accept, QueryReadOptions options, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(destination);
        using HttpResponseMessage response = await QueryAsync(query, accept, options.AsOf.HeaderValue, options.Entailment?.Value, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            (ResponseFacts facts, byte[] body) = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            Problem? problem = Problem.Parse(body, facts.Status);
            return new VarveResult<Download>(null, problem, facts.With(problem));
        }

        await response.Content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        return new VarveResult<Download>(new Download(response.Content.Headers.ContentType), null, ResponseFacts.Of(response));
    }

    /// <summary>
    /// The diff between two positions or times (change feed §5, ADR 0097):
    /// one record of the changes after <paramref name="from"/> up to
    /// <paramref name="to"/>, as the caller's grants and rows let it read them
    /// (ADR 0237 point 4). <see cref="AsOfSelector.Head"/> is the start of the
    /// log as <paramref name="from"/> and the head as <paramref name="to"/>.
    /// </summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    /// <exception cref="ChangeFeedFormatException">The body is not the line format.</exception>
    public async Task<VarveResult<FeedRecord>> ReadDiffAsync(AsOfSelector from, AsOfSelector to, CancellationToken cancellationToken)
    {
        StringBuilder address = new("diff");
        char separator = '?';
        Bound(from, "from");
        Bound(to, "to");

        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(Dataset, address.ToString()));
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.varve.delta");
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        (ResponseFacts facts, byte[] body) = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            Problem? problem = Problem.Parse(body, facts.Status);
            return new VarveResult<FeedRecord>(null, problem, facts.With(problem));
        }

        List<FeedRecord> records = await RecordsAsync(body, cancellationToken).ConfigureAwait(false);
        return new VarveResult<FeedRecord>(records.Count == 1 ? records[0] : null, null, facts);

        void Bound(AsOfSelector selector, string name)
        {
            if (selector.Position is FeedPosition position)
            {
                address.Append(separator).Append(name).Append('=').Append(position.Value.ToString(CultureInfo.InvariantCulture));
                separator = '&';
            }
            else if (selector.Time is AsOfTime time)
            {
                address.Append(separator).Append(name).Append("Time=").Append(Uri.EscapeDataString(time.ToString()));
                separator = '&';
            }
        }
    }

    /// <summary>
    /// The dataset's service description (SPARQL 1.1 Service Description;
    /// ADRs 0192 point 5, 0237 point 3), read as N-Triples: the graphs the
    /// caller reads, the regimes, the as-of limit and the report graphs.
    /// </summary>
    /// <exception cref="HttpRequestException">No answer within the limits' timeout, or a body over their cap.</exception>
    /// <exception cref="FormatException">The body is not N-Triples.</exception>
    public async Task<VarveResult<DatasetDescription>> ServiceDescriptionAsync(CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, Dataset);
        request.Headers.TryAddWithoutValidation("Accept", "application/n-triples");
        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        (ResponseFacts facts, byte[] body) = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            Problem? problem = Problem.Parse(body, facts.Status);
            return new VarveResult<DatasetDescription>(null, problem, facts.With(problem));
        }

        return new VarveResult<DatasetDescription>(Describe(body), null, facts);
    }

    // The description's statements, read into what a reader uses. Graphs keep
    // the order the description lists them in.
    private static DatasetDescription Describe(byte[] body)
    {
        List<(RdfTerm S, RdfTerm P, RdfTerm O)> statements = [];
        ParseResult parsed = NQuadsParser.Parse(
            body,
            (in QuadView quad) => statements.Add((Terms.Own(quad.Subject), Terms.Own(quad.Predicate), Terms.Own(quad.Object))),
            new ParseOptions { Syntax = RdfSyntax.NTriples });

        if (!parsed.Succeeded)
        {
            throw new FormatException("The service description is not N-Triples: " + parsed.FirstError);
        }

        static RdfTerm Iri(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));
        RdfTerm name = Iri(Sd + "name");
        RdfTerm regime = Iri(Sd + "entailmentRegime");
        List<RdfTerm> graphs = [];
        Dictionary<RdfTerm, RdfTerm> nameOf = new(RdfTerm.Comparer);

        foreach ((RdfTerm s, RdfTerm p, RdfTerm o) in statements.Where(t => t.P.Equals(name)))
        {
            graphs.Add(o);
            nameOf[s] = o;
        }

        Dictionary<RdfTerm, IReadOnlyList<RdfTerm>> regimes = new(RdfTerm.Comparer);

        foreach (IGrouping<RdfTerm, (RdfTerm S, RdfTerm P, RdfTerm O)> group in statements.Where(t => t.P.Equals(regime) && nameOf.ContainsKey(t.S)).GroupBy(t => nameOf[t.S], RdfTerm.Comparer))
        {
            regimes[group.Key] = [.. group.Select(t => t.O)];
        }

        RdfTerm? defaultRegime = statements.FirstOrDefault(t => t.P.Equals(Iri(Sd + "defaultEntailmentRegime"))).O;
        RdfTerm? distance = statements.FirstOrDefault(t => t.P.Equals(Iri("https://w3id.org/varve/ns#maxAsOfDistance"))).O;
        Dictionary<RdfTerm, RdfTerm> reports = new(RdfTerm.Comparer);

        foreach (RdfTerm graph in graphs)
        {
            string iri = Encoding.UTF8.GetString(graph.Lexical);

            if (graph.Kind == RdfTermKind.Iri && iri.StartsWith(ReportGraphPrefix, StringComparison.Ordinal))
            {
                reports[Iri(Uri.UnescapeDataString(iri[ReportGraphPrefix.Length..]))] = graph;
            }
        }

        return new DatasetDescription(
            graphs,
            defaultRegime,
            regimes,
            distance is not null && long.TryParse(distance.Lexical, NumberStyles.None, CultureInfo.InvariantCulture, out long limit) ? new AsOfDistance(limit) : null,
            reports);
    }
}
