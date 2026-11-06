// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CsCheck;
using Varve.Classification;
using Varve.Protocol.Endpoints;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Entailment;
using Varve.Sparql.Entailment.Model;
using Varve.Sparql.Evaluation;
using Varve.Store;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// ADR 0238 point 1 over HTTP: under a regime tier 1 serves, a row-restricted
/// caller's answer is the rewriting evaluated over the rows it sees, so an
/// entailment whose premise is in a row it does not read is not in its
/// answer. The TBox graph is outside the classified graphs, and every caller
/// reads it.
/// </summary>
public partial class RowScopeTests
{
    private const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";

    private const string TBoxText =
        "GRAPH <http://ex/tbox> { <http://ex/ownedBy> <" + Rdfs + "subPropertyOf> <http://ex/relatedTo> . "
        + "<http://ex/knows> <" + Rdfs + "domain> <http://ex/Agent> ; <" + Rdfs + "range> <http://ex/Agent> . "
        + "<http://ex/label> <" + Rdfs + "domain> <http://ex/Named> . "
        + "<http://ex/Person> <" + Rdfs + "subClassOf> <http://ex/Agent> . }";

    private static readonly TBoxCover[] Covers = [new TBoxCover([Iri("http://ex/tbox")], GraphScope.Of([], ["http://ex/g/"], DefaultGraphAccess.Excluded))];

    private static readonly string[] EntailedQueries =
    [
        "SELECT ?g ?s ?o WHERE { GRAPH ?g { ?s <http://ex/relatedTo> ?o } }",
        "SELECT ?g ?s WHERE { GRAPH ?g { ?s a <http://ex/Agent> } }",
        "SELECT ?s WHERE { GRAPH <http://ex/g/1> { ?s a <http://ex/Named> } }",
        "SELECT ?g ?s ?c WHERE { GRAPH ?g { ?s a ?c } }",
        "CONSTRUCT { ?s a ?c } WHERE { GRAPH ?g { ?s a ?c } }",
    ];

    [Fact]
    public async Task A_row_restricted_answer_under_rdfs_equals_the_rewriting_over_the_rows_it_sees()
    {
        await using Rows rows = new();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(rows.Datasets, accessScopes: rows, rowClassifications: rows, entailment: new PerDataset(), stopping: P.Ct);

        await Gen.Select(Quad.List[0, 12], Gen.OneOfConst(EntailedQueries), Gen.Int[0, 7]).SampleAsync(async (quads, text, set) =>
        {
            (string name, Dataset dataset, RowScope scope) = await rows.AddAsync([TBoxText, .. quads], set);
            bool graph = text.StartsWith("CONSTRUCT", StringComparison.Ordinal);
            using HttpRequestMessage request = P.Query("datasets/" + name + "/sparql", text, graph ? "application/n-triples" : "application/sparql-results+json");
            request.Headers.TryAddWithoutValidation("Varve-Entailment", "rdfs");
            HttpResponseMessage response = await host.Client.SendAsync(request, P.Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("rdfs", P.Header(response, "Varve-Entailment"));

            using DatasetView view = dataset.Pin();
            IQuadSource visible = RowScopedQuadSource.Wrap(view, new ShapeClassifier([Binding]).Classify(view, QuadDelta.Empty), scope);
            EntailedQuery entailed = EntailmentRewriter.Rewrite(SparqlParser.ParseQuery(Encoding.UTF8.GetBytes(text)), visible, Covers, EntailmentRegime.Rdfs, cancellationToken: P.Ct);
            using QueryResults expected = new SparqlEvaluator(EntailmentFunctions.With(new EvaluationOptions())).Evaluate(entailed.Query, entailed.Source, P.Ct);
            Assert.Equal(Count(expected), Count(await response.Content.ReadAsStringAsync(P.Ct), text));
            rows.Remove(name);
        }, iter: Iterations);
    }

    /// <summary>
    /// The case the property generalises: a and b are data subjects, each
    /// labelled, and a label's domain is ex:Named. A caller of a's rows alone
    /// is answered that a is ex:Named and not that b is; a caller of every row
    /// is answered both.
    /// </summary>
    [Fact]
    public async Task An_entailment_from_a_row_the_caller_does_not_read_is_not_in_its_answer()
    {
        await using Rows rows = new();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(rows.Datasets, accessScopes: rows, rowClassifications: rows, entailment: new PerDataset(), stopping: P.Ct);
        const string Type = "<http://www.w3.org/1999/02/22-rdf-syntax-ns#type>";
        (string name, _, _) = await rows.AddAsync(
            [
                TBoxText,
                "GRAPH <http://ex/g/1> { <http://ex/a> " + Type + " <http://ex/Person> ; <http://ex/label> \"a\" }",
                "GRAPH <http://ex/g/1> { <http://ex/b> " + Type + " <http://ex/Person> ; <http://ex/label> \"b\" }",
            ],
            set: 1);
        const string Text = "SELECT ?s WHERE { GRAPH <http://ex/g/1> { ?s a <http://ex/Named> } }";

        string restricted = await NamedAsync(host, name, Text, "rdfs");
        Assert.Contains("http://ex/a", restricted, StringComparison.Ordinal);
        Assert.DoesNotContain("http://ex/b", restricted, StringComparison.Ordinal);

        string everything = await NamedAsync(host, rows.Alias(name), Text, "rdfs");
        Assert.Contains("http://ex/a", everything, StringComparison.Ordinal);
        Assert.Contains("http://ex/b", everything, StringComparison.Ordinal);

        // Simple entailment asserts nobody ex:Named.
        Assert.Equal(0, Count(await NamedAsync(host, rows.Alias(name), Text, "simple"), Text));
    }

    private static async Task<string> NamedAsync(ProtocolTestHost host, string dataset, string text, string regime)
    {
        using HttpRequestMessage request = P.Query("datasets/" + dataset + "/sparql", text, "application/sparql-results+json");
        request.Headers.TryAddWithoutValidation("Varve-Entailment", regime);
        HttpResponseMessage response = await host.Client.SendAsync(request, P.Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(P.Ct);
    }

    /// <summary>One set of bindings per dataset: a schema cache's keys name graphs, not the dataset they are in.</summary>
    private sealed class PerDataset : IEntailmentBindings
    {
        private readonly ConcurrentDictionary<string, EntailmentBindings> _bindings = new(StringComparer.Ordinal);

        public EntailmentBindings Of(DatasetName dataset) => _bindings.GetOrAdd(dataset.Value, static _ => new EntailmentBindings(Covers, EntailmentRegime.Simple));
    }
}
