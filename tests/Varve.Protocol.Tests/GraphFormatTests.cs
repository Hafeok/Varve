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
using System.Threading.Tasks;
using Varve.JsonLd;
using Varve.Protocol.Model;
using Varve.Rdf;
using Varve.RdfXml;
using Varve.Sparql.Evaluation;
using Varve.Store;
using Varve.Store.Log;
using Varve.Turtle;
using Xunit;

namespace Varve.Protocol.Tests;

/// <summary>
/// The six graph formats over a real socket (ADR 0092, amended 2026-10-12):
/// every graph a Graph Store read, a CONSTRUCT or the service description
/// writes is negotiated among N-Triples, Turtle, N-Quads, TriG, RDF/XML and
/// JSON-LD, and a Graph Store write reads each. RDF 1.2 terms travel where
/// the syntax has them; where it has not, the answer is a 406 that says
/// why, before the first byte.
/// </summary>
public class GraphFormatTests
{
    private const string Sparql = "datasets/d/sparql";
    private const string Ex = "http://ex/";

    public static TheoryData<string> Formats() => [.. AllFormats];

    private static readonly string[] AllFormats =
    [
        "application/n-triples", "text/turtle", "application/n-quads", "application/trig", "application/rdf+xml", "application/ld+json",
    ];

    // Each format's spelling of one graph: two IRIs, an integer, a
    // language-tagged literal and an RDF 1.2 directional one.
    private static readonly Dictionary<string, string> Bodies = new(StringComparer.Ordinal)
    {
        ["application/n-triples"] = "<http://ex/s> <http://ex/p> <http://ex/o> .\n<http://ex/s> <http://ex/n> \"1\"^^<http://www.w3.org/2001/XMLSchema#integer> .\n<http://ex/s> <http://ex/l> \"chat\"@fr .\n<http://ex/s> <http://ex/d> \"x\"@ar--rtl .\n",
        ["text/turtle"] = "PREFIX : <http://ex/>\n:s :p :o ; :n 1 ; :l \"chat\"@fr ; :d \"x\"@ar--rtl .\n",
        ["application/n-quads"] = "<http://ex/s> <http://ex/p> <http://ex/o> .\n<http://ex/s> <http://ex/n> \"1\"^^<http://www.w3.org/2001/XMLSchema#integer> .\n<http://ex/s> <http://ex/l> \"chat\"@fr .\n<http://ex/s> <http://ex/d> \"x\"@ar--rtl .\n",
        ["application/trig"] = "PREFIX : <http://ex/>\n{ :s :p :o ; :n 1 ; :l \"chat\"@fr ; :d \"x\"@ar--rtl . }\n",
        ["application/rdf+xml"] = "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:its=\"http://www.w3.org/2005/11/its\" xmlns:ex=\"http://ex/\" rdf:version=\"1.2\">"
            + "<rdf:Description rdf:about=\"http://ex/s\"><ex:p rdf:resource=\"http://ex/o\"/>"
            + "<ex:n rdf:datatype=\"http://www.w3.org/2001/XMLSchema#integer\">1</ex:n><ex:l xml:lang=\"fr\">chat</ex:l>"
            + "<ex:d xml:lang=\"ar\" its:dir=\"rtl\">x</ex:d></rdf:Description></rdf:RDF>",
        ["application/ld+json"] = "{\"@context\": {\"ex\": \"http://ex/\"}, \"@id\": \"ex:s\", \"ex:p\": {\"@id\": \"ex:o\"}, \"ex:n\": 1,"
            + " \"ex:l\": {\"@value\": \"chat\", \"@language\": \"fr\"}, \"ex:d\": {\"@value\": \"x\", \"@language\": \"ar\", \"@direction\": \"rtl\"}}",
    };

    private static readonly SortedSet<string> Expected = new(StringComparer.Ordinal)
    {
        "<http://ex/s> <http://ex/p> <http://ex/o>",
        "<http://ex/s> <http://ex/n> \"1\"^^<http://www.w3.org/2001/XMLSchema#integer>",
        "<http://ex/s> <http://ex/l> \"chat\"@fr",
        "<http://ex/s> <http://ex/d> \"x\"@ar--rtl",
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task a_graph_written_in_one_format_reads_back_the_same_in_every_format(string written)
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        string graph = "datasets/d/graphs?graph=" + Uri.EscapeDataString(Ex + "g");

        HttpResponseMessage put = await host.Client.PutAsync(new Uri(graph, UriKind.Relative), new StringContent(Bodies[written], Encoding.UTF8, written), P.Ct);
        Assert.True(put.StatusCode == HttpStatusCode.Created, written + ": " + await put.Content.ReadAsStringAsync(P.Ct));

        foreach (string read in AllFormats)
        {
            HttpResponseMessage response = await host.Client.SendAsync(P.Get(graph, read), P.Ct);
            string body = await response.Content.ReadAsStringAsync(P.Ct);
            Assert.True(response.StatusCode == HttpStatusCode.OK, written + " read as " + read + ": " + body);
            Assert.Equal(read, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("Accept,Varve-As-Of,Authorization,Varve-Entailment", string.Join(",", response.Headers.Vary));
            Assert.Equal(Expected, Parse(read, body, Ex));
        }
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task a_construct_is_written_in_the_format_accept_chooses(string format)
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/s> <http://ex/p> <http://ex/o> ; <http://ex/n> 1 ; <http://ex/l> \"chat\"@fr ; <http://ex/d> \"x\"@ar--rtl }"), P.Ct);

        HttpResponseMessage response = await host.Client.SendAsync(P.Query(Sparql, "CONSTRUCT WHERE { ?s ?p ?o }", accept: format), P.Ct);
        string body = await response.Content.ReadAsStringAsync(P.Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, format + ": " + body);
        Assert.Equal(format, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Accept,Varve-As-Of,Authorization,Varve-Entailment", string.Join(",", response.Headers.Vary));
        Assert.Equal(Expected, Parse(format, body, Ex));
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public async Task the_service_description_is_written_in_every_format_and_names_every_format(string format)
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);

        HttpResponseMessage response = await host.Client.SendAsync(P.Get("datasets/d/", format), P.Ct);
        string body = await response.Content.ReadAsStringAsync(P.Ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, format + ": " + body);
        Assert.Equal(format, response.Content.Headers.ContentType?.MediaType);
        SortedSet<string> lines = Parse(format, body, host.Address.ToString());

        foreach (string named in (string[])["N-Triples", "Turtle", "N-Quads", "TriG", "RDF_XML", "JSON-LD"])
        {
            Assert.Contains(lines, l => l.EndsWith("<http://www.w3.org/ns/sparql-service-description#resultFormat> <http://www.w3.org/ns/formats/" + named + ">", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.EndsWith("<http://www.w3.org/ns/sparql-service-description#inputFormat> <http://www.w3.org/ns/formats/" + named + ">", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("application/rdf+xml")]
    [InlineData("application/ld+json")]
    public async Task the_empty_default_graph_is_an_empty_document_not_an_empty_body(string format)
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);

        HttpResponseMessage response = await host.Client.SendAsync(P.Get("datasets/d/graphs?default", format), P.Ct);
        string body = await response.Content.ReadAsStringAsync(P.Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(body);
        Assert.Empty(Parse(format, body, Ex));

        HttpResponseMessage construct = await host.Client.SendAsync(P.Query(Sparql, "CONSTRUCT WHERE { ?s ?p ?o }", accept: format), P.Ct);
        Assert.Empty(Parse(format, await construct.Content.ReadAsStringAsync(P.Ct), Ex));
    }

    [Theory]
    [InlineData("application/n-triples")]
    [InlineData("text/turtle")]
    [InlineData("application/n-quads")]
    [InlineData("application/trig")]
    [InlineData("application/rdf+xml")]
    public async Task a_triple_term_travels_in_every_syntax_that_has_one(string format)
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "VERSION \"1.2\" INSERT DATA { <http://ex/a> <http://ex/says> <<( <http://ex/s> <http://ex/p> \"x\"@ar--rtl )>> }"), P.Ct);

        HttpResponseMessage read = await host.Client.SendAsync(P.Get("datasets/d/graphs?default", format), P.Ct);
        string body = await read.Content.ReadAsStringAsync(P.Ct);
        Assert.True(read.StatusCode == HttpStatusCode.OK, format + ": " + body);
        Assert.Equal(["<http://ex/a> <http://ex/says> <<( <http://ex/s> <http://ex/p> \"x\"@ar--rtl )>>"], Parse(format, body, Ex));

        if (format == "application/rdf+xml")
        {
            Assert.Contains("rdf:version=\"1.2\"", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task json_ld_refuses_a_triple_term_with_a_406_that_names_it_before_the_first_byte()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "VERSION \"1.2\" INSERT DATA { <http://ex/a> <http://ex/says> <<( <http://ex/s> <http://ex/p> <http://ex/o> )>> }"), P.Ct);

        foreach (HttpRequestMessage request in (HttpRequestMessage[])[P.Get("datasets/d/graphs?default", "application/ld+json"), P.Query(Sparql, "CONSTRUCT WHERE { ?s ?p ?o }", accept: "application/ld+json")])
        {
            HttpResponseMessage response = await host.Client.SendAsync(request, P.Ct);
            await AssertProblemAsync(response, ProblemType.NotAcceptable, "triple term");
        }
    }

    [Fact]
    public async Task rdf_xml_refuses_a_predicate_with_no_qualified_name_with_a_406_that_names_it()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/s> <http://ex/p/> <http://ex/o> }"), P.Ct);

        foreach (HttpRequestMessage request in (HttpRequestMessage[])[P.Get("datasets/d/graphs?default", "application/rdf+xml"), P.Query(Sparql, "CONSTRUCT WHERE { ?s ?p ?o }", accept: "application/rdf+xml")])
        {
            HttpResponseMessage response = await host.Client.SendAsync(request, P.Ct);
            await AssertProblemAsync(response, ProblemType.NotAcceptable, "qualified name");
        }

        HttpResponseMessage turtle = await host.Client.SendAsync(P.Get("datasets/d/graphs?default", "text/turtle"), P.Ct);
        Assert.Equal(HttpStatusCode.OK, turtle.StatusCode);
    }

    [Fact]
    public async Task an_rdf_xml_refusal_after_the_first_byte_is_the_cut_and_never_a_whole_looking_document()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { " + string.Concat(Enumerable.Range(0, 3000).Select(i => "<http://ex/s" + i + "> <http://ex/p> \"" + i + "\" . ")) + "}"), P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { <http://ex/z> <http://ex/p/> <http://ex/o> }"), P.Ct);

        // Over HTTP/1.1 there are no trailers: the connection is aborted
        // (ADR 0095), so the client never holds a closed rdf:RDF.
        await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
        {
            HttpResponseMessage response = await host.Client.SendAsync(P.Get("datasets/d/graphs?default", "application/rdf+xml"), P.Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await response.Content.ReadAsByteArrayAsync(P.Ct);
        });
    }

    [Fact]
    public async Task json_ld_holds_the_graph_against_the_memory_limit_and_says_so_before_the_first_byte()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, limits: new ProtocolLimits(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), new ByteCount(1L << 30), new ByteCount(100L << 20), TimeSpan.FromSeconds(15)) { MaxQueryMemory = new MemoryBytes(4096) }, stopping: P.Ct);
        await host.Client.SendAsync(P.Update(Sparql, "INSERT DATA { " + string.Concat(Enumerable.Range(0, 200).Select(i => "<http://ex/s" + i + "> <http://ex/p> \"" + new string('x', 16) + "\" . ")) + "}"), P.Ct);

        HttpResponseMessage json = await host.Client.SendAsync(P.Get("datasets/d/graphs?default", "application/ld+json"), P.Ct);
        await AssertProblemAsync(json, ProblemType.MemoryLimitExceeded, "budget");

        // The streaming syntaxes hold nothing per triple and answer.
        foreach (string format in (string[])["application/n-triples", "application/rdf+xml"])
        {
            HttpResponseMessage streamed = await host.Client.SendAsync(P.Get("datasets/d/graphs?default", format), P.Ct);
            Assert.Equal(HttpStatusCode.OK, streamed.StatusCode);
            Assert.Equal(200, Parse(format, await streamed.Content.ReadAsStringAsync(P.Ct), Ex).Count);
        }
    }

    [Fact]
    public async Task a_json_ld_body_naming_a_graph_or_a_remote_context_is_refused_and_commits_nothing()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);

        foreach (string body in (string[])[
            "{\"@id\": \"http://ex/g\", \"@graph\": [{\"@id\": \"http://ex/s\", \"http://ex/p\": 1}]}",
            "{\"@context\": \"http://ex/context.jsonld\", \"@id\": \"http://ex/s\", \"p\": 1}"])
        {
            HttpResponseMessage response = await host.Client.PutAsync(new Uri("datasets/d/graphs?default", UriKind.Relative), new StringContent(body, Encoding.UTF8, "application/ld+json"), P.Ct);
            await AssertProblemAsync(response, ProblemType.RdfSyntax, null);
        }

        HttpResponseMessage xml = await host.Client.PutAsync(new Uri("datasets/d/graphs?default", UriKind.Relative), new StringContent("<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description", Encoding.UTF8, "application/rdf+xml"), P.Ct);
        await AssertProblemAsync(xml, ProblemType.RdfSyntax, null);
        Assert.Equal(new Position(0), dataset.Head);
    }

    [Fact]
    public async Task a_format_nothing_reads_is_still_415_and_one_nothing_writes_406()
    {
        await using Dataset dataset = await P.NewDatasetAsync();
        await using ProtocolTestHost host = await ProtocolTestHost.StartAsync(dataset, stopping: P.Ct);

        HttpResponseMessage put = await host.Client.PutAsync(new Uri("datasets/d/graphs?default", UriKind.Relative), new StringContent("s,p,o", Encoding.UTF8, "text/csv"), P.Ct);
        await AssertProblemAsync(put, ProblemType.UnsupportedMediaType, "JSON-LD");

        HttpResponseMessage get = await host.Client.SendAsync(P.Get("datasets/d/graphs?default", "text/html"), P.Ct);
        await AssertProblemAsync(get, ProblemType.NotAcceptable, "RDF/XML or JSON-LD");
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, ProblemType type, string? detail)
    {
        string body = await response.Content.ReadAsStringAsync(P.Ct);
        Assert.True(ProblemCatalogue.Of(type).Status == (int)response.StatusCode, type.Value + ": " + (int)response.StatusCode + " " + body);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Accept,Varve-As-Of,Authorization,Varve-Entailment", string.Join(",", response.Headers.Vary));
        using JsonDocument problem = JsonDocument.Parse(body);
        Assert.Equal(type.Value, problem.RootElement.GetProperty("type").GetString());

        if (detail is not null)
        {
            Assert.Contains(detail, problem.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
        }
    }

    /// <summary>A graph response, read back by Varve's own reader of its format, as N-Triples lines without the final dot.</summary>
    private static SortedSet<string> Parse(string format, string body, string baseIri)
    {
        SortedSet<string> lines = new(StringComparer.Ordinal);
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        byte[] @base = Encoding.UTF8.GetBytes(baseIri);

        void Add(in QuadView quad)
        {
            Assert.False(quad.HasGraph, "a graph response names a graph");
            lines.Add(P.Line(quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise(), null));
        }

        switch (format)
        {
            case "application/rdf+xml":
                RdfXmlParseResult xml = RdfXmlParser.Parse(bytes, Add, new RdfXmlOptions { BaseIri = @base });
                Assert.True(xml.Succeeded, xml.Error + "\n" + body);
                break;
            case "application/ld+json":
                JsonLdResult json = JsonLdParser.Parse(bytes, Add, new JsonLdOptions { BaseIri = @base });
                Assert.True(json.Succeeded, json.Error + "\n" + body);
                break;
            case "text/turtle":
            case "application/trig":
                ParseResult turtle = TurtleParser.Parse(bytes, Add, new TurtleOptions { Syntax = format == "text/turtle" ? RdfSyntax.Turtle : RdfSyntax.TriG, BaseIri = @base });
                Assert.True(turtle.Succeeded, turtle.FirstError + "\n" + body);
                break;
            default:
                ParseResult lines2 = NQuadsParser.Parse(bytes, Add, new ParseOptions { Syntax = format == "application/n-triples" ? RdfSyntax.NTriples : RdfSyntax.NQuads });
                Assert.True(lines2.Succeeded, lines2.FirstError + "\n" + body);
                break;
        }

        return lines;
    }
}
