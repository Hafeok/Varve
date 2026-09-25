// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Query;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Evaluation;
using Varve.Store;
using Varve.Turtle;

namespace Varve.Benchmarks;

/// <summary>
/// A workload in the shape of the Berlin SPARQL Benchmark's explore use case:
/// products with types, features, producers and numeric and textual
/// properties; offers with prices, vendors and dates; reviews with ratings.
/// Generated from a fixed seed, so every engine reads the same N-Triples. Not
/// BSBM itself — its generator is a Java tool and its query mix is
/// parameterised per run — but the same schema and the same query shapes with
/// fixed parameters, which is what a comparison across engines needs.
/// </summary>
internal static class Bsbm
{
    internal const int Products = 2_000;

    private const string B = "http://www4.wiwiss.fu-berlin.de/bizer/bsbm/v01/vocabulary/";
    private const string I = "http://www4.wiwiss.fu-berlin.de/bizer/bsbm/v01/instances/";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";
    private const string Xsd = "http://www.w3.org/2001/XMLSchema#";
    private const string Dc = "http://purl.org/dc/elements/1.1/";
    private const string Rev = "http://purl.org/stuff/rev#";
    private const string Foaf = "http://xmlns.com/foaf/0.1/";

    private const string Prefixes =
        "PREFIX bsbm: <" + B + ">\nPREFIX inst: <" + I + ">\nPREFIX rdf: <" + Rdf + ">\nPREFIX rdfs: <" + Rdfs + ">\n"
        + "PREFIX xsd: <" + Xsd + ">\nPREFIX dc: <" + Dc + ">\nPREFIX rev: <" + Rev + ">\nPREFIX foaf: <" + Foaf + ">\n";

    /// <summary>The query mix: BSBM explore's shapes with fixed parameters, and one aggregate.</summary>
    internal static readonly (string Name, string Text)[] Queries =
    [
        ("Q1 type+feature+numeric", Prefixes + """
            SELECT DISTINCT ?product ?label WHERE {
              ?product rdfs:label ?label ; a inst:ProductType7 ; bsbm:productFeature inst:ProductFeature12 ;
                       bsbm:productPropertyNumeric1 ?value1 .
              FILTER (?value1 > 300)
            } ORDER BY ?label LIMIT 10
            """),
        ("Q2 product details", Prefixes + """
            SELECT ?label ?comment ?producer ?f ?p1 ?p2 ?t1 ?t2 WHERE {
              inst:Product17 rdfs:label ?label ; rdfs:comment ?comment ; bsbm:producer ?p .
              ?p rdfs:label ?producer .
              inst:Product17 bsbm:productFeature ?feature . ?feature rdfs:label ?f .
              inst:Product17 bsbm:productPropertyNumeric1 ?p1 ; bsbm:productPropertyNumeric2 ?p2 ; bsbm:productPropertyTextual1 ?t1 .
              OPTIONAL { inst:Product17 bsbm:productPropertyTextual2 ?t2 }
            }
            """),
        ("Q3 negation by OPTIONAL", Prefixes + """
            SELECT ?product ?label WHERE {
              ?product rdfs:label ?label ; a inst:ProductType3 ; bsbm:productFeature inst:ProductFeature20 ;
                       bsbm:productPropertyNumeric1 ?p1 ; bsbm:productPropertyNumeric3 ?p3 .
              FILTER (?p1 > 100 && ?p3 < 900)
              OPTIONAL { ?product bsbm:productFeature inst:ProductFeature21 . ?product rdfs:label ?testVar }
              FILTER (!bound(?testVar))
            } ORDER BY ?label LIMIT 10
            """),
        ("Q4 union", Prefixes + """
            SELECT DISTINCT ?product ?label ?propertyTextual WHERE {
              { ?product rdfs:label ?label ; a inst:ProductType5 ; bsbm:productFeature inst:ProductFeature30 ;
                         bsbm:productPropertyTextual1 ?propertyTextual ; bsbm:productPropertyNumeric1 ?p1 . FILTER (?p1 > 200) }
              UNION
              { ?product rdfs:label ?label ; a inst:ProductType5 ; bsbm:productFeature inst:ProductFeature31 ;
                         bsbm:productPropertyTextual1 ?propertyTextual ; bsbm:productPropertyNumeric2 ?p2 . FILTER (?p2 > 300) }
            } ORDER BY ?label OFFSET 5 LIMIT 10
            """),
        ("Q5 similar products", Prefixes + """
            SELECT DISTINCT ?product ?productLabel WHERE {
              ?product rdfs:label ?productLabel . FILTER (inst:Product17 != ?product)
              inst:Product17 bsbm:productFeature ?prodFeature . ?product bsbm:productFeature ?prodFeature .
              inst:Product17 bsbm:productPropertyNumeric1 ?origProperty1 . ?product bsbm:productPropertyNumeric1 ?simProperty1 .
              FILTER (?simProperty1 < (?origProperty1 + 120) && ?simProperty1 > (?origProperty1 - 120))
              inst:Product17 bsbm:productPropertyNumeric2 ?origProperty2 . ?product bsbm:productPropertyNumeric2 ?simProperty2 .
              FILTER (?simProperty2 < (?origProperty2 + 170) && ?simProperty2 > (?origProperty2 - 170))
            } ORDER BY ?productLabel LIMIT 5
            """),
        ("Q7 offers and reviews", Prefixes + """
            SELECT ?productLabel ?offer ?price ?vendor ?vendorTitle ?review ?revTitle ?reviewer ?revName ?rating1 ?rating2 WHERE {
              inst:Product17 rdfs:label ?productLabel .
              OPTIONAL {
                ?offer bsbm:product inst:Product17 ; bsbm:price ?price ; bsbm:vendor ?vendor ; bsbm:validTo ?date .
                ?vendor rdfs:label ?vendorTitle ; bsbm:country <http://downlode.org/rdf/iso-3166/countries#DE> .
                FILTER (?date > "2008-06-20"^^xsd:date)
              }
              OPTIONAL {
                ?review bsbm:reviewFor inst:Product17 ; rev:reviewer ?reviewer ; dc:title ?revTitle .
                ?reviewer foaf:name ?revName .
                OPTIONAL { ?review bsbm:rating1 ?rating1 }
                OPTIONAL { ?review bsbm:rating2 ?rating2 }
              }
            }
            """),
        ("Q8 recent reviews", Prefixes + """
            SELECT ?title ?text ?reviewDate ?reviewer ?reviewerName ?rating1 ?rating2 WHERE {
              ?review bsbm:reviewFor inst:Product17 ; dc:title ?title ; rev:text ?text ; bsbm:reviewDate ?reviewDate ;
                      rev:reviewer ?reviewer .
              ?reviewer foaf:name ?reviewerName .
              OPTIONAL { ?review bsbm:rating1 ?rating1 }
              OPTIONAL { ?review bsbm:rating2 ?rating2 }
            } ORDER BY DESC(?reviewDate) LIMIT 20
            """),
        ("Q10 cheap offers", Prefixes + """
            SELECT DISTINCT ?offer ?price WHERE {
              ?offer bsbm:product ?product ; bsbm:vendor ?vendor ; bsbm:deliveryDays ?deliveryDays ;
                     bsbm:price ?price ; bsbm:validTo ?date .
              ?vendor bsbm:country <http://downlode.org/rdf/iso-3166/countries#US> .
              FILTER (?deliveryDays <= 3 && ?date > "2008-06-20"^^xsd:date)
            } ORDER BY xsd:double(str(?price)) LIMIT 10
            """),
        ("Qa ratings by producer", Prefixes + """
            SELECT ?producer (COUNT(?review) AS ?reviews) (AVG(?rating) AS ?average) WHERE {
              ?review bsbm:reviewFor ?product ; bsbm:rating1 ?rating . ?product bsbm:producer ?producer .
            } GROUP BY ?producer ORDER BY DESC(?reviews) ?producer LIMIT 10
            """),
    ];

    /// <summary>The dataset as N-Triples, generated once.</summary>
    internal static byte[] Data => field ??= Generate();

    private static byte[] Generate()
    {
        Random random = new(20260925);
        StringBuilder text = new();
        void Triple(string s, string p, string o) => text.Append('<').Append(s).Append("> <").Append(p).Append("> ").Append(o).Append(" .\n");
        static string Iri(string iri) => "<" + iri + ">";
        static string Lit(string value) => "\"" + value + "\"";
        static string Typed(string value, string type) => "\"" + value + "\"^^<" + Xsd + type + ">";
        string[] countries = ["US", "DE", "GB", "FR", "JP", "CN", "RU", "AT", "KR", "ES"];
        string Country() => Iri("http://downlode.org/rdf/iso-3166/countries#" + countries[random.Next(countries.Length)]);

        for (int t = 0; t < 50; t++)
        {
            Triple(I + "ProductType" + t, Rdf + "type", Iri(B + "ProductType"));
            Triple(I + "ProductType" + t, Rdfs + "label", Lit("product type " + t));
        }

        for (int f = 0; f < 500; f++)
        {
            Triple(I + "ProductFeature" + f, Rdf + "type", Iri(B + "ProductFeature"));
            Triple(I + "ProductFeature" + f, Rdfs + "label", Lit("feature " + f));
        }

        for (int p = 0; p < 40; p++)
        {
            Triple(I + "Producer" + p, Rdfs + "label", Lit("producer " + p));
            Triple(I + "Producer" + p, B + "country", Country());
        }

        for (int v = 0; v < 100; v++)
        {
            Triple(I + "Vendor" + v, Rdfs + "label", Lit("vendor " + v));
            Triple(I + "Vendor" + v, B + "country", Country());
        }

        for (int r = 0; r < 500; r++)
        {
            Triple(I + "Reviewer" + r, Foaf + "name", Lit("reviewer " + r));
            Triple(I + "Reviewer" + r, B + "country", Country());
        }

        int offer = 0, review = 0;
        for (int i = 0; i < Products; i++)
        {
            string product = I + "Product" + i;
            Triple(product, Rdf + "type", Iri(B + "Product"));
            Triple(product, Rdf + "type", Iri(I + "ProductType" + random.Next(10)));
            Triple(product, Rdfs + "label", Lit("product " + Word(random) + " " + i));
            Triple(product, Rdfs + "comment", Lit(Word(random) + " " + Word(random) + " " + Word(random)));
            Triple(product, B + "producer", Iri(I + "Producer" + random.Next(40)));
            HashSet<int> features = [];
            while (features.Count < 8)
            {
                features.Add(random.Next(50));
            }

            foreach (int f in features)
            {
                Triple(product, B + "productFeature", Iri(I + "ProductFeature" + f));
            }

            for (int n = 1; n <= 3; n++)
            {
                Triple(product, B + "productPropertyNumeric" + n, Typed(random.Next(1, 1000).ToString(CultureInfo.InvariantCulture), "integer"));
            }

            Triple(product, B + "productPropertyTextual1", Lit(Word(random) + " " + Word(random)));
            if (random.Next(2) == 0)
            {
                Triple(product, B + "productPropertyTextual2", Lit(Word(random)));
            }

            for (int o = 0; o < 10; o++, offer++)
            {
                string node = I + "Offer" + offer;
                Triple(node, B + "product", Iri(product));
                Triple(node, B + "vendor", Iri(I + "Vendor" + random.Next(100)));
                Triple(node, B + "price", Typed((random.Next(500, 1_000_000) / 100m).ToString("0.00", CultureInfo.InvariantCulture), "decimal"));
                Triple(node, B + "validTo", Typed(new DateOnly(2008, 1, 1).AddDays(random.Next(365)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "date"));
                Triple(node, B + "deliveryDays", Typed(random.Next(1, 8).ToString(CultureInfo.InvariantCulture), "integer"));
            }

            for (int r = 0; r < 5; r++, review++)
            {
                string node = I + "Review" + review;
                Triple(node, B + "reviewFor", Iri(product));
                Triple(node, Rev + "reviewer", Iri(I + "Reviewer" + random.Next(500)));
                Triple(node, Dc + "title", Lit("review " + Word(random)));
                Triple(node, Rev + "text", Lit(Word(random) + " " + Word(random) + " " + Word(random) + " " + Word(random)));
                Triple(node, B + "reviewDate", Typed(new DateOnly(2008, 1, 1).AddDays(random.Next(365)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00", "dateTime"));
                if (random.Next(4) != 0)
                {
                    Triple(node, B + "rating1", Typed(random.Next(1, 11).ToString(CultureInfo.InvariantCulture), "integer"));
                }

                if (random.Next(4) != 0)
                {
                    Triple(node, B + "rating2", Typed(random.Next(1, 11).ToString(CultureInfo.InvariantCulture), "integer"));
                }
            }
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static readonly string[] Words = ["alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel", "india", "juliet", "kilo", "lima", "mike", "november", "oscar", "papa"];

    private static string Word(Random random) => Words[random.Next(Words.Length)];

    /// <summary>Writes the data and the query mix for <c>oxigraph/bsbm.py</c>.</summary>
    internal static void Export(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "bsbm.nt"), Data);
        File.WriteAllText(Path.Combine(directory, "queries.json"), JsonSerializer.Serialize(Queries.Select(q => new[] { q.Name, q.Text })));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Data.Length:N0} bytes, {Data.Count(b => b == (byte)'\n'):N0} triples, {Queries.Length} queries, in {directory}"));
    }
}

/// <summary>
/// The BSBM-style query mix: Varve over the store's pinned view and over
/// <see cref="Varve.Rdf.InMemoryDataset"/>, against dotNetRDF's Leviathan
/// engine over its in-memory triple store, on the same N-Triples. The answer's
/// row count is checked to agree across the three at setup.
/// </summary>
[MemoryDiagnoser]
public class BsbmBenchmarks : IDisposable
{
    private Varve.Store.Dataset _store = null!;
    private DatasetView _view = null!;
    private Varve.Rdf.InMemoryDataset _dataset = null!;
    private Varve.Sparql.Algebra.Query _varveQuery = null!;
    private readonly SparqlEvaluator _evaluator = new();
    private LeviathanQueryProcessor _processor = null!;
    private SparqlQuery _dotNetRdfQuery = null!;

    /// <summary>The query mix, or the part of it named in <c>VARVE_BSBM_QUERIES</c> (comma-separated), to re-run one query without the rest.</summary>
    public static IEnumerable<string> Names =>
        Environment.GetEnvironmentVariable("VARVE_BSBM_QUERIES") is { Length: > 0 } only
            ? Bsbm.Queries.Select(q => q.Name).Where(n => only.Split(',').Contains(n))
            : Bsbm.Queries.Select(q => q.Name);

    [ParamsSource(nameof(Names))]
    public string Query { get; set; } = "";

    [GlobalSetup]
    public void Load()
    {
        string text = Bsbm.Queries.Single(q => q.Name == Query).Text;
        _varveQuery = SparqlParser.ParseQuery(text.AsSpan());

        _dataset = new Varve.Rdf.InMemoryDataset();
        CommitRequest request = new();
        Varve.Turtle.NQuadsParser.Parse(
            Bsbm.Data,
            (in QuadView quad) =>
            {
                RdfTerm s = quad.Subject.Materialise(), p = quad.Predicate.Materialise(), o = quad.Object.Materialise();
                _dataset.Add(s, p, o);
                request.Assert(s, p, o);
            },
            new ParseOptions { Syntax = RdfSyntax.NTriples });
        _store = Varve.Store.Dataset.OpenAsync(new MemoryStorage(), new DatasetOptions { Clock = TimeProvider.System }).AsTask().GetAwaiter().GetResult();
        _ = _store.CommitAsync(request).AsTask().GetAwaiter().GetResult();
        _view = _store.Pin();

        Graph graph = new();
        new NTriplesParser().Load(graph, new StreamReader(new MemoryStream(Bsbm.Data), Encoding.UTF8));
        TripleStore triples = new();
        triples.Add(graph);
        _processor = new LeviathanQueryProcessor(new VDS.RDF.Query.Datasets.InMemoryDataset(triples, true));
        _dotNetRdfQuery = new SparqlQueryParser().ParseFromString(text);

        int store = VarveStore(), dataset = VarveDataset(), dotNetRdf = DotNetRdf();
        Console.WriteLine($"// {Query}: {store} rows (store), {dataset} (dataset), {dotNetRdf} (dotNetRDF)");
        if (store != dataset || store != dotNetRdf)
        {
            throw new InvalidOperationException(Query + ": the engines disagree on the row count.");
        }
    }

    [GlobalCleanup]
    public void Release()
    {
        _view.Dispose();
        _store.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _processor?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark(Baseline = true)]
    public int DotNetRdf() => ((SparqlResultSet)_processor.ProcessQuery(_dotNetRdfQuery)).Count;

    [Benchmark]
    public int VarveStore() => Count(_view);

    [Benchmark]
    public int VarveDataset() => Count(_dataset);

    private int Count(IQuadSource source)
    {
        using QueryResults results = _evaluator.Evaluate(_varveQuery, source);
        SolutionResults solutions = (SolutionResults)results;
        int count = 0;
        while (solutions.MoveNext())
        {
            count++;
        }

        return count;
    }
}
