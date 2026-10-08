// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Varve.Protocol.Client.Model;
using Varve.Rdf;
using Varve.Store;
using Varve.Store.Log;
using Varve.Turtle;
using Xunit;

namespace Varve.Protocol.Client.Tests;

/// <summary>The document fetch behind <c>LOAD</c> (ADR 0104): negotiation, the syntax, the base, the cap, the refusals.</summary>
public class RdfDocumentClientTests
{
    private static readonly HttpClient Http = OutboundHttp.CreateClient();

    [Fact]
    public async Task A_graph_is_fetched_in_a_syntax_the_server_writes_and_parses_to_its_quads()
    {
        await using Dataset remote = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(remote);
        RdfDocumentClient client = new(Http, C.Allowing(host), ClientLimits.Default);
        string iri = host.Address + "datasets/d/graphs?graph=http://ex/g#fragment";

        RdfDocument document = await client.FetchAsync(C.Iri(iri), C.Ct);

        Assert.False(document.IsFailure, document.Failure);
        Assert.Equal(host.Address + "datasets/d/graphs?graph=http://ex/g", Encoding.UTF8.GetString(document.BaseIri.Span));
        Assert.Equal(["<http://ex/t> <http://ex/p> \"3\""], Parse(document));
    }

    [Fact]
    public async Task A_generic_media_type_falls_back_to_the_paths_extension()
    {
        await using Dataset remote = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(remote);
        RdfDocumentClient client = new(Http, C.Allowing(host), ClientLimits.Default);

        RdfDocument document = await client.FetchAsync(C.Iri(host.Address + "plain.ttl"), C.Ct);

        Assert.False(document.IsFailure, document.Failure);
        Assert.Equal(RdfSyntax.Turtle, document.Syntax);
        Assert.Equal(host.Address + "plain.ttl", Encoding.UTF8.GetString(document.BaseIri.Span));
    }

    [Theory]
    [InlineData("json", "application/json")]
    [InlineData("datasets/d/graphs?graph=http://ex/absent", "answered 404")]
    [InlineData("redirect", "answered 405")]
    public async Task An_answer_that_is_not_a_document_is_a_failure(string path, string reason)
    {
        await using Dataset remote = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(remote);
        RdfDocumentClient client = new(Http, C.Allowing(host), ClientLimits.Default);

        RdfDocument document = await client.FetchAsync(C.Iri(host.Address + path), C.Ct);

        Assert.True(document.IsFailure);
        Assert.Contains(reason, document.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_document_over_the_cap_and_a_refused_address_are_failures()
    {
        await using Dataset remote = await C.DataAsync();
        await using ProtocolTestHost host = await C.StartAsync(remote);
        RdfDocumentClient capped = new(Http, C.Allowing(host), new ClientLimits(TimeSpan.FromSeconds(30), new ByteCount(8)));
        RdfDocumentClient refusing = new(Http, EndpointPolicy.None, ClientLimits.Default);
        RdfTerm iri = C.Iri(host.Address + "datasets/d/graphs?graph=http://ex/g");

        RdfDocument cut = await capped.FetchAsync(iri, C.Ct);
        RdfDocument refused = await refusing.FetchAsync(iri, C.Ct);
        RdfDocument notIri = await refusing.FetchAsync(RdfTerm.Literal("x"u8), C.Ct);

        Assert.Contains("8-byte cap", cut.Failure, StringComparison.Ordinal);
        Assert.Contains("allow-list is empty", refused.Failure, StringComparison.Ordinal);
        Assert.Contains("not an IRI", notIri.Failure, StringComparison.Ordinal);
    }

    private static List<string> Parse(RdfDocument document)
    {
        List<string> quads = [];
        Assert.True(document.Syntax is RdfSyntax.NTriples or RdfSyntax.NQuads, document.Syntax.ToString());
        NQuadsParser.Parse(document.Content.Span, (in QuadView quad) =>
        {
            quads.Add("<" + Encoding.UTF8.GetString(quad.Subject.Lexical) + "> <" + Encoding.UTF8.GetString(quad.Predicate.Lexical) + "> \"" + Encoding.UTF8.GetString(quad.Object.Lexical) + "\"");
        }, new ParseOptions { Syntax = document.Syntax });
        return quads;
    }
}
