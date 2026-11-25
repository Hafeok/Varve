// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Evaluation;
using Varve.Sparql.Evaluation.Model;
using Varve.Store;

namespace Varve.Learn;

/// <summary>
/// What a sample prints: named sections, and a query's answer as a table,
/// IRIs written with the prefixes the track uses. The learn gate compares
/// each section with the sample's <c>expected/</c> file of the same name.
/// </summary>
internal static class Show
{
    private static readonly ImmutableArray<(string Prefix, string Namespace)> Prefixes =
    [
        ("rec:", "https://w3id.org/rec#"),
        ("brick:", "https://brickschema.org/schema/Brick#"),
        ("rdf:", "http://www.w3.org/1999/02/22-rdf-syntax-ns#"),
        ("rdfs:", "http://www.w3.org/2000/01/rdf-schema#"),
        ("xsd:", "http://www.w3.org/2001/XMLSchema#"),
        (":", "https://example.org/learn/"),
    ];

    /// <summary>Starts the section named <paramref name="name"/>.</summary>
    internal static void Section(string name)
    {
        Console.WriteLine();
        Console.WriteLine("== " + name + " ==");
    }

    /// <summary>The text of the sample's query or update file <paramref name="name"/>, under <c>queries/</c>.</summary>
    internal static string File(string name) => System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "queries", name));

    /// <summary>Evaluates the query in the sample's file <paramref name="name"/> over <paramref name="source"/> and prints its solutions as a table.</summary>
    internal static void Query(IQuadSource source, string name)
    {
        SparqlEvaluator evaluator = new(new EvaluationOptions { Clock = TimeProvider.System });
        using QueryResults results = evaluator.Evaluate(SparqlParser.ParseQuery(File(name)), source, CancellationToken.None);
        SolutionResults solutions = (SolutionResults)results;
        List<string> header = [.. solutions.Variables.Select(v => v.Name)];
        List<string[]> rows = [];
        while (solutions.MoveNext())
        {
            string[] row = new string[header.Count];
            for (int i = 0; i < header.Count; i++)
            {
                row[i] = solutions.TryGetTerm(new ColumnIndex(i), out RdfTerm? term) && term is not null ? Text(term) : "";
            }

            rows.Add(row);
        }

        Table(header, rows);
    }

    /// <summary>Prints a table: a header, a rule, then the rows, each column as wide as its widest cell.</summary>
    internal static void Table(IReadOnlyList<string> header, IReadOnlyList<string[]> rows)
    {
        int[] widths = [.. header.Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length)))];
        Console.WriteLine(Line(header, widths));
        Console.WriteLine(string.Join("-+-", widths.Select(w => new string('-', w))));
        foreach (string[] row in rows)
        {
            Console.WriteLine(Line(row, widths));
        }

        Console.WriteLine("(" + rows.Count.ToString(CultureInfo.InvariantCulture) + (rows.Count == 1 ? " row)" : " rows)"));
    }

    /// <summary>A term as the track writes it: an IRI by its prefixed name, a literal by its value, its datatype unless it is a string, an integer or a boolean.</summary>
    internal static string Text(RdfTerm term)
    {
        string lexical = Encoding.UTF8.GetString(term.Lexical);
        return term.Kind switch
        {
            RdfTermKind.Iri => Name(lexical),
            RdfTermKind.Literal when term.Datatype is { } datatype
                && Encoding.UTF8.GetString(datatype.Lexical) is not ("http://www.w3.org/2001/XMLSchema#string" or "http://www.w3.org/2001/XMLSchema#integer" or "http://www.w3.org/2001/XMLSchema#boolean")
                => lexical + "^^" + Name(Encoding.UTF8.GetString(datatype.Lexical)),
            RdfTermKind.Literal => lexical,
            _ => "_:" + lexical,
        };
    }

    /// <summary>
    /// When the sample is run with <c>--state &lt;file&gt;</c>, writes its
    /// dataset's head there as canonical N-Quads (RDFC-1.0), which the learn
    /// gate compares with <c>expected/state.nq</c> and the REC tests validate.
    /// </summary>
    internal static void StateIfAsked(string[] args, Dataset dataset)
    {
        int at = Array.IndexOf(args, "--state");
        if (at < 0 || at + 1 >= args.Length)
        {
            return;
        }

        using DatasetView head = dataset.Pin();
        CanonicalDataset canonical = RdfCanonicaliser.Canonicalise(head);
        System.IO.File.WriteAllBytes(args[at + 1], canonical.NQuads.ToArray());
    }

    private static string Name(string iri)
    {
        foreach ((string prefix, string ns) in Prefixes)
        {
            if (iri.StartsWith(ns, StringComparison.Ordinal))
            {
                return prefix + iri[ns.Length..];
            }
        }

        return "<" + iri + ">";
    }

    private static string Line(IReadOnlyList<string> cells, int[] widths) =>
        string.Join(" | ", cells.Select((c, i) => c.PadRight(widths[i]))).TrimEnd();
}
