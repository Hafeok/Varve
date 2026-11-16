// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Varve.Rdf;

namespace Varve.Conformance.Tests;

/// <summary>
/// <c>rdf11/rdf-mt</c>, the RDF 1.1 Semantics cases (ADR 0193): its 48 active
/// positive and negative entailment cases, each under its regime
/// (<c>mf:entailmentRegime</c>: <c>simple</c>, <c>RDF</c> or <c>RDFS</c>)
/// with the datatypes it recognises. A case whose result is <c>false</c> asks
/// whether the action is inconsistent, which the rewriting does not check (§4
/// lets it answer regardless): those wait for the closure's clash rules, #171.
/// </summary>
internal static class RdfMtSuite
{
    internal const string Id = "rdf11/rdf-mt";
    private const string Mf = "http://www.w3.org/2001/sw/DataAccess/tests/test-manifest#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    private static readonly Lazy<ImmutableArray<RdfMtEntry>> AllEntries = new(() => [.. Read()]);
    private static readonly Lazy<FrozenDictionary<string, RdfMtEntry>> Index = new(() => AllEntries.Value.ToFrozenDictionary(e => e.TestIri, StringComparer.Ordinal));

    internal static ImmutableArray<RdfMtEntry> Entries => AllEntries.Value;

    internal static FrozenDictionary<string, RdfMtEntry> ByIri => Index.Value;

    private static List<RdfMtEntry> Read()
    {
        string manifestPath = "rdf/rdf11/rdf-mt/manifest.ttl";
        ManifestGraph graph = ManifestGraph.Load(TestData.ResolveFromRoot(manifestPath), EvaluationSuite.PublishedRoot + manifestPath);
        List<RdfMtEntry> result = [];
        RdfTerm manifest = graph.Subjects(Rdf + "type", Mf + "Manifest").Single();
        foreach (RdfTerm list in graph.Objects(manifest, Mf + "entries"))
        {
            foreach (RdfTerm entry in graph.Collection(list))
            {
                string type = graph.Object(entry, Rdf + "type") is { } t ? ManifestGraph.Text(t) : "";
                bool positive = type.EndsWith("#PositiveEntailmentTest", StringComparison.Ordinal);
                if (!positive && !type.EndsWith("#NegativeEntailmentTest", StringComparison.Ordinal))
                {
                    continue;
                }

                RdfTerm resultTerm = graph.Object(entry, Mf + "result")!;
                result.Add(new RdfMtEntry(
                    ManifestGraph.Text(entry),
                    graph.Object(entry, Mf + "name") is { } name ? ManifestGraph.Text(name) : ManifestGraph.Text(entry),
                    positive,
                    ManifestGraph.Text(graph.Object(entry, Mf + "entailmentRegime")!),
                    [.. Members(graph, entry, Mf + "recognizedDatatypes")],
                    ManifestGraph.Text(graph.Object(entry, Mf + "action")!),
                    resultTerm.Kind == RdfTermKind.Literal ? null : ManifestGraph.Text(resultTerm)));
            }
        }

        return result;
    }

    private static IEnumerable<RdfTerm> Members(ManifestGraph graph, RdfTerm entry, string predicate)
    {
        foreach (RdfTerm list in graph.Objects(entry, predicate))
        {
            foreach (RdfTerm member in graph.Collection(list))
            {
                yield return member;
            }
        }
    }

    /// <summary>Why a case does not run on the rewriting, or null when it runs.</summary>
    internal static string? NotRun(RdfMtEntry entry) =>
        entry.Result is null ? "pending: whether the action is inconsistent, the closure's clash rules, #171" : null;
}

/// <summary>One case of <c>rdf-mt</c>: does the action entail the result, under the regime, recognising the datatypes.</summary>
internal sealed record RdfMtEntry(string TestIri, string Name, bool Positive, string Regime, IReadOnlyList<RdfTerm> Recognised, string Action, string? Result);
