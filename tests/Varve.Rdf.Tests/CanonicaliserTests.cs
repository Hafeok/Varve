// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Varve.Rdf.Tests;

/// <summary>
/// The canonicaliser's surface (<c>rdf-canon.md</c> §2–§4); the algorithm is
/// gated by the W3C suite and the properties in the conformance project.
/// </summary>
public class CanonicaliserTests
{
    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    private static RdfTerm Iri(string local) => RdfTerm.Iri(U("http://example.org/" + local));

    private static string Canonical(InMemoryDataset dataset, CanonicalisationOptions? options = null) =>
        Encoding.UTF8.GetString(RdfCanonicaliser.Canonicalise(dataset, options, TestContext.Current.CancellationToken).NQuads.Span);

    [Fact]
    public void The_example_of_4_6_2_hashes_as_the_recommendation_says()
    {
        // §4.6.2: e0's first-degree hash under SHA-256.
        InMemoryDatasetBuilder builder = new();
        builder.Add(RdfTerm.Iri(U("http://example.com/#p")), RdfTerm.Iri(U("http://example.com/#q")), RdfTerm.BlankNode(U("e0")));
        builder.Add(RdfTerm.BlankNode(U("e0")), RdfTerm.Iri(U("http://example.com/#s")), RdfTerm.Iri(U("http://example.com/#u")));
        InMemoryDataset dataset = builder.ToDataset();
        string line = "<http://example.com/#p> <http://example.com/#q> _:a .\n_:a <http://example.com/#s> <http://example.com/#u> .\n";

        Assert.Equal("21d1dd5ba21f3dee9d76c0c00c260fa6f5d5d65315099e553026f4828d0dc77a", Convert.ToHexStringLower(SHA256.HashData(U(line))));
        Assert.Equal(
            "<http://example.com/#p> <http://example.com/#q> _:c14n0 .\n_:c14n0 <http://example.com/#s> <http://example.com/#u> .\n",
            Canonical(dataset));
        Assert.Equal("c14n0", RdfCanonicaliser.Canonicalise(dataset, cancellationToken: TestContext.Current.CancellationToken).IssuedIdentifiers["e0"]);
    }

    [Fact]
    public void Appendix_a_escapes_and_lowercases()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Iri("s"), Iri("p"), RdfTerm.Literal(U("\b\t\n\f\r\"\\\u0001\u000B\u001F\u007F\uFFFE é")));
        builder.Add(Iri("s"), Iri("p"), RdfTerm.Literal(U("x"), U("EN-gb"), TextDirection.RightToLeft));
        builder.Add(Iri("s"), Iri("p"), RdfTerm.Literal(U("1"), RdfTerm.Iri(U("http://www.w3.org/2001/XMLSchema#integer"))), Iri("g"));
        builder.Add(Iri("s"), Iri("p"), RdfTerm.TripleTerm(Iri("a"), Iri("b"), RdfTerm.Literal(U("c"))));
        InMemoryDataset dataset = builder.ToDataset();

        Assert.Equal(
            "<http://example.org/s> <http://example.org/p> \"1\"^^<http://www.w3.org/2001/XMLSchema#integer> <http://example.org/g> .\n"
            + "<http://example.org/s> <http://example.org/p> \"\\b\\t\\n\\f\\r\\\"\\\\\\u0001\\u000B\\u001F\\u007F\\uFFFE é\" .\n"
            + "<http://example.org/s> <http://example.org/p> \"x\"@en-gb--rtl .\n"
            + "<http://example.org/s> <http://example.org/p> <<( <http://example.org/a> <http://example.org/b> \"c\" )>> .\n",
            Canonical(dataset));
    }

    [Fact]
    public void Sha_384_and_512_are_selectable_and_others_are_not()
    {
        Assert.Equal(HashAlgorithmName.SHA384, new CanonicalisationOptions { HashAlgorithm = HashAlgorithmName.SHA384 }.HashAlgorithm);
        Assert.Equal(HashAlgorithmName.SHA512, new CanonicalisationOptions { HashAlgorithm = HashAlgorithmName.SHA512 }.HashAlgorithm);
        Assert.Throws<ArgumentException>(() => new CanonicalisationOptions { HashAlgorithm = HashAlgorithmName.MD5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CanonicalisationOptions { WorkLimit = 0 });
    }

    [Fact]
    public void A_triple_term_around_a_blank_node_is_refused()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Iri("s"), Iri("p"), RdfTerm.TripleTerm(RdfTerm.BlankNode(U("b")), Iri("q"), Iri("o")));
        InMemoryDataset dataset = builder.ToDataset();

        ArgumentException error = Assert.Throws<ArgumentException>(() => Canonical(dataset));
        Assert.Contains("RDF 1.1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_symmetric_dataset_meets_a_small_limit_and_says_so()
    {
        // A four-clique: every first-degree hash equal, everything to Hash N-Degree Quads.
        InMemoryDatasetBuilder builder = new();
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                if (i != j)
                {
                    builder.Add(RdfTerm.BlankNode(U("n" + i)), Iri("p"), RdfTerm.BlankNode(U("n" + j)));
                }
            }
        }

        InMemoryDataset dataset = builder.ToDataset();
        CanonicalisationLimitException error = Assert.Throws<CanonicalisationLimitException>(() => Canonical(dataset, new CanonicalisationOptions { WorkLimit = 1 }));
        Assert.Equal(4, error.Limit);
        Assert.True(error.Steps > error.Limit);
        Assert.Contains("_:c14n3", Canonical(dataset), StringComparison.Ordinal);
    }

    [Fact]
    public void Cancellation_stops_it()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(RdfTerm.BlankNode(U("a")), Iri("p"), RdfTerm.BlankNode(U("b")));
        builder.Add(RdfTerm.BlankNode(U("b")), Iri("p"), RdfTerm.BlankNode(U("a")));
        InMemoryDataset dataset = builder.ToDataset();
        using System.Threading.CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => RdfCanonicaliser.Canonicalise(dataset, null, cancelled.Token));
    }
}
