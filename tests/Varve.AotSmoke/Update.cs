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
using Varve.Sparql.Store;

namespace Varve.AotSmoke;

/// <summary>
/// Milestone 5c's smoke, which both hosts run: a SPARQL Update request of two
/// operations executed through <c>Varve.Sparql.Store</c> as one commit to an
/// in-memory store, a query over the result written as SPARQL results JSON,
/// and a small dataset canonicalised with RDFC-1.0 under SHA-256 and SHA-384.
/// </summary>
/// <remarks>
/// The update runs through the integration package since ADR 0060 made the
/// smoke apps layer 6 hosts; until then they composed it by hand.
/// </remarks>
internal static partial class Smoke
{
    internal const string ExpectedUpdate = "Committed at 1";

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

    private const string Request =
        "PREFIX ex: <http://example.org/>\n"
        + "INSERT DATA { ex:a ex:n 1 . ex:b ex:n \"B\u00e9\\n\"@fr } ;\n"
        + "DELETE { ?s ex:n ?o } INSERT { ?s ex:n 2 } WHERE { ?s ex:n ?o FILTER(isNumeric(?o)) }";

    internal static async Task<(string Update, string Json, string Canonical, string Sha384)> UpdateAsync()
    {
        await using Varve.Store.Dataset dataset = await Varve.Store.Dataset.OpenAsync(
            new Varve.Store.MemoryStorage(), new Varve.Store.DatasetOptions { Clock = TimeProvider.System });

        // Two operations, one commit: the second sees the first's insert.
        Varve.Store.CommitResult result = await SparqlUpdate.ExecuteAsync(
            dataset, Varve.Sparql.SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(Request)), new UpdateOptions());
        string update = result.Outcome + " at " + dataset.Head.ToString(System.Globalization.CultureInfo.InvariantCulture);

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

        InMemoryDatasetBuilder builder = new();
        RdfTerm p = RdfTerm.Iri("http://example.org/p"u8);
        builder.Add(RdfTerm.BlankNode("x"u8), RdfTerm.Iri("http://example.org/q"u8), RdfTerm.Literal("end"u8));
        builder.Add(RdfTerm.BlankNode("y"u8), p, RdfTerm.BlankNode("x"u8));
        builder.Add(RdfTerm.Iri("http://example.org/s"u8), p, RdfTerm.BlankNode("y"u8));
        InMemoryDataset small = builder.ToDataset();
        CanonicalDataset sha256 = RdfCanonicaliser.Canonicalise(small);
        CanonicalDataset sha384 = RdfCanonicaliser.Canonicalise(
            small, new CanonicalisationOptions { HashAlgorithm = System.Security.Cryptography.HashAlgorithmName.SHA384 });

        return (update, Encoding.UTF8.GetString(json.WrittenSpan), Encoding.UTF8.GetString(sha256.NQuads.Span), Encoding.UTF8.GetString(sha384.NQuads.Span));
    }
}
