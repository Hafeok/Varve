// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Varve.Conformance.Tests;
using Varve.Rdf;
using Varve.Sparql.Evaluation;

namespace Varve.Benchmarks;

/// <summary>
/// The differential run against Oxigraph (ADR 0038): every evaluation case
/// that is not blocked and does not call a SERVICE, answered by pyoxigraph
/// (<c>oxigraph/differential.py</c>) and compared with Varve's answer by the
/// conformance comparator. Varve passes every one of these cases, so a
/// disagreement is Oxigraph departing from the suite's expected result, or a
/// comparison the suite leaves open; each is reported.
/// </summary>
internal static class Differential
{
    /// <summary>Writes the cases as JSON for the Python side: query, base, and the files of each graph.</summary>
    internal static void Export(string path)
    {
        List<Dictionary<string, object?>> cases = [];
        foreach (EvaluationEntry entry in Cases())
        {
            List<string[]> graphs = [];
            graphs.AddRange(entry.Data.Select(iri => new[] { "", EvaluationSuite.PathOf(iri) }));
            HashSet<string> named = new(StringComparer.Ordinal);
            foreach (string iri in entry.GraphData)
            {
                if (named.Add(iri))
                {
                    graphs.Add([iri, EvaluationSuite.PathOf(iri)]);
                }
            }

            // As the harness does: a FROM or FROM NAMED that names a suite file loads it as that named graph.
            if (EvaluationRunner.ParseQuery(entry).Dataset is { } dataset)
            {
                foreach (RdfTerm graph in dataset.DefaultGraphs.ToArray().Concat(dataset.NamedGraphs.ToArray()))
                {
                    string iri = Encoding.UTF8.GetString(graph.Lexical);
                    if (iri.StartsWith(EvaluationSuite.PublishedRoot, StringComparison.Ordinal) && EvaluationCatalogue.Exists(iri) && named.Add(iri))
                    {
                        graphs.Add([iri, EvaluationSuite.PathOf(iri)]);
                    }
                }
            }

            cases.Add(new()
            {
                ["id"] = entry.TestIri,
                ["query"] = EvaluationSuite.PathOf(entry.QueryIri),
                ["base"] = entry.QueryIri,
                ["graphs"] = graphs,
            });
        }

        File.WriteAllText(path, JsonSerializer.Serialize(cases));
        Console.WriteLine(cases.Count + " cases written to " + path);
    }

    /// <summary>Compares Varve's answer to each case with Oxigraph's, from the directory the Python side wrote.</summary>
    internal static void Compare(string directory)
    {
        int agreed = 0;
        List<string> disagreements = [];
        List<string> refused = [];
        foreach ((EvaluationEntry entry, int index) in Cases().Select((e, i) => (e, i)))
        {
            string stem = Path.Combine(directory, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (File.Exists(stem + ".error"))
            {
                refused.Add(entry.TestIri + "\n    Oxigraph: " + File.ReadAllText(stem + ".error").Trim().Split('\n')[0]);
                continue;
            }

            string answer = File.Exists(stem + ".srj") ? stem + ".srj" : stem + ".nt";
            string? failure = EvaluationRunner.RunAsync(entry, EvaluationSubjects.Dataset, ValueAccess.InlineAccessor, answer, CancellationToken.None).GetAwaiter().GetResult();
            if (failure is null)
            {
                agreed++;
            }
            else
            {
                disagreements.Add(entry.TestIri + "\n    " + failure.Replace("\n", "\n    ", StringComparison.Ordinal));
            }
        }

        Console.WriteLine($"{agreed} agree, {disagreements.Count} disagree, {refused.Count} refused by Oxigraph");
        Console.WriteLine("\n## Disagreements (Varve's answer is the suite's expected one)\n");
        disagreements.ForEach(Console.WriteLine);
        Console.WriteLine("\n## Refused by Oxigraph\n");
        refused.ForEach(Console.WriteLine);
    }

    private static IEnumerable<EvaluationEntry> Cases() =>
        EvaluationCatalogue.Entries.Where(e => !EvaluationCatalogue.IsBlocked(e) && e.ServiceData.Count == 0 && !e.Suite.EndsWith("/service", StringComparison.Ordinal));
}
