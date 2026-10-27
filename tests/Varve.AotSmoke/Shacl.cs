// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Shacl;
using Varve.Shacl.Model;
using Varve.Shacl.Sparql;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;
using Varve.Turtle;

namespace Varve.AotSmoke;

/// <summary>
/// The SHACL smoke both hosts run, the AOT binary and the browser app (#120):
/// people committed to an in-memory store, validated over the pinned view
/// against shapes with an XPath pattern, a recursive <c>sh:node</c>, a
/// datatype, a class and a SHACL-SPARQL constraint; and two queries whose
/// <c>REGEX</c> and <c>REPLACE</c> use XPath syntax .NET does not have. Each
/// answer is one line, and the report's canonical form is hashed, so the two
/// hosts and the desktop must agree byte for byte.
/// </summary>
/// <remarks>
/// What ILC or the trimmer can drop without a build error: the shapes
/// reader's dictionaries keyed by term, the regular expression translator's
/// code point sets and Unicode tables, the compiled <c>Regex</c>, the
/// fixpoint's tables, the SPARQL extension reached only through an interface,
/// and RDFC-1.0 over the report.
/// </remarks>
internal static class ShaclSmoke
{
    internal const string ExpectedShacl =
        "shacl: conforms false; 6 results: Datatype carol, Node alice, Node bob, Node carol, Pattern bob, SPARQL carol; "
        + "canonical sha256 b5b5b72833b07d5b3bed7b036636e717667d29e59a8e2f7c78423c1ec84606f5";

    internal const string ExpectedRegex = "regex: alice carol; replace Al*c* b*b Ç*r*l D*v*";

    private const string Data =
        "@prefix ex: <http://example.org/> .\n"
        + "@prefix xsd: <http://www.w3.org/2001/XMLSchema#> .\n"
        + "ex:alice a ex:Person ; ex:name \"Alice\" ; ex:knows ex:bob .\n"
        + "ex:bob a ex:Person ; ex:name \"bob\" ; ex:knows ex:alice .\n"
        + "ex:carol a ex:Person ; ex:name \"Çarol\" ; ex:age \"forty\"^^xsd:integer ; ex:knows ex:dave .\n"
        + "ex:dave ex:name \"Dave\" .\n";

    // The pattern is XPath's, not .NET's: \p{Lu} then letters other than the
    // digraph, by character class subtraction, which .NET spells differently.
    private const string Shapes =
        "@prefix sh: <http://www.w3.org/ns/shacl#> .\n"
        + "@prefix ex: <http://example.org/> .\n"
        + "@prefix xsd: <http://www.w3.org/2001/XMLSchema#> .\n"
        + "ex:PersonShape a sh:NodeShape ;\n"
        + "  sh:targetClass ex:Person ;\n"
        + "  sh:class ex:Person ;\n"
        + "  sh:property [ sh:path ex:name ; sh:minCount 1 ; sh:pattern \"^\\\\p{Lu}[\\\\p{L}-[\\\\p{Lu}]]+$\" ] ;\n"
        + "  sh:property [ sh:path ex:age ; sh:datatype xsd:integer ] ;\n"
        + "  sh:property [ sh:path ex:knows ; sh:node ex:PersonShape ] ;\n"
        + "  sh:sparql [ sh:message \"knows is mutual\" ; sh:select \"\"\"\n"
        + "    SELECT $this ?value WHERE { $this <http://example.org/knows> ?value FILTER NOT EXISTS { ?value <http://example.org/knows> $this } }\"\"\" ] .\n";

    internal static async Task<(string Shacl, string Regex)> RunAsync()
    {
        Varve.Store.Log.CommitRequestBuilder request = new();
        TurtleOptions read = new() { Syntax = RdfSyntax.Turtle };
        ParseResult parsed = TurtleParser.Parse(
            Encoding.UTF8.GetBytes(Data),
            (in QuadView quad) => request.Assert(quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise()),
            in read);
        if (!parsed.Succeeded)
        {
            throw new InvalidOperationException("shacl: the data did not parse: " + parsed.FirstError.ToString());
        }

        await using Varve.Store.Dataset dataset = await Varve.Store.Dataset.CreateAsync(
            new Varve.Store.MemoryStorage(), new Varve.Store.Log.DatasetId(Guid.NewGuid()), new Varve.Store.DatasetOptions { Clock = TimeProvider.System });
        _ = await dataset.CommitAsync(request);
        using Varve.Store.DatasetView view = dataset.Pin();

        InMemoryDatasetBuilder shapes = new();
        ParseResult shapesParsed = TurtleParser.Parse(
            Encoding.UTF8.GetBytes(Shapes),
            (in QuadView quad) => shapes.Add(quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise()),
            in read);
        if (!shapesParsed.Succeeded)
        {
            throw new InvalidOperationException("shacl: the shapes did not parse: " + shapesParsed.FirstError.ToString());
        }

        ShaclOptions options = new() { Extensions = [SparqlConstraints.Extension()] };
        ShapesLoad load = ShapesGraph.Load(shapes.ToDataset(), GraphPattern.DefaultGraph, options);
        if (load.Shapes is null)
        {
            throw new InvalidOperationException("shacl: the shapes were refused: " + string.Join("; ", load.Problems));
        }

        ValidationOutcome outcome = ShaclValidator.Validate(load.Shapes, view, GraphPattern.DefaultGraph, options);
        if (outcome.Report is not { } report)
        {
            throw new InvalidOperationException("shacl: the validation failed: " + string.Join("; ", outcome.Problems));
        }

        List<string> results = [];
        foreach (ShaclResult result in report.Results)
        {
            string component = Lexical(result.SourceConstraintComponent)["http://www.w3.org/ns/shacl#".Length..].Replace("ConstraintComponent", string.Empty, StringComparison.Ordinal);
            results.Add(component + " " + Local(result.FocusNode));
        }

        results.Sort(StringComparer.Ordinal);
        string shacl = "shacl: conforms " + (report.Conforms ? "true" : "false") + "; " + results.Count + " results: " + string.Join(", ", results)
            + "; canonical sha256 " + Convert.ToHexStringLower(SHA256.HashData(report.ToCanonicalNQuads().Span));

        string matched = string.Join(" ", Rows(view, "SELECT ?s { ?s ex:name ?n FILTER(REGEX(?n, \"^[\\\\p{Lu}-[B-Z]]|^\\\\p{Lu}[\\\\p{Ll}-[aeiou]]a\")) } ORDER BY ?s", r => Local(r[0])));
        string replaced = string.Join(" ", Rows(view, "SELECT (REPLACE(?n, \"[\\\\p{Ll}-[lrbcv]]\", \"*\") AS ?r) { ?s ex:name ?n } ORDER BY ?s", r => Lexical(r[0])));
        return (shacl, "regex: " + matched + "; replace " + replaced);
    }

    private static List<string> Rows(IQuadSource source, string text, Func<RdfTerm?[], string> render)
    {
        Varve.Sparql.Algebra.Query query = Varve.Sparql.SparqlParser.ParseQuery(("PREFIX ex: <http://example.org/>\n" + text).AsSpan());
        using QueryResults results = new SparqlEvaluator().Evaluate(query, source);
        SolutionResults solutions = (SolutionResults)results;
        List<string> rows = [];
        while (solutions.MoveNext())
        {
            RdfTerm?[] row = new RdfTerm?[solutions.Variables.Count];
            for (int i = 0; i < row.Length; i++)
            {
                row[i] = solutions.TryGetTerm(new ColumnIndex(i), out RdfTerm? term) ? term : null;
            }

            rows.Add(render(row));
        }

        return rows;
    }

    private static string Lexical(RdfTerm? term) => term is null ? "(unbound)" : Encoding.UTF8.GetString(term.Lexical);

    private static string Local(RdfTerm? term) => Lexical(term)["http://example.org/".Length..];
}
