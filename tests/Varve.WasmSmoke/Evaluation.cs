// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Sparql.Evaluation;
using Varve.Turtle;

namespace Varve.WasmSmoke;

/// <summary>
/// The evaluation smoke both hosts run: a Turtle document committed to an
/// in-memory store, and four queries over its pinned view — a basic graph
/// pattern with a filter, an aggregate, a property path closure, and the five
/// hash functions — each answer rendered as one line.
/// </summary>
internal static partial class Smoke
{
    internal const string ExpectedBgp = "a-b b-c";
    internal const string ExpectedAggregate = "count 3 sum 120 avg 40";
    internal const string ExpectedPath = "b c d";

    /// <summary>The five hashes of "abc": RFC 1321 §A.5 for MD5, FIPS 180's examples for the others.</summary>
    internal const string ExpectedHashes =
        "md5 900150983cd24fb0d6963f7d28e17f72"
        + " sha1 a9993e364706816aba3e25717850c26c9cd0d89d"
        + " sha256 ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
        + " sha384 cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7"
        + " sha512 ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f";

    private const string Data =
        "@prefix ex: <http://example.org/> .\n"
        + "ex:a ex:knows ex:b . ex:b ex:knows ex:c . ex:c ex:knows ex:d .\n"
        + "ex:a ex:age 30 . ex:b ex:age 40 . ex:c ex:age 50 .\n"
        + "ex:a ex:name \"abc\" .\n";

    private const string Prefix = "PREFIX ex: <http://example.org/>\n";

    internal static async Task<(string Bgp, string Aggregate, string Path, string Hashes)> EvaluateAsync()
    {
        Varve.Store.CommitRequest request = new();
        TurtleOptions read = new() { Syntax = RdfSyntax.Turtle };
        ParseResult parsed = TurtleParser.Parse(
            Encoding.UTF8.GetBytes(Data),
            (in QuadView quad) => request.Assert(quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise()),
            in read);
        if (!parsed.Succeeded)
        {
            throw new InvalidOperationException("evaluation: the Turtle did not parse: " + parsed.FirstError.ToString());
        }

        await using Varve.Store.Dataset dataset = await Varve.Store.Dataset.OpenAsync(
            new Varve.Store.MemoryStorage(), new Varve.Store.DatasetOptions { Clock = TimeProvider.System });
        _ = await dataset.CommitAsync(request);
        using Varve.Store.DatasetView view = dataset.Pin();

        string bgp = string.Join(" ", Rows(view, "SELECT ?x ?y { ?x ex:knows ?y . ?y ex:age ?a FILTER(?a > 35) } ORDER BY ?x", r => Local(r[0]) + "-" + Local(r[1])));
        string aggregate = string.Join(" ", Rows(view, "SELECT (COUNT(*) AS ?n) (SUM(?a) AS ?s) (AVG(?a) AS ?m) { ?x ex:age ?a }", r => "count " + Lexical(r[0]) + " sum " + Lexical(r[1]) + " avg " + Lexical(r[2])));
        string path = string.Join(" ", Rows(view, "SELECT ?y { ex:a ex:knows+ ?y } ORDER BY ?y", r => Local(r[0])));
        string hashes = string.Join(" ", Rows(
            view,
            "SELECT (MD5(?n) AS ?md5) (SHA1(?n) AS ?sha1) (SHA256(?n) AS ?sha256) (SHA384(?n) AS ?sha384) (SHA512(?n) AS ?sha512) { ex:a ex:name ?n }",
            r => "md5 " + Lexical(r[0]) + " sha1 " + Lexical(r[1]) + " sha256 " + Lexical(r[2]) + " sha384 " + Lexical(r[3]) + " sha512 " + Lexical(r[4])));
        return (bgp, aggregate, path, hashes);
    }

    private static List<string> Rows(IQuadSource source, string text, Func<RdfTerm?[], string> render)
    {
        Varve.Sparql.Algebra.Query query = Varve.Sparql.SparqlParser.ParseQuery((Prefix + text).AsSpan());
        using QueryResults results = new SparqlEvaluator().Evaluate(query, source);
        SolutionResults solutions = (SolutionResults)results;
        List<string> rows = [];
        while (solutions.MoveNext())
        {
            RdfTerm?[] row = new RdfTerm?[solutions.Variables.Count];
            for (int i = 0; i < row.Length; i++)
            {
                row[i] = solutions.TryGetTerm(i, out RdfTerm? term) ? term : null;
            }

            rows.Add(render(row));
        }

        return rows;
    }

    private static string Lexical(RdfTerm? term) => term is null ? "(unbound)" : Encoding.UTF8.GetString(term.Lexical);

    private static string Local(RdfTerm? term) => Lexical(term)["http://example.org/".Length..];
}
