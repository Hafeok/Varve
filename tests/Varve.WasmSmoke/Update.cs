// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Results;

namespace Varve.WasmSmoke;

/// <summary>
/// Milestone 5c's smoke, which both hosts run: an update against an in-memory
/// store — the data inserted, then a DELETE/INSERT whose WHERE is evaluated
/// over the pinned head and whose change is committed expecting that head — a
/// query over the result written as SPARQL results JSON, and a small dataset
/// canonicalised with RDFC-1.0 under SHA-256 and SHA-384.
/// </summary>
/// <remarks>
/// The update is composed here rather than through
/// <c>Varve.Sparql.Store.SparqlUpdate</c>: this app is a layer 5 host and that
/// package is layer 5, and ADR 0003 has no recorded exception for a host
/// referencing an integration. The composition is the same one the package
/// makes — pin, evaluate, commit at the pinned position — over the same
/// store, evaluator and staging code.
/// </remarks>
internal static partial class Smoke
{
    internal const string ExpectedUpdate = "Committed at 1; Committed at 2";

    internal const string ExpectedJson =
        "{\"head\":{\"vars\":[\"s\",\"n\"]},\"results\":{\"bindings\":["
        + "{\"s\":{\"type\":\"uri\",\"value\":\"http://example.org/a\"},\"n\":{\"type\":\"literal\",\"value\":\"2\",\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\"}},"
        + "{\"s\":{\"type\":\"uri\",\"value\":\"http://example.org/b\"},\"n\":{\"type\":\"literal\",\"value\":\"Bé\\n\",\"xml:lang\":\"fr\"}}"
        + "]}}\n";

    internal const string ExpectedCanonical =
        "<http://example.org/s> <http://example.org/p> _:c14n0 .\n"
        + "_:c14n0 <http://example.org/p> _:c14n1 .\n"
        + "_:c14n1 <http://example.org/q> \"end\" .\n";

    internal const string ExpectedCanonical384 = ExpectedCanonical;

    private static readonly RdfTerm N = RdfTerm.Iri("http://example.org/n"u8);

    internal static async Task<(string Update, string Json, string Canonical, string Sha384)> UpdateAsync()
    {
        await using Varve.Store.Dataset dataset = await Varve.Store.Dataset.OpenAsync(
            new Varve.Store.MemoryStorage(), new Varve.Store.DatasetOptions { Clock = TimeProvider.System });

        // INSERT DATA { ex:a ex:n 1 . ex:b ex:n "Bé\n"@fr }
        Varve.Store.CommitResult inserted = await dataset.CommitAsync(new Varve.Store.CommitRequest()
            .Assert(RdfTerm.Iri("http://example.org/a"u8), N, RdfTerm.Literal("1"u8, RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8)))
            .Assert(RdfTerm.Iri("http://example.org/b"u8), N, RdfTerm.Literal("B\u00e9\n"u8, "fr"u8)));
        string update = inserted.Outcome + " at " + dataset.Head.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // DELETE { ?s ex:n ?o } INSERT { ?s ex:n 2 } WHERE { ?s ex:n ?o FILTER(isNumeric(?o)) }
        Varve.Store.CommitRequest rewrite;
        using (Varve.Store.DatasetView where = dataset.Pin())
        {
            rewrite = new Varve.Store.CommitRequest { ExpectedPosition = where.Position };
            Varve.Sparql.Algebra.Query select = Varve.Sparql.SparqlParser.ParseQuery(
                "PREFIX ex: <http://example.org/>\nSELECT ?s ?o { ?s ex:n ?o FILTER(isNumeric(?o)) }".AsSpan());

            using QueryResults matches = new SparqlEvaluator().Evaluate(select, where);
            SolutionResults rows = (SolutionResults)matches;

            while (rows.MoveNext())
            {
                if (rows.TryGetTerm(0, out RdfTerm? s) && rows.TryGetTerm(1, out RdfTerm? o))
                {
                    rewrite.Retract(s!, N, o!).Assert(s!, N, RdfTerm.Literal("2"u8, RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8)));
                }
            }
        }

        Varve.Store.CommitResult rewritten = await dataset.CommitAsync(rewrite);
        update += "; " + rewritten.Outcome + " at " + dataset.Head.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using Varve.Store.DatasetView view = dataset.Pin();
        Varve.Sparql.Algebra.Query query = Varve.Sparql.SparqlParser.ParseQuery(
            "PREFIX ex: <http://example.org/>\nSELECT ?s ?n { ?s ex:n ?n } ORDER BY ?s".AsSpan());
        ArrayBufferWriter<byte> json = new();

        using (QueryResults results = new SparqlEvaluator().Evaluate(query, view))
        using (SparqlResultsWriter writer = new(json, SparqlResultsFormat.Json))
        {
            SolutionResults solutions = (SolutionResults)results;
            writer.WriteHead([.. System.Linq.Enumerable.Select(solutions.Variables, v => v.Name)]);

            while (solutions.MoveNext())
            {
                writer.StartSolution();

                for (int i = 0; i < solutions.Variables.Count; i++)
                {
                    if (solutions.TryGetTerm(i, out RdfTerm? term))
                    {
                        writer.WriteBinding(i, term!);
                    }
                }

                writer.EndSolution();
            }

            writer.WriteEnd();
        }

        InMemoryDataset small = new();
        RdfTerm p = RdfTerm.Iri("http://example.org/p"u8);
        small.Add(RdfTerm.BlankNode("x"u8), RdfTerm.Iri("http://example.org/q"u8), RdfTerm.Literal("end"u8));
        small.Add(RdfTerm.BlankNode("y"u8), p, RdfTerm.BlankNode("x"u8));
        small.Add(RdfTerm.Iri("http://example.org/s"u8), p, RdfTerm.BlankNode("y"u8));
        CanonicalDataset sha256 = RdfCanonicaliser.Canonicalise(small);
        CanonicalDataset sha384 = RdfCanonicaliser.Canonicalise(
            small, new CanonicalisationOptions { HashAlgorithm = System.Security.Cryptography.HashAlgorithmName.SHA384 });

        return (update, Encoding.UTF8.GetString(json.WrittenSpan), Encoding.UTF8.GetString(sha256.NQuads.Span), Encoding.UTF8.GetString(sha384.NQuads.Span));
    }
}
