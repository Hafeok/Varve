// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Varve.Rdf;
using Varve.Sparql.Entailment;
using Varve.Sparql.Entailment.Model;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;

namespace Varve.WasmSmoke;

/// <summary>
/// Tier 1 of Inference in the browser (#170, ADR 0193): a query rewritten
/// under RDFS against a TBox graph merged into the data graph, and one under
/// D, which matches a literal by its value through the two functions the
/// rewriting calls.
/// </summary>
internal static partial class Smoke
{
    internal const string ExpectedEntailment = "rdfs rex d rex";

    private static readonly RdfTerm Tbox = RdfTerm.Iri("http://example.org/tbox"u8);
    private static readonly RdfTerm XsdBoolean = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#boolean"u8);

    internal static string Entail()
    {
        InMemoryDatasetBuilder builder = new();
        builder.Add(Ex("Dog"), RdfTerm.Iri("http://www.w3.org/2000/01/rdf-schema#subClassOf"u8), Ex("Animal"), Tbox);
        builder.Add(Ex("rex"), RdfTerm.Iri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8), Ex("Dog"));
        builder.Add(Ex("rex"), Ex("age"), RdfTerm.Literal("030"u8, RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#integer"u8)));
        InMemoryDataset dataset = builder.ToDataset();
        TBoxCover[] covers = [new([Tbox], GraphScope.All)];

        string rdfs = string.Join(" ", Entailed(dataset, covers, EntailmentRegime.Rdfs, "SELECT ?x { ?x a ex:Animal }"));
        string d = string.Join(" ", Entailed(dataset, covers, EntailmentRegime.D, "SELECT ?x { ?x ex:age 30 }"));
        return "rdfs " + rdfs + " d " + d;
    }

    private static List<string> Entailed(InMemoryDataset dataset, TBoxCover[] covers, EntailmentRegime regime, string text)
    {
        Varve.Sparql.Algebra.Query query = Varve.Sparql.SparqlParser.ParseQuery((Prefix + text).AsSpan());
        EntailedQuery entailed = EntailmentRewriter.Rewrite(query, dataset, covers, regime);
        EvaluationOptions options = new()
        {
            Functions = new Dictionary<string, IExtensionFunction>(StringComparer.Ordinal)
            {
                [Encoding.UTF8.GetString(LiteralValues.FunctionIri.Lexical)] = new CanonicalFunction(),
                [Encoding.UTF8.GetString(LiteralValues.HasValueTypeIri.Lexical)] = new HasValueTypeFunction(),
            },
        };
        using QueryResults results = new SparqlEvaluator(options).Evaluate(entailed.Query, entailed.Source);
        SolutionResults solutions = (SolutionResults)results;
        List<string> rows = [];
        while (solutions.MoveNext())
        {
            rows.Add(solutions.TryGetTerm(new ColumnIndex(0), out RdfTerm? term) ? Local(term) : "(unbound)");
        }

        return rows;
    }

    private static RdfTerm Ex(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.org/" + local));

    private sealed class CanonicalFunction : IExtensionFunction
    {
        public bool TryEvaluate(ReadOnlySpan<RdfTerm> arguments, [NotNullWhen(true)] out RdfTerm? result)
        {
            result = arguments.Length == 1 ? LiteralValues.Canonical(arguments[0]) : null;
            return result is not null;
        }
    }

    private sealed class HasValueTypeFunction : IExtensionFunction
    {
        public bool TryEvaluate(ReadOnlySpan<RdfTerm> arguments, [NotNullWhen(true)] out RdfTerm? result)
        {
            result = arguments.Length == 2 ? RdfTerm.Literal(LiteralValues.HasValueType(arguments[0], arguments[1]) ? "true"u8 : "false"u8, XsdBoolean) : null;
            return result is not null;
        }
    }
}
