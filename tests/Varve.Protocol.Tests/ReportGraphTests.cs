// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CsCheck;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.Shacl;
using Varve.Shacl.Store;
using Varve.Shacl.Store.Model;
using Varve.Sparql;
using Varve.Store;
using Varve.Store.Log;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// A binding's report as its reserved graph through the protocol (ADR
/// 0174): read by query and by the graph store when the caller reads every
/// graph the binding validates and invisible otherwise (ADR 0107,
/// amended), <c>503 validation-behind</c> when the projection has not
/// reached the position read, and never written.
/// </summary>
public class ReportGraphTests
{
    private const string Sparql = "datasets/d/sparql";
    private const string Data = "http://ex/g/data";
    private const string Sh = "http://www.w3.org/ns/shacl#";
    private const string Report = "https://w3id.org/varve/ns#validation/urn%3Ab";
    private const string FocusQuery = "SELECT ?focus WHERE { GRAPH <" + Report + "> { ?r <" + Sh + "focusNode> ?focus } }";

    private static readonly ShapesBinding Binding = new(RdfTerm.Iri("urn:b"u8), RdfTerm.Iri("http://ex/shapes"u8), GraphScope.Of([], ["http://ex/g/"], DefaultGraphAccess.Excluded), ValidationMode.Report);

    [Fact]
    public async Task a_caller_who_reads_every_validated_graph_reads_the_report_and_any_other_sees_no_such_graph()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await FillAsync(dataset);
        ValidationProjection projection = new(dataset, Binding);
        await dataset.CatchUpAsync(projection, P.Ct);
        Reports reports = new(projection);
        const string Focus = FocusQuery;

        // Read on a prefix above the binding's: the report.
        await using (ProtocolTestHost host = await StartAsync(dataset, reports, Readable(["http://ex/"])))
        {
            string csv = await ReadAsync(host, P.Query(Sparql, Focus, "text/csv"));
            Assert.Equal("focus\r\nhttp://ex/ann\r\n", csv);
            string graphs = await ReadAsync(host, P.Query(Sparql, "SELECT DISTINCT ?g WHERE { GRAPH ?g { ?s ?p ?o } } ORDER BY ?g", "text/csv"));
            Assert.Contains(Report, graphs, StringComparison.Ordinal);
        }

        // Read on one data graph only, not every graph the prefix names: no report graph at all.
        await using (ProtocolTestHost host = await StartAsync(dataset, reports, GraphScope.Of([RdfTerm.Iri(Encoding.UTF8.GetBytes(Data))], [], DefaultGraphAccess.Excluded)))
        {
            Assert.Equal("focus\r\n", await ReadAsync(host, P.Query(Sparql, Focus, "text/csv")));
            HttpResponseMessage missing = await host.Client.SendAsync(P.Get("datasets/d/graphs?graph=" + Uri.EscapeDataString(Report), "application/n-triples"), P.Ct);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    [Fact]
    public async Task a_read_above_the_projection_is_validation_behind_and_a_read_that_never_needs_the_report_is_not()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await FillAsync(dataset);

        // Fed nothing: the projection is at position 0, the head at 2.
        Reports reports = new(new ValidationProjection(dataset, Binding));
        await using ProtocolTestHost host = await StartAsync(dataset, reports, Readable(["http://ex/"]));

        HttpResponseMessage behind = await host.Client.SendAsync(P.Get("datasets/d/graphs?graph=" + Uri.EscapeDataString(Report), "application/n-triples"), P.Ct);
        string body = await behind.Content.ReadAsStringAsync(P.Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, behind.StatusCode);
        Assert.Equal("1", P.Header(behind, "Retry-After"));
        using (JsonDocument problem = JsonDocument.Parse(body))
        {
            Assert.Equal("https://w3id.org/varve/problems/validation-behind", problem.RootElement.GetProperty("type").GetString());
            Assert.Equal("urn:b", problem.RootElement.GetProperty("binding").GetString());
            Assert.Equal(2, problem.RootElement.GetProperty("position").GetInt64());
            Assert.Equal(0, problem.RootElement.GetProperty("projectedPosition").GetInt64());
        }

        HttpResponseMessage queried = await host.Client.SendAsync(P.Query(Sparql, "SELECT * WHERE { GRAPH ?g { ?s ?p ?o } }", "text/csv"), P.Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, queried.StatusCode);

        // The data graphs alone never read the report.
        Assert.Contains("http://ex/ann", await ReadAsync(host, P.Query(Sparql, "SELECT ?s WHERE { GRAPH <" + Data + "> { ?s ?p ?o } }", "text/csv")), StringComparison.Ordinal);
    }

    /// <summary>
    /// A query whose terms only the report holds, read while the projection
    /// is behind (#183): <c>sh:focusNode</c> is no term of the data, so it is
    /// looked up first, before the report graph it is matched in. That
    /// lookup reads the reports without waiting, and must not answer "no
    /// such term" for a report it could not read yet: the query would then
    /// match nothing and answer an empty report, which reads as conforming.
    /// </summary>
    [Fact]
    public async Task a_query_of_terms_only_the_report_holds_is_validation_behind_while_the_projection_is_behind()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await FillAsync(dataset);

        // Held back: never fed, at position 0, the head at 2.
        Reports reports = new(new ValidationProjection(dataset, Binding));
        await using ProtocolTestHost host = await StartAsync(dataset, reports, Readable(["http://ex/"]));

        HttpResponseMessage behind = await host.Client.SendAsync(P.Query(Sparql, FocusQuery, "text/csv"), P.Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, behind.StatusCode);
        using JsonDocument problem = JsonDocument.Parse(await behind.Content.ReadAsStringAsync(P.Ct));
        Assert.Equal("https://w3id.org/varve/problems/validation-behind", problem.RootElement.GetProperty("type").GetString());
    }

    /// <summary>
    /// The same query, with the projection held back until the read's one
    /// bounded wait, as the server's is: the lookup that reads without
    /// waiting finds nothing, the match of the report graph waits, and the
    /// answer is the report at the position read (#183).
    /// </summary>
    [Fact]
    public async Task a_query_of_terms_only_the_report_holds_answers_the_report_once_the_wait_reaches_it()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await FillAsync(dataset);
        ValidationProjection projection = new(dataset, Binding);
        Reports reports = new(projection, wait: () => dataset.CatchUpAsync(projection, P.Ct).AsTask().GetAwaiter().GetResult());
        await using ProtocolTestHost host = await StartAsync(dataset, reports, Readable(["http://ex/"]));

        Assert.Equal("focus\r\nhttp://ex/ann\r\n", await ReadAsync(host, P.Query(Sparql, FocusQuery, "text/csv")));
        Assert.Equal(1, reports.Waits);
    }

    /// <summary>
    /// Every read of a report graph is <c>503 validation-behind</c> or the
    /// report equal to <see cref="BindingValidation.Validate"/> at its
    /// position (ADR 0174, #183), whatever the read and however far behind
    /// the projection is held: generated data commits, a projection fed to a
    /// generated position, a bounded wait that reaches the head or does not,
    /// and one of four reads, each the whole graph it names compared under
    /// RDFC-1.0. It is validation-behind exactly when the wait leaves the
    /// projection below the position read.
    /// </summary>
    [Fact]
    public void every_report_read_is_validation_behind_or_the_full_validation_at_its_position()
    {
        Gen<(int Subject, bool Assert, bool Valid)> change = Gen.Select(Gen.Int[0, 2], Gen.Bool, Gen.Bool, (subject, assert, valid) => (subject, assert, valid));
        Gen.Select(change.Array[1, 5], Gen.Double[0, 1], Gen.Bool, Gen.Int[0, ReadShapes.Length - 1], (changes, fed, reaches, read) => (changes, fed, reaches, read))
            .Sample(sample => CheckReadAsync(sample.changes, sample.fed, sample.reaches, sample.read).GetAwaiter().GetResult(), iter: 100);
    }

    private static readonly (string Name, Func<HttpRequestMessage> Request, string? Predicate)[] ReadShapes =
    [
        ("graph store", () => P.Get("datasets/d/graphs?graph=" + Uri.EscapeDataString(Report), "application/n-triples"), null),
        ("construct", () => P.Query(Sparql, "CONSTRUCT { ?s ?p ?o } WHERE { GRAPH <" + Report + "> { ?s ?p ?o } }", "application/n-triples"), null),
        ("a term only the report holds", () => P.Query(Sparql, "CONSTRUCT { ?r <" + Sh + "focusNode> ?f } WHERE { GRAPH <" + Report + "> { ?r <" + Sh + "focusNode> ?f } }", "application/n-triples"), Sh + "focusNode"),
        ("conforms", () => P.Query(Sparql, "CONSTRUCT { ?r <" + Sh + "conforms> ?c } WHERE { GRAPH <" + Report + "> { ?r <" + Sh + "conforms> ?c } }", "application/n-triples"), Sh + "conforms"),
    ];

    private static async Task CheckReadAsync((int Subject, bool Assert, bool Valid)[] changes, double fed, bool reaches, int read)
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await FillAsync(dataset);
        foreach ((int subject, bool assert, bool valid) in changes)
        {
            string triple = "<http://ex/p" + subject + "> <http://ex/age> " + (valid ? "42" : "\"old\"");
            string update = (assert ? "INSERT" : "DELETE") + " DATA { GRAPH <" + Data + "> { " + triple + " } }";
            await Varve.Sparql.Store.SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(update)), new Varve.Sparql.Store.UpdateOptions(), P.Ct);
        }

        Position head = dataset.Head;
        ValidationProjection projection = new(dataset, Binding);
        await FeedAsync(dataset, projection, new Position((long)Math.Floor(fed * head.Value)));
        Reports reports = new(projection, wait: reaches ? () => FeedAsync(dataset, projection, head).GetAwaiter().GetResult() : null);
        (string name, Func<HttpRequestMessage> request, string? predicate) = ReadShapes[read];

        await using ProtocolTestHost host = await StartAsync(dataset, reports, Readable(["http://ex/"]));
        HttpResponseMessage response = await host.Client.SendAsync(request(), P.Ct);
        string body = await response.Content.ReadAsStringAsync(P.Ct);
        if (projection.Position < head)
        {
            Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable, name + ": " + (int)response.StatusCode + " " + body);
            using JsonDocument problem = JsonDocument.Parse(body);
            Assert.Equal("https://w3id.org/varve/problems/validation-behind", problem.RootElement.GetProperty("type").GetString());
            return;
        }

        Assert.True(response.StatusCode == HttpStatusCode.OK, name + ": " + (int)response.StatusCode + " " + body);
        using DatasetView at = await dataset.AsOfAsync(head, P.Ct);
        Varve.Shacl.Model.ValidationReport expected = Assert.IsType<Varve.Shacl.Model.ValidationReport>(BindingValidation.Validate(Binding, at, null, P.Ct).Report);
        Assert.Equal(Canonical(Only(expected.ToDataset(), predicate)), Canonical(Triples(body)));
    }

    /// <summary>Feeds <paramref name="projection"/> the commits up to <paramref name="target"/>, in order.</summary>
    private static async Task FeedAsync(Dataset dataset, ValidationProjection projection, Position target)
    {
        if (projection.Position >= target)
        {
            return;
        }

        await foreach (Commit commit in dataset.Subscribe(projection.Position, SubscriptionFilter.All, P.Ct))
        {
            await projection.ApplyAsync(commit, P.Ct);
            if (commit.Position >= target)
            {
                return;
            }
        }
    }

    /// <summary>The triples of <paramref name="dataset"/>, or those of one predicate.</summary>
    private static InMemoryDataset Only(InMemoryDataset dataset, string? predicate)
    {
        InMemoryDatasetBuilder builder = new();
        using IQuadCursor cursor = dataset.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;
            dataset.TryExternalise(quad.Subject, out RdfTerm? s);
            dataset.TryExternalise(quad.Predicate, out RdfTerm? p);
            dataset.TryExternalise(quad.Object, out RdfTerm? o);
            if (predicate is null || p!.Equals(RdfTerm.Iri(Encoding.UTF8.GetBytes(predicate))))
            {
                builder.Add(s!, p!, o!);
            }
        }

        return builder.ToDataset();
    }

    private static InMemoryDataset Triples(string body)
    {
        InMemoryDatasetBuilder builder = new();
        void Add(in QuadView quad) => builder.Add(quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise());
        Varve.Turtle.ParseResult parsed = Varve.Turtle.NQuadsParser.Parse(Encoding.UTF8.GetBytes(body), Add, new Varve.Turtle.ParseOptions { Syntax = Varve.Turtle.RdfSyntax.NTriples });
        Assert.True(parsed.Succeeded, parsed.FirstError + "\n" + body);
        return builder.ToDataset();
    }

    private static string Canonical(InMemoryDataset dataset) => Encoding.UTF8.GetString(RdfCanonicaliser.Canonicalise(dataset, cancellationToken: P.Ct).NQuads.Span);

    [Fact]
    public async Task a_write_to_a_report_graph_is_graph_not_writable_for_an_administrator_too()
    {
        await using Dataset dataset = await Dataset.CreateAsync(new MemoryStorage(), new DatasetId(Guid.NewGuid()), new DatasetOptions { Clock = TimeProvider.System, Validators = [new ReportGraphGuard()] }, P.Ct);
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);

        HttpResponseMessage refused = await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <" + Report + "> { <http://ex/a> <http://ex/b> <http://ex/c> } }"), P.Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using (JsonDocument problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(P.Ct)))
        {
            Assert.Equal("https://w3id.org/varve/problems/graph-not-writable", problem.RootElement.GetProperty("type").GetString());
            Assert.Equal(Report, problem.RootElement.GetProperty("graph").GetString());
        }

        using HttpRequestMessage put = new(HttpMethod.Put, new Uri("datasets/d/graphs?graph=" + Uri.EscapeDataString(Report), UriKind.Relative))
        {
            Content = new StringContent("<http://ex/a> <http://ex/b> <http://ex/c> .\n", Encoding.UTF8, "application/n-triples"),
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(put, P.Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { GRAPH <" + Data + "> { <http://ex/a> <http://ex/b> <http://ex/c> } }"), P.Ct)).StatusCode);
    }

    /// <summary>The shapes, then a data graph with one violation: positions 1 and 2.</summary>
    private static async Task FillAsync(Dataset dataset)
    {
        foreach (string text in (string[])[
            "INSERT DATA { GRAPH <http://ex/shapes> { <http://ex/S> <" + Sh + "targetSubjectsOf> <http://ex/age> ; <" + Sh + "property> [ <" + Sh + "path> <http://ex/age> ; <" + Sh + "datatype> <http://www.w3.org/2001/XMLSchema#integer> ] } }",
            "INSERT DATA { GRAPH <" + Data + "> { <http://ex/ann> <http://ex/age> \"old\" } }",
        ])
        {
            await Varve.Sparql.Store.SparqlUpdate.ExecuteAsync(dataset, SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(text)), new Varve.Sparql.Store.UpdateOptions(), P.Ct);
        }
    }

    private static GraphScope Readable(string[] prefixes) => GraphScope.Of([], prefixes, DefaultGraphAccess.Included);

    private static Task<ProtocolTestHost> StartAsync(Dataset dataset, Reports reports, GraphScope readable) =>
        ProtocolTestHost.StartAsync(new OneNamed(dataset), accessScopes: new FixedScope(new CallerScope(readable, GraphScope.None, AdminAccess.None)), stopping: P.Ct, validationReports: reports);

    private static async Task<string> ReadAsync(ProtocolTestHost host, HttpRequestMessage request)
    {
        HttpResponseMessage response = await host.Client.SendAsync(request, P.Ct);
        string body = await response.Content.ReadAsStringAsync(P.Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        return body;
    }

    private sealed class FixedScope(CallerScope scope) : IAccessScopes
    {
        public CallerScope ScopesOf(ClaimsPrincipal caller, DatasetName dataset) => scope;
    }

    private sealed class OneNamed(Dataset dataset) : IDatasetResolver
    {
        public bool TryResolve(DatasetName name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dataset? resolved)
        {
            resolved = name.Value == "d" ? dataset : null;
            return resolved is not null;
        }
    }

    /// <summary>
    /// One projection as its report graph: a host's adapter, as the server's
    /// is. <paramref name="wait"/> stands for the server's one bounded wait,
    /// run when a waiting read finds the projection behind; none by default.
    /// </summary>
    private sealed class Reports(ValidationProjection projection, Action? wait = null) : IValidationReports, IValidationReportGraph
    {
        /// <summary>How many waiting reads found the projection behind.</summary>
        public int Waits { get; private set; }

        public RdfTerm Binding => projection.Binding.Binding;

        public RdfTerm Graph => projection.ReportGraph;

        public GraphScope Validates => projection.Binding.Validates;

        public IReadOnlyList<IValidationReportGraph> Of(DatasetName dataset) => [this];

        public bool TryReportAt(Position position, out ValidationOutcome outcome) => projection.TryReportAt(position, out outcome);

        public ValidationOutcome ReportAt(Position position)
        {
            if (projection.TryReportAt(position, out ValidationOutcome outcome))
            {
                return outcome;
            }

            Waits++;
            wait?.Invoke();
            return projection.TryReportAt(position, out outcome) ? outcome : throw new ValidationBehindException(Binding, position, projection.Position);
        }
    }
}
