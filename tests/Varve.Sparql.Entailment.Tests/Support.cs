// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Entailment.Model;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;

namespace Varve.Sparql.Entailment.Tests;

internal static class Support
{
    internal const string Ex = "http://example.org/";
    internal const string RdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    internal const string RdfsNs = "http://www.w3.org/2000/01/rdf-schema#";

    internal static RdfTerm Named(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes(Ex + local));

    internal static RdfTerm IriOf(string iri) => RdfTerm.Iri(Encoding.UTF8.GetBytes(iri));

    internal static RdfTerm Tbox { get; } = Named("tbox");

    internal static Query Parse(string text)
    {
        SparqlParseOptions options = new(Encoding.UTF8.GetBytes(Ex), SparqlVersion.Sparql11);
        string prologue = "PREFIX : <" + Ex + ">\nPREFIX rdf: <" + RdfNs + ">\nPREFIX rdfs: <" + RdfsNs + ">\nPREFIX xsd: <http://www.w3.org/2001/XMLSchema#>\n";
        return SparqlParser.TryParseQuery(Encoding.UTF8.GetBytes(prologue + text), options, out Query? query, out SparqlParseError error)
            ? query
            : throw new InvalidOperationException("Does not parse: " + error + "\n" + text);
    }

    /// <summary>A query's solutions under <paramref name="regime"/>, as sorted lines.</summary>
    internal static List<string> Run(string query, IQuadSource source, EntailmentRegime regime, params RdfTerm[] tbox) =>
        Run(query, source, regime, tbox.Length == 0 ? [] : [new TBoxCover(tbox, GraphScope.All)]);

    /// <summary>A query's solutions under <paramref name="regime"/>, each graph merged with the TBox of the binding that covers it.</summary>
    internal static List<string> Run(string query, IQuadSource source, EntailmentRegime regime, IReadOnlyList<TBoxCover> covers, SchemaCache? schemas = null) =>
        Run(EntailmentRewriter.Rewrite(Parse(query), source, covers, regime, schemas: schemas));

    /// <summary>A rewritten query's solutions, as sorted lines.</summary>
    internal static List<string> Run(EntailedQuery entailed)
    {
        EvaluationOptions options = new()
        {
            Functions = new Dictionary<string, IExtensionFunction>
            {
                [Encoding.UTF8.GetString(LiteralValues.FunctionIri.Lexical)] = new Canonical(),
                [Encoding.UTF8.GetString(LiteralValues.HasValueTypeIri.Lexical)] = new HasValueType(),
            },
        };
        using QueryResults results = new SparqlEvaluator(options).Evaluate(entailed.Query, entailed.Source);
        return results switch
        {
            SolutionResults solutions => Rows(solutions),
            BooleanResult boolean => [boolean.Value ? "true" : "false"],
            _ => throw new InvalidOperationException("Not a SELECT or an ASK."),
        };
    }

    internal static List<string> Rows(SolutionResults solutions)
    {
        List<string> rows = [];
        while (solutions.MoveNext())
        {
            List<string> cells = [];
            for (int i = 0; i < solutions.Variables.Count; i++)
            {
                if (solutions.TryGetTerm(new ColumnIndex(i), out RdfTerm? term))
                {
                    cells.Add("?" + solutions.Variables[i].Name + "=" + Text(term!));
                }
            }

            cells.Sort(StringComparer.Ordinal);
            rows.Add(string.Join(" ", cells));
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    /// <summary>The host's adapter for D's canonical literal (ADR 0193 point 7).</summary>
    private sealed class Canonical : IExtensionFunction
    {
        public bool TryEvaluate(ReadOnlySpan<RdfTerm> arguments, [NotNullWhen(true)] out RdfTerm? result)
        {
            result = arguments.Length == 1 ? LiteralValues.Canonical(arguments[0]) : null;
            return result is not null;
        }
    }

    private sealed class HasValueType : IExtensionFunction
    {
        public bool TryEvaluate(ReadOnlySpan<RdfTerm> arguments, [NotNullWhen(true)] out RdfTerm? result)
        {
            result = arguments.Length == 2
                ? RdfTerm.Literal(LiteralValues.HasValueType(arguments[0], arguments[1]) ? "true"u8 : "false"u8, IriOf("http://www.w3.org/2001/XMLSchema#boolean"))
                : null;
            return result is not null;
        }
    }

    internal static string Text(RdfTerm term) => term.Kind switch
    {
        RdfTermKind.Iri => "<" + Encoding.UTF8.GetString(term.Lexical) + ">",
        RdfTermKind.BlankNode => "_:" + Encoding.UTF8.GetString(term.Lexical),
        _ => "\"" + Encoding.UTF8.GetString(term.Lexical) + "\""
            + (term.Language.IsEmpty ? "" : "@" + Encoding.UTF8.GetString(term.Language))
            + (term.Datatype is null ? "" : "^^<" + Encoding.UTF8.GetString(term.DatatypeIri) + ">"),
    };
}
