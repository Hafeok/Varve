// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Varve.Inference.Model;
using Varve.Inference.Store;
using Varve.Inference.Store.Model;
using Varve.Protocol.Client;
using Varve.Protocol.Http;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// The tier-2 surfaces over HTTP (ADRs 0195, 0197, 0198): the views in
/// <c>FROM</c> and <c>FROM NAMED</c>, a binding's reserved graph of TBox
/// conclusions, <c>Varve-Entailment: owl2-rl</c>, and the seam's refusals
/// as problems. A read that names none of them never reads a closure.
/// </summary>
public class InferenceSurfaceTests
{
    private const string Sparql = "datasets/d/sparql";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";
    private const string Explicit = "https://w3id.org/varve/ns#explicit";
    private const string Implicit = "https://w3id.org/varve/ns#implicit";
    private const string Conclusions = "https://w3id.org/varve/ns#inferred/urn%3Ab/tbox";

    [Fact]
    public async Task FROM_varve_implicit_reads_a_graphs_entailments_and_varve_explicit_its_assertions()
    {
        await using Fixture fixture = await Fixture.StartAsync(InferenceProfile.Rdfs);

        string[] implicitClasses = await fixture.ColumnAsync("SELECT ?c FROM <http://ex/g/1> FROM <" + Implicit + "> WHERE { <http://ex/ann> a ?c }", "c");
        Assert.Contains("http://ex/Agent", implicitClasses);
        Assert.DoesNotContain("http://ex/Person", implicitClasses);

        string[] explicitClasses = await fixture.ColumnAsync("SELECT ?c FROM <http://ex/g/1> FROM <" + Explicit + "> WHERE { <http://ex/ann> a ?c }", "c");
        Assert.Equal(["http://ex/Person"], explicitClasses);

        string[] both = await fixture.ColumnAsync("SELECT ?c FROM <http://ex/g/1> FROM <" + Explicit + "> FROM <" + Implicit + "> WHERE { <http://ex/ann> a ?c }", "c");
        Assert.Contains("http://ex/Person", both);
        Assert.Contains("http://ex/Agent", both);
    }

    [Fact]
    public async Task Under_FROM_NAMED_varve_implicit_GRAPH_binds_the_real_graph()
    {
        await using Fixture fixture = await Fixture.StartAsync(InferenceProfile.Rdfs);

        string[] graphs = await fixture.ColumnAsync("SELECT ?g FROM NAMED <" + Implicit + "> WHERE { GRAPH ?g { <http://ex/ann> a <http://ex/Agent> } }", "g");
        Assert.Equal(["http://ex/g/1"], graphs);

        // A view counts as a FROM: with no FROM NAMED, there are no named graphs.
        Assert.Empty(await fixture.ColumnAsync("SELECT ?g FROM <" + Implicit + "> WHERE { GRAPH ?g { ?s ?p ?o } }", "g"));
    }

    [Fact]
    public async Task The_reserved_graph_holds_the_TBox_conclusions_by_query_and_by_graph_store()
    {
        await using Fixture fixture = await Fixture.StartAsync(InferenceProfile.Rdfs);

        string[] supers = await fixture.ColumnAsync("SELECT ?x WHERE { GRAPH <" + Conclusions + "> { <http://ex/Person> <" + Rdfs + "subClassOf> ?x } }", "x");
        Assert.Contains(Rdfs + "Resource", supers);
        Assert.Contains("http://ex/Person", supers);

        using HttpRequestMessage get = new(HttpMethod.Get, new Uri("datasets/d/graphs?graph=" + Uri.EscapeDataString(Conclusions), UriKind.Relative));
        get.Headers.Accept.ParseAdd("application/n-triples");
        HttpResponseMessage read = await fixture.Host.Client.SendAsync(get, P.Ct);
        string body = await read.Content.ReadAsStringAsync(P.Ct);
        Assert.True(read.StatusCode == HttpStatusCode.OK, body);
        Assert.Contains("<http://ex/Person> <" + Rdfs + "subClassOf> <" + Rdfs + "Resource> .", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owl2_rl_reads_every_graph_as_its_assertions_and_entailments()
    {
        await using Fixture fixture = await Fixture.StartAsync(InferenceProfile.Owl2Rl);

        HttpResponseMessage answered = await fixture.QueryAsync("SELECT ?c WHERE { GRAPH <http://ex/g/1> { <http://ex/ann> a ?c } }", "owl2-rl");
        string body = await answered.Content.ReadAsStringAsync(P.Ct);
        Assert.True(answered.StatusCode == HttpStatusCode.OK, body);
        Assert.Equal("owl2-rl", P.Header(answered, "Varve-Entailment"));
        string[] classes = Column(body, "c");
        Assert.Contains("http://ex/Person", classes);
        Assert.Contains("http://ex/Agent", classes);
    }

    /// <summary>
    /// The service description claims no more than is served (ADR 0197, the
    /// ruling of 2026-11-19): a graph an OWL2RL binding with
    /// <c>varve:SameAsOff</c> covers is described with
    /// <c>ent:OWL-RDF-Based</c>, the profile
    /// <c>varve:OWL2RLWithoutEquality</c> and the binding's
    /// <c>varve:sameAs</c>, never with <c>pr:RL</c>; a graph under an RDFS
    /// binding has none of them.
    /// </summary>
    [Fact]
    public async Task The_service_description_names_owl2_rl_without_equality_and_never_pr_RL()
    {
        const string Sd = "<http://www.w3.org/ns/sparql-service-description#";
        await using (Fixture fixture = await Fixture.StartAsync(InferenceProfile.Owl2Rl))
        {
            string[] lines = (await (await fixture.Host.Client.SendAsync(P.Get(Sparql, "application/n-triples"), P.Ct)).Content.ReadAsStringAsync(P.Ct)).Split('\n');
            string graph = lines.Single(l => l.Contains(Sd + "name> <http://ex/g/1>", StringComparison.Ordinal)).Split(' ')[0];
            Assert.Contains(graph + " " + Sd + "entailmentRegime> <http://www.w3.org/ns/entailment/OWL-RDF-Based> .", lines);
            Assert.Contains(graph + " " + Sd + "supportedEntailmentProfile> <https://w3id.org/varve/ns#OWL2RLWithoutEquality> .", lines);
            Assert.Contains(graph + " <https://w3id.org/varve/ns#sameAs> <https://w3id.org/varve/ns#SameAsOff> .", lines);
            Assert.Contains("_:default " + Sd + "supportedEntailmentProfile> <https://w3id.org/varve/ns#OWL2RLWithoutEquality> .", lines);
            Assert.DoesNotContain(lines, l => l.Contains("http://www.w3.org/ns/owl-profile/RL", StringComparison.Ordinal));

            // The TBox graph is no data graph of the binding: no claim.
            string tbox = lines.Single(l => l.Contains(Sd + "name> <http://ex/tbox>", StringComparison.Ordinal)).Split(' ')[0];
            Assert.DoesNotContain(lines, l => l.StartsWith(tbox + " " + Sd + "supportedEntailmentProfile>", StringComparison.Ordinal));
        }

        await using (Fixture rdfs = await Fixture.StartAsync(InferenceProfile.Rdfs))
        {
            string description = await (await rdfs.Host.Client.SendAsync(P.Get(Sparql, "application/n-triples"), P.Ct)).Content.ReadAsStringAsync(P.Ct);
            Assert.DoesNotContain("OWL-RDF-Based", description, StringComparison.Ordinal);
            Assert.DoesNotContain("OWL2RLWithoutEquality", description, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every answer under <c>owl2-rl</c> by a binding with
    /// <c>varve:SameAsOff</c> says equality was not applied, as
    /// <c>Varve-Equality: not-applied</c>, which <see cref="SparqlHttpClient.EntailedAnswerOf"/>
    /// reads; an answer under another regime says nothing of it.
    /// </summary>
    [Fact]
    public async Task An_owl2_rl_answer_says_equality_was_not_applied()
    {
        await using Fixture fixture = await Fixture.StartAsync(InferenceProfile.Owl2Rl);

        HttpResponseMessage owl = await fixture.QueryAsync("ASK { GRAPH <http://ex/g/1> { <http://ex/ann> a <http://ex/Agent> } }", "owl2-rl");
        Assert.Equal(HttpStatusCode.OK, owl.StatusCode);
        Assert.Equal("not-applied", P.Header(owl, "Varve-Equality"));
        Varve.Protocol.Client.Model.EntailedAnswer read = SparqlHttpClient.EntailedAnswerOf(owl);
        Assert.True(read.EqualityNotApplied);
        Assert.False(read.Inconsistent);

        HttpResponseMessage simple = await fixture.QueryAsync("ASK { GRAPH <http://ex/g/1> { <http://ex/ann> a <http://ex/Person> } }", null);
        Assert.Equal(HttpStatusCode.OK, simple.StatusCode);
        Assert.False(simple.Headers.Contains("Varve-Equality"));
        Assert.False(SparqlHttpClient.EntailedAnswerOf(simple).EqualityNotApplied);
    }

    /// <summary>
    /// A default graph merging two or more graphs under <c>owl2-rl</c> is
    /// answered by the union of each graph's closure, and the answer says so
    /// as <c>Varve-Closure: per-graph</c>, by FROM and by the protocol's
    /// <c>default-graph-uri</c> alike; one graph, a view beside it, or
    /// <c>GRAPH</c> patterns are no merge, and say nothing.
    /// </summary>
    [Fact]
    public async Task An_owl2_rl_answer_over_a_merge_says_it_was_answered_graph_by_graph()
    {
        await using Fixture fixture = await Fixture.StartAsync(InferenceProfile.Owl2Rl);

        HttpResponseMessage merged = await fixture.QueryAsync("ASK FROM <http://ex/g/1> FROM <http://ex/g/2> { <http://ex/ann> a <http://ex/Agent> }", "owl2-rl");
        Assert.True(merged.StatusCode == HttpStatusCode.OK, await merged.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal("per-graph", P.Header(merged, "Varve-Closure"));
        Assert.True(SparqlHttpClient.EntailedAnswerOf(merged).PerGraphClosure);

        HttpRequestMessage byProtocol = new(HttpMethod.Get, new Uri(Sparql + "?query=" + Uri.EscapeDataString("ASK { ?s ?p ?o }") + "&default-graph-uri=" + Uri.EscapeDataString("http://ex/g/1") + "&default-graph-uri=" + Uri.EscapeDataString("http://ex/g/2"), UriKind.Relative));
        byProtocol.Headers.TryAddWithoutValidation("Accept", "application/sparql-results+json");
        byProtocol.Headers.TryAddWithoutValidation("Varve-Entailment", "owl2-rl");
        HttpResponseMessage protocol = await fixture.Host.Client.SendAsync(byProtocol, P.Ct);
        Assert.Equal("per-graph", P.Header(protocol, "Varve-Closure"));

        foreach (string one in (string[])[
            "ASK FROM <http://ex/g/1> { <http://ex/ann> a <http://ex/Agent> }",
            "ASK FROM <http://ex/g/1> FROM <" + Implicit + "> { <http://ex/ann> a <http://ex/Agent> }",
            "ASK { GRAPH ?g { <http://ex/ann> a <http://ex/Agent> } }",
        ])
        {
            HttpResponseMessage answered = await fixture.QueryAsync(one, "owl2-rl");
            Assert.True(answered.StatusCode == HttpStatusCode.OK, one + await answered.Content.ReadAsStringAsync(P.Ct));
            Assert.False(answered.Headers.Contains("Varve-Closure"), one);
        }

        // Under another regime there is no closure to merge, and nothing to say.
        Assert.False((await fixture.QueryAsync("ASK FROM <http://ex/g/1> FROM <http://ex/g/2> { ?s ?p ?o }", null)).Headers.Contains("Varve-Closure"));
    }

    /// <summary>
    /// <c>owl2-rl</c>, or a view, naming a graph no binding gives a closure is
    /// refused as <c>entailment-unsupported</c>, naming the graph (ADR 0197
    /// point 6, ADR 0198 point 1, as amended 2026-11-19): by <c>FROM</c>, by
    /// <c>GRAPH</c>, and the binding's own TBox graph, whose conclusions are
    /// in the reserved graph. A covered graph is answered.
    /// </summary>
    [Fact]
    public async Task Owl2_rl_or_a_view_naming_a_graph_with_no_closure_is_entailment_unsupported()
    {
        await using Fixture fixture = await Fixture.StartAsync(InferenceProfile.Owl2Rl);
        foreach ((string query, string? regime, string graph) in new (string, string?, string)[]
        {
            ("ASK FROM <http://other/x> { ?s ?p ?o }", "owl2-rl", "http://other/x"),
            ("ASK { GRAPH <http://other/x> { ?s ?p ?o } }", "owl2-rl", "http://other/x"),
            ("ASK { GRAPH <http://ex/tbox> { ?s ?p ?o } }", "owl2-rl", "http://ex/tbox"),
            ("ASK FROM <http://other/x> FROM <" + Implicit + "> { ?s ?p ?o }", null, "http://other/x"),
        })
        {
            HttpResponseMessage refused = await fixture.QueryAsync(query, regime);
            string body = await refused.Content.ReadAsStringAsync(P.Ct);
            Assert.True(refused.StatusCode == HttpStatusCode.BadRequest, query + ": " + body);
            using JsonDocument problem = JsonDocument.Parse(body);
            Assert.Equal("https://w3id.org/varve/problems/entailment-unsupported", problem.RootElement.GetProperty("type").GetString());
            Assert.Contains("<" + graph + ">", problem.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }

        HttpResponseMessage covered = await fixture.QueryAsync("ASK FROM <http://ex/g/1> { <http://ex/ann> a <http://ex/Agent> }", "owl2-rl");
        Assert.True(covered.StatusCode == HttpStatusCode.OK, await covered.Content.ReadAsStringAsync(P.Ct));
    }

    /// <summary>
    /// <c>GRAPH ?g</c> under <c>owl2-rl</c> over named graphs some of which
    /// have no closure is answered, and <c>Varve-Without-Closure</c> names
    /// those read with their explicit quads only: here the binding's TBox
    /// graph. With <c>FROM NAMED</c> naming only covered graphs it says
    /// nothing.
    /// </summary>
    [Fact]
    public async Task Graph_variable_over_a_mix_names_the_graphs_read_without_a_closure()
    {
        await using Fixture fixture = await Fixture.StartAsync(InferenceProfile.Owl2Rl);
        HttpResponseMessage mixed = await fixture.QueryAsync("SELECT ?g WHERE { GRAPH ?g { ?s ?p ?o } }", "owl2-rl");
        Assert.True(mixed.StatusCode == HttpStatusCode.OK, await mixed.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal("<http://ex/tbox>", P.Header(mixed, "Varve-Without-Closure"));
        Assert.Equal([new Varve.Protocol.Client.Model.GraphWithoutClosure("<http://ex/tbox>")], SparqlHttpClient.EntailedAnswerOf(mixed).WithoutClosure);

        HttpResponseMessage covered = await fixture.QueryAsync("SELECT ?g FROM NAMED <http://ex/g/1> WHERE { GRAPH ?g { ?s ?p ?o } }", "owl2-rl");
        Assert.True(covered.StatusCode == HttpStatusCode.OK, await covered.Content.ReadAsStringAsync(P.Ct));
        Assert.False(covered.Headers.Contains("Varve-Without-Closure"));
        Assert.Empty(SparqlHttpClient.EntailedAnswerOf(covered).WithoutClosure);

        HttpResponseMessage viewed = await fixture.QueryAsync("SELECT ?g FROM NAMED <" + Implicit + "> WHERE { GRAPH ?g { ?s ?p ?o } }", null);
        Assert.True(viewed.StatusCode == HttpStatusCode.OK, await viewed.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal("<http://ex/tbox>", P.Header(viewed, "Varve-Without-Closure"));
    }

    /// <summary>
    /// A merge under <c>owl2-rl</c> of graphs one cross-graph binding covers
    /// reads the premise-set graphs within it, and is the closure of the
    /// merge: a conclusion drawn from two graphs together is answered, and no
    /// <c>Varve-Closure</c> is sent. Under an intra-graph binding the same
    /// merge is each graph's closure, the conclusion is missing, and the
    /// answer says <c>per-graph</c> (ADR 0197 point 6).
    /// </summary>
    [Fact]
    public async Task A_merge_under_a_cross_graph_binding_is_the_closure_of_the_merge()
    {
        const string Ask = "ASK FROM <http://ex/g/1> FROM <http://ex/g/2> { <http://ex/ann> a <http://ex/Agent> }";
        foreach (bool crossGraph in new[] { true, false })
        {
            await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, P.Ct);
            CommitRequestBuilder request = new();
            request.Assert(Term("http://ex/ann"), Term(Rdf + "type"), Term("http://ex/Person"), Term("http://ex/g/1"));
            request.Assert(Term("http://ex/Person"), Term(Rdfs + "subClassOf"), Term("http://ex/Agent"), Term("http://ex/g/2"));
            Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(request, P.Ct)).Outcome);
            InferenceProjection projection = new(dataset, new InferenceBinding(RdfTerm.Iri("urn:b"u8), [RdfTerm.Iri("http://ex/tbox"u8)], GraphScope.Of([], ["http://ex/g/"], DefaultGraphAccess.Included), InferenceProfile.Owl2Rl) { CrossGraph = crossGraph });
            await dataset.CatchUpAsync(projection, P.Ct);
            await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(new ProtocolTestHost.OneDataset(dataset), inference: new Closures(projection, Refusal.None), stopping: P.Ct);

            HttpRequestMessage query = P.Query(Sparql, Ask, "application/sparql-results+json");
            query.Headers.TryAddWithoutValidation("Varve-Entailment", "owl2-rl");
            HttpResponseMessage answered = await host.Client.SendAsync(query, P.Ct);
            string body = await answered.Content.ReadAsStringAsync(P.Ct);
            Assert.True(answered.StatusCode == HttpStatusCode.OK, body);
            Assert.Contains(crossGraph ? "\"boolean\":true" : "\"boolean\":false", body.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.Equal(crossGraph ? null : "per-graph", answered.Headers.Contains("Varve-Closure") ? P.Header(answered, "Varve-Closure") : null);
        }
    }

    [Fact]
    public async Task Owl2_rl_over_no_owl2rl_binding_and_a_view_under_a_rewritten_regime_are_unsupported()
    {
        await using Fixture fixture = await Fixture.StartAsync(InferenceProfile.Rdfs);

        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.QueryAsync("ASK { ?s ?p ?o }", "owl2-rl")).StatusCode);
        HttpResponseMessage refused = await fixture.QueryAsync("SELECT ?c FROM <" + Implicit + "> WHERE { ?s a ?c }", "rdfs");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("entailment-unsupported", await refused.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_naming_no_view_nor_reserved_graph_never_reads_a_closure()
    {
        await using Fixture fixture = await Fixture.StartAsync(InferenceProfile.Rdfs, refuse: Refusal.Behind);

        HttpResponseMessage answered = await fixture.QueryAsync("SELECT ?g ?s WHERE { GRAPH ?g { ?s ?p ?o } }", null);
        string body = await answered.Content.ReadAsStringAsync(P.Ct);
        Assert.True(answered.StatusCode == HttpStatusCode.OK, body);
        Assert.DoesNotContain("inferred", body, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Closures.Reads);
    }

    [Fact]
    public async Task A_closure_behind_is_inference_behind_and_one_stopped_is_inference_unavailable()
    {
        await using (Fixture behind = await Fixture.StartAsync(InferenceProfile.Rdfs, refuse: Refusal.Behind))
        {
            HttpResponseMessage response = await behind.QueryAsync("SELECT ?c FROM <http://ex/g/1> FROM <" + Implicit + "> WHERE { ?s a ?c }", null);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("1", P.Header(response, "Retry-After"));
            using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(P.Ct));
            Assert.Equal("https://w3id.org/varve/problems/inference-behind", problem.RootElement.GetProperty("type").GetString());
            Assert.Equal("urn:b", problem.RootElement.GetProperty("binding").GetString());
        }

        await using Fixture stopped = await Fixture.StartAsync(InferenceProfile.Rdfs, refuse: Refusal.Stopped);
        HttpResponseMessage refused = await stopped.QueryAsync("SELECT ?x WHERE { GRAPH <" + Conclusions + "> { ?s ?p ?x } }", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.False(refused.Headers.Contains("Retry-After"));
        using JsonDocument unavailable = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(P.Ct));
        JsonElement root = unavailable.RootElement;
        Assert.Equal("https://w3id.org/varve/problems/inference-unavailable", root.GetProperty("type").GetString());
        Assert.Equal("memory-limit", root.GetProperty("reason").GetString());
        Assert.Equal("configuration", root.GetProperty("failure").GetString());
        Assert.Equal(1024, root.GetProperty("limit").GetInt64());
        Assert.Equal(2048, root.GetProperty("reached").GetInt64());
        foreach (JsonProperty member in root.EnumerateObject())
        {
            Assert.True(member.Name is "type" or "title" or "status" or "detail" or "instance" || ProblemCatalogue.Of(ProblemType.InferenceUnavailable).Members.Contains(member.Name), member.Name);
        }
    }

    /// <summary>
    /// A binding stopped because its projection failed is answered as a
    /// defect, never as configuration (ADR 0195 point 6, ADR 0242 ruling 2 as
    /// amended 2026-11-19): <c>reason</c> <c>failed</c>, <c>failure</c>
    /// <c>defect</c>, no <c>Retry-After</c>, no limit; the detail says to
    /// report it and upgrade.
    /// </summary>
    [Fact]
    public async Task A_binding_stopped_by_a_failure_is_answered_as_a_defect()
    {
        await using Fixture failed = await Fixture.StartAsync(InferenceProfile.Rdfs, refuse: Refusal.Failed);
        HttpResponseMessage refused = await failed.QueryAsync("SELECT ?x WHERE { GRAPH <" + Conclusions + "> { ?s ?p ?x } }", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.False(refused.Headers.Contains("Retry-After"));
        using JsonDocument unavailable = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(P.Ct));
        JsonElement root = unavailable.RootElement;
        Assert.Equal("https://w3id.org/varve/problems/inference-unavailable", root.GetProperty("type").GetString());
        Assert.Equal("failed", root.GetProperty("reason").GetString());
        Assert.Equal("defect", root.GetProperty("failure").GetString());
        Assert.False(root.TryGetProperty("limit", out _));
        Assert.False(root.TryGetProperty("reached", out _));
        string detail = root.GetProperty("detail").GetString()!;
        Assert.Contains("report", detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("upgrade", detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ADR 0198 point 3, ADR 0022 as amended: a read of the closure never
    /// denies a term its rows hold, a vocabulary constant the dataset lacks
    /// included, whichever term it is asked for first, even while the
    /// projection is behind and its rows unread. Each fresh source is a fresh
    /// projection, held back, whose one wait catches it up.
    /// </summary>
    [Fact]
    public async Task A_closure_read_behind_its_projection_never_denies_a_term_its_rows_hold()
    {
        await using Dataset dataset = await FilledAsync();
        using DatasetView view = dataset.Pin();
        RdfTerm conclusions = RdfTerm.Iri(Encoding.UTF8.GetBytes(Conclusions));

        IQuadSource Fresh()
        {
            InferenceProjection projection = new(dataset, Binding(InferenceProfile.Owl2Rl));
            Closures closures = new(projection, Refusal.None, () => dataset.CatchUpAsync(projection, P.Ct).AsTask().GetAwaiter().GetResult());
            StagingView staging = view.Stage();
            return ClosureGraphs.Compose(GraphScopedQuadSource.Wrap(staging, GraphScope.All), staging, [closures], ClosureMode.Union);
        }

        IReadOnlyCollection<RdfTerm> yielded = QuadSourceContract.Holds(
            Fresh,
            probes: [RdfTerm.Iri(Encoding.UTF8.GetBytes(Rdfs + "Resource")), RdfTerm.Iri("http://www.w3.org/2002/07/owl#Thing"u8), conclusions],
            complete: source =>
            {
                // The reserved graph is read only when named: name it, and match it.
                Assert.True(source.TryInternalise(conclusions, out TermHandle graph));
                using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Named(graph));
                while (cursor.MoveNext())
                {
                }
            });
        Assert.Contains(RdfTerm.Iri(Encoding.UTF8.GetBytes(Rdfs + "Resource")), yielded);
        Assert.Contains(RdfTerm.Iri("http://ex/Agent"u8), yielded);
    }

    [Fact]
    public async Task Explain_answers_a_well_founded_proof_explicit_or_absent()
    {
        await using Fixture fixture = await Fixture.StartAsync(InferenceProfile.Rdfs);

        using (JsonDocument implicitQuad = await fixture.ExplainAsync("<http://ex/ann> <" + Rdf + "type> <http://ex/Agent> <http://ex/g/1> ."))
        {
            JsonElement root = implicitQuad.RootElement;
            Assert.Equal("implicit", root.GetProperty("status").GetString());
            Assert.Equal(1, root.GetProperty("position").GetInt64());
            JsonElement proof = root.GetProperty("proof");
            Assert.Equal("https://w3id.org/varve/ns#rule/rdfs/rdfs9", proof.GetProperty("rule").GetString());
            JsonElement[] premises = [.. proof.GetProperty("premises").EnumerateArray()];
            Assert.Equal(2, premises.Length);
            Assert.Equal("<http://ex/Person> <" + Rdfs + "subClassOf> <http://ex/Agent> <http://ex/tbox> .", premises[0].GetProperty("quad").GetString());
            Assert.Equal("explicit", premises[0].GetProperty("status").GetString());
            Assert.Equal("http://ex/tbox", premises[0].GetProperty("graph").GetString());
            Assert.Equal("<http://ex/ann> <" + Rdf + "type> <http://ex/Person> <http://ex/g/1> .", premises[1].GetProperty("quad").GetString());
            Assert.False(root.TryGetProperty("inconsistent", out _));
        }

        using (JsonDocument asserted = await fixture.ExplainAsync("<http://ex/ann> <" + Rdf + "type> <http://ex/Person> <http://ex/g/1> ."))
        {
            Assert.Equal("explicit", asserted.RootElement.GetProperty("status").GetString());
            Assert.False(asserted.RootElement.TryGetProperty("proof", out _));
        }

        using (JsonDocument absent = await fixture.ExplainAsync("<http://ex/ann> <" + Rdf + "type> <http://ex/Nobody> <http://ex/g/1> ."))
        {
            Assert.Equal("absent", absent.RootElement.GetProperty("status").GetString());
        }

        using (JsonDocument conclusion = await fixture.ExplainAsync("<http://ex/Person> <" + Rdfs + "subClassOf> <" + Rdfs + "Resource> <" + Conclusions + "> ."))
        {
            Assert.Equal("implicit", conclusion.RootElement.GetProperty("status").GetString());
        }

        // A malformed request, or one with a blank node, is a bad request.
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Host.Client.GetAsync(new Uri("datasets/d/explain?quad=" + Uri.EscapeDataString("_:b <http://ex/p> <http://ex/o> ."), UriKind.Relative), P.Ct)).StatusCode);
    }

    [Fact]
    public async Task Explain_names_the_clash_and_is_refused_as_a_read_is()
    {
        await using (Fixture fixture = await Fixture.StartAsync(InferenceProfile.Owl2Rl))
        {
            CommitRequestBuilder request = new();
            request.Assert(Term("http://ex/Person"), Term("http://www.w3.org/2002/07/owl#disjointWith"), Term("http://ex/Robot"), Term("http://ex/tbox"));
            request.Assert(Term("http://ex/ann"), Term(Rdf + "type"), Term("http://ex/Robot"), Term("http://ex/g/1"));
            Assert.Equal(CommitOutcome.Committed, (await fixture.Dataset.CommitAsync(request, P.Ct)).Outcome);
            await fixture.Dataset.CatchUpAsync(fixture.Closures.Projection, P.Ct);

            using JsonDocument explained = await fixture.ExplainAsync("<http://ex/ann> <" + Rdf + "type> <http://ex/Robot> <http://ex/g/1> .");
            JsonElement root = explained.RootElement;
            Assert.Equal("explicit", root.GetProperty("status").GetString());
            Assert.True(root.GetProperty("inconsistent").GetBoolean());
            JsonElement clash = Assert.Single(root.GetProperty("clashes").EnumerateArray());
            Assert.Equal("https://w3id.org/varve/ns#rule/owl2rl/cax-dw", clash.GetProperty("rule").GetString());
            Assert.Equal(3, clash.GetProperty("premises").GetArrayLength());

            HttpResponseMessage owl = await fixture.QueryAsync("ASK { GRAPH <http://ex/g/1> { <http://ex/ann> a <http://ex/Agent> } }", "owl2-rl");
            Assert.Equal("true", P.Header(owl, "Varve-Inconsistent"));
        }

        await using Fixture behind = await Fixture.StartAsync(InferenceProfile.Rdfs, refuse: Refusal.Behind);
        HttpResponseMessage response = await behind.Host.Client.GetAsync(new Uri("datasets/d/explain?quad=" + Uri.EscapeDataString("<http://ex/ann> <" + Rdf + "type> <http://ex/Agent> <http://ex/g/1> ."), UriKind.Relative), P.Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("inference-behind", await response.Content.ReadAsStringAsync(P.Ct), StringComparison.Ordinal);
    }

    private static string[] Column(string json, string variable)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return [.. document.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray()
            .Select(binding => binding.GetProperty(variable).GetProperty("value").GetString()!).Order(StringComparer.Ordinal)];
    }

    private enum Refusal
    {
        None,
        Behind,
        Stopped,
        Failed,
    }

    /// <summary>A dataset with a TBox and one data graph, a binding's projection over it, and the host reading it through the seam.</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(Dataset dataset, ProtocolTestHost host, Closures closures)
        {
            Dataset = dataset;
            Host = host;
            Closures = closures;
        }

        internal Dataset Dataset { get; }

        internal ProtocolTestHost Host { get; }

        internal Closures Closures { get; }

        internal static async Task<Fixture> StartAsync(InferenceProfile profile, Refusal refuse = Refusal.None)
        {
            Dataset dataset = await FilledAsync();
            InferenceProjection projection = new(dataset, Binding(profile));
            await dataset.CatchUpAsync(projection, P.Ct);
            Closures closures = new(projection, refuse);
            ProtocolTestHost host = await ProtocolTestHost.StartAsync(new ProtocolTestHost.OneDataset(dataset), inference: closures, stopping: P.Ct);
            return new Fixture(dataset, host, closures);
        }

        internal async Task<HttpResponseMessage> QueryAsync(string query, string? regime)
        {
            HttpRequestMessage request = P.Query(Sparql, query, "application/sparql-results+json");
            if (regime is not null)
            {
                request.Headers.TryAddWithoutValidation("Varve-Entailment", regime);
            }

            return await Host.Client.SendAsync(request, P.Ct);
        }

        internal async Task<JsonDocument> ExplainAsync(string quad)
        {
            HttpResponseMessage answered = await Host.Client.GetAsync(new Uri("datasets/d/explain?quad=" + Uri.EscapeDataString(quad), UriKind.Relative), P.Ct);
            string body = await answered.Content.ReadAsStringAsync(P.Ct);
            Assert.True(answered.StatusCode == HttpStatusCode.OK, body);
            return JsonDocument.Parse(body);
        }

        internal async Task<string[]> ColumnAsync(string query, string variable)
        {
            HttpResponseMessage answered = await QueryAsync(query, null);
            string body = await answered.Content.ReadAsStringAsync(P.Ct);
            Assert.True(answered.StatusCode == HttpStatusCode.OK, body);
            return Column(body, variable);
        }

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await Dataset.DisposeAsync();
        }
    }

    /// <summary>
    /// A cross-graph binding's conclusion drawn from two graphs (ADR 0190
    /// point 6) sits in its premise set's reserved graph, read by a caller
    /// who reads both graphs and by no caller who reads one.
    /// </summary>
    [Fact]
    public async Task A_cross_graph_conclusion_is_read_in_its_premise_sets_graph_by_a_caller_who_reads_every_graph_of_it()
    {
        await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, P.Ct);
        CommitRequestBuilder request = new();
        request.Assert(Term("http://ex/ann"), Term(Rdf + "type"), Term("http://ex/Person"), Term("http://ex/g/1"));
        request.Assert(Term("http://ex/Person"), Term(Rdfs + "subClassOf"), Term("http://ex/Agent"), Term("http://ex/g/2"));
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(request, P.Ct)).Outcome);
        InferenceProjection projection = new(dataset, new InferenceBinding(RdfTerm.Iri("urn:b"u8), [RdfTerm.Iri("http://ex/tbox"u8)], GraphScope.Of([], ["http://ex/g/"], DefaultGraphAccess.Included), InferenceProfile.Rdfs) { CrossGraph = true });
        await dataset.CatchUpAsync(projection, P.Ct);

        string digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("<http://ex/g/1>\n<http://ex/g/2>\n"u8));
        string reserved = "https://w3id.org/varve/ns#inferred/urn%3Ab/" + digest;
        Assert.Equal(["http://ex/g/1", "http://ex/g/2"], projection.PremiseSetOf(RdfTerm.Iri(Encoding.UTF8.GetBytes(reserved)))!.Select(g => Encoding.UTF8.GetString(g.Lexical)));
        string query = "SELECT ?c WHERE { GRAPH <" + reserved + "> { <http://ex/ann> a ?c } }";

        foreach ((string[] reads, bool sees) in new[] { (new[] { "http://ex/g/1", "http://ex/g/2" }, true), (new[] { "http://ex/g/1" }, false), (new[] { "http://ex/g/2" }, false) })
        {
            GraphScope readable = GraphScope.Of([.. reads.Select(g => RdfTerm.Iri(Encoding.UTF8.GetBytes(g)))], [], DefaultGraphAccess.Excluded);
            await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(
                new ProtocolTestHost.OneDataset(dataset),
                accessScopes: new DefaultGraphOnly(new CallerScope(readable, readable, AdminAccess.None)),
                inference: new Closures(projection, Refusal.None),
                stopping: P.Ct);
            HttpResponseMessage answered = await host.Client.SendAsync(P.Query(Sparql, query, "application/sparql-results+json"), P.Ct);
            string body = await answered.Content.ReadAsStringAsync(P.Ct);
            Assert.True(answered.StatusCode == HttpStatusCode.OK, body);
            string[] classes = Column(body, "c");
            Assert.True(sees ? classes.Contains("http://ex/Agent") : classes.Length == 0, string.Join(", ", reads) + ": " + body);
        }

        // Neither graph alone holds ann's being an agent: it is a row of the reserved graph only.
        Assert.Equal(InferenceReadOutcome.Held, projection.TryRead(projection.Position, out InferredRows? rows));
        using DatasetView view = dataset.Pin();
        List<string> placed = [];
        foreach (InferredRow row in rows!.Rows)
        {
            if ((rows.TryExternalise(row.Fact.Subject, out RdfTerm? s) || view.TryExternalise(row.Fact.Subject, out s)) && Encoding.UTF8.GetString(s.Lexical) == "http://ex/ann"
                && (rows.TryExternalise(row.Fact.Object, out RdfTerm? o) || view.TryExternalise(row.Fact.Object, out o)) && Encoding.UTF8.GetString(o.Lexical) == "http://ex/Agent"
                && (rows.TryExternalise(row.Graph, out RdfTerm? g) || view.TryExternalise(row.Graph, out g)))
            {
                placed.Add(Encoding.UTF8.GetString(g.Lexical));
            }
        }

        Assert.Equal([reserved], placed);
    }

    /// <summary>
    /// An answer under <c>owl2-rl</c> whose closure leaves list cells unread
    /// in a graph the caller reads says how many, as
    /// <c>Varve-Unread-Lists</c>, which <see cref="SparqlHttpClient.EntailedAnswerOf"/>
    /// reads; cells in a graph the caller does not read are not counted, and
    /// an answer as of a position before the cells were asserted says
    /// nothing (ADR 0197, amended 2026-11-26).
    /// </summary>
    [Fact]
    public async Task An_owl2_rl_answer_says_how_many_list_cells_it_reads_are_unread_as_of_its_position()
    {
        await using Dataset dataset = await FilledAsync();
        CommitRequestBuilder request = new();
        request.Assert(Term("http://ex/l1"), Term(Rdf + "first"), Term("http://ex/A"), Term("http://ex/g/1"));
        request.Assert(Term("http://ex/l2"), Term(Rdf + "rest"), Term(Rdf + "nil"), Term("http://ex/g/2"));
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(request, P.Ct)).Outcome);

        // A commit after: the projection is above position 2, so an answer as of 2 reads its lists from the view.
        CommitRequestBuilder after = new();
        after.Assert(Term("http://ex/bob"), Term(Rdf + "type"), Term("http://ex/Person"), Term("http://ex/g/1"));
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(after, P.Ct)).Outcome);
        InferenceProjection projection = new(dataset, Binding(InferenceProfile.Owl2Rl));
        await dataset.CatchUpAsync(projection, P.Ct);
        const string ask = "ASK { GRAPH ?g { ?s ?p ?o } }";

        foreach ((string[] reads, string? asOf, int expected) in new[]
        {
            (new[] { "http://ex/g/1", "http://ex/g/2" }, (string?)null, 2),
            (new[] { "http://ex/g/1" }, null, 1),
            (new[] { "http://ex/g/1", "http://ex/g/2" }, "position:2", 2),
            (new[] { "http://ex/g/2" }, "position:2", 1),
            (new[] { "http://ex/g/1", "http://ex/g/2" }, "position:1", 0),
        })
        {
            GraphScope readable = GraphScope.Of([.. reads.Select(g => RdfTerm.Iri(Encoding.UTF8.GetBytes(g)))], [], DefaultGraphAccess.Excluded);
            await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(
                new ProtocolTestHost.OneDataset(dataset),
                accessScopes: new DefaultGraphOnly(new CallerScope(readable, readable, AdminAccess.None)),
                inference: new Closures(projection, Refusal.None),
                stopping: P.Ct);
            HttpRequestMessage query = P.Query(Sparql, ask, "application/sparql-results+json", asOf);
            query.Headers.TryAddWithoutValidation("Varve-Entailment", "owl2-rl");
            HttpResponseMessage answered = await host.Client.SendAsync(query, P.Ct);
            Assert.True(answered.StatusCode == HttpStatusCode.OK, await answered.Content.ReadAsStringAsync(P.Ct));
            Assert.Equal(expected == 0 ? null : expected.ToString(System.Globalization.CultureInfo.InvariantCulture), answered.Headers.TryGetValues("Varve-Unread-Lists", out IEnumerable<string>? values) ? string.Join(",", values) : null);
            Assert.Equal(expected, SparqlHttpClient.EntailedAnswerOf(answered).UnreadLists.Value);
        }
    }

    private static InferenceBinding Binding(InferenceProfile profile) =>
        new(RdfTerm.Iri("urn:b"u8), [RdfTerm.Iri("http://ex/tbox"u8)], GraphScope.Of([], ["http://ex/g/"], DefaultGraphAccess.Included), profile);

    /// <summary>A TBox triple and a data triple, at position 1.</summary>
    private static async Task<Dataset> FilledAsync()
    {
        Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System }, P.Ct);
        CommitRequestBuilder request = new();
        request.Assert(Term("http://ex/Person"), Term(Rdfs + "subClassOf"), Term("http://ex/Agent"), Term("http://ex/tbox"));
        request.Assert(Term("http://ex/ann"), Term(Rdf + "type"), Term("http://ex/Person"), Term("http://ex/g/1"));
        Assert.Equal(CommitOutcome.Committed, (await dataset.CommitAsync(request, P.Ct)).Outcome);
        return dataset;
    }

    private static RequestTerm Term(string iri) => RequestTerm.FromTerm(RdfTerm.Iri(Encoding.UTF8.GetBytes(iri)));

    /// <summary>The seam over one projection, as the server's is, or refusing as asked; it counts the reads that reached it.</summary>
    private sealed class Closures(InferenceProjection projection, Refusal refuse, Action? catchUp = null) : IInferenceClosures, IInferenceClosure
    {
        private int _reads;

        internal int Reads => Volatile.Read(ref _reads);

        internal InferenceProjection Projection => projection;

        public RdfTerm Binding => projection.Binding.Binding;

        public RdfTerm TBoxConclusions => projection.TBoxGraph;

        public IReadOnlyList<RdfTerm> TBoxGraphs => projection.Binding.TBoxGraphs;

        public GraphScope DataGraphs => projection.Binding.DataGraphs;

        public InferenceProfile Profile => projection.Binding.Profile;

        public IReadOnlyList<RdfTerm>? PremiseSetOf(RdfTerm graph) => projection.PremiseSetOf(graph);

        public IReadOnlyList<RdfTerm>? PremiseSetGraphs() => projection.Binding.CrossGraph ? projection.PremiseSetGraphs() : null;

        public IReadOnlyList<IInferenceClosure> Of(DatasetName dataset) => [this];

        public InferredRows RowsAt(Position position)
        {
            Interlocked.Increment(ref _reads);
            if (projection.Position < position)
            {
                // The read's one bounded wait, which here catches the projection up.
                catchUp?.Invoke();
            }

            return refuse switch
            {
                Refusal.Behind => throw new InferenceBehindException(Binding, position, new Position(0)),
                Refusal.Stopped => throw new InferenceUnavailableException(Binding, new ByteCount(1024), new ByteCount(2048)),
                Refusal.Failed => throw new InferenceUnavailableException(Binding, new InvalidOperationException("an invariant broke")),
                _ => projection.TryRead(position, out InferredRows? rows) == InferenceReadOutcome.Held
                    ? rows!
                    : projection.ReadFromScratchAsync(position, CancellationToken.None).AsTask().GetAwaiter().GetResult(),
            };
        }

        public IReadOnlyList<PlacedClash> Clashes() => projection.Clashes();

        public IReadOnlyList<RdfTerm> UnreadListGraphsAt(DatasetView view) => [.. projection.UnreadListsAt(view).Select(unread => unread.Graph)];

        public Explanation Explain(Position position, RdfTerm? graph, RdfTerm subject, RdfTerm predicate, RdfTerm @object)
        {
            Interlocked.Increment(ref _reads);
            return refuse switch
            {
                Refusal.Behind => throw new InferenceBehindException(Binding, position, new Position(0)),
                Refusal.Stopped => throw new InferenceUnavailableException(Binding, new ByteCount(1024), new ByteCount(2048)),
                Refusal.Failed => throw new InferenceUnavailableException(Binding, new InvalidOperationException("an invariant broke")),
                _ => projection.ExplainAsync(position, graph, subject, predicate, @object, CancellationToken.None).AsTask().GetAwaiter().GetResult().Explanation!,
            };
        }
    }
}
