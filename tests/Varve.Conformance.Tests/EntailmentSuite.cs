// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Entailment.Model;

namespace Varve.Conformance.Tests;

/// <summary>
/// <c>sparql11/entailment</c> (<c>sparql-evaluation.md</c> §12.1, ADR 0193):
/// its 70 query evaluation cases, each tagged with the regimes it holds under
/// (<c>sd:entailmentRegime</c>, one IRI or a list) and the profiles
/// (<c>sd:EntailmentProfile</c>, the class name the manifest uses as a
/// property). A case runs under each of RDF, RDFS and D it lists. A case
/// with none of them waits for #171 when it holds under OWL 2 RDF-Based in
/// the RL profile, for #95's last part when it holds under OWL 2 Direct in
/// the QL profile, and is otherwise out of scope — RIF, and OWL 2 DL or Full
/// beyond both profiles, which #15 excludes: blocked by name, in
/// <c>baseline/blocked.txt</c>.
/// </summary>
internal static class EntailmentSuite
{
    internal const string Id = "sparql11/entailment";
    private const string Mf = "http://www.w3.org/2001/sw/DataAccess/tests/test-manifest#";
    private const string Qt = "http://www.w3.org/2001/sw/DataAccess/tests/test-query#";
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Sd = "http://www.w3.org/ns/sparql-service-description#";
    private const string Ent = "http://www.w3.org/ns/entailment/";
    private const string Profile = "http://www.w3.org/ns/owl-profile/";

    private static readonly Lazy<ImmutableArray<EntailmentEntry>> AllEntries = new(() => [.. Read()]);
    private static readonly Lazy<FrozenDictionary<string, EntailmentEntry>> Index = new(() => AllEntries.Value.ToFrozenDictionary(e => e.Evaluation.TestIri, StringComparer.Ordinal));

    internal static ImmutableArray<EntailmentEntry> Entries => AllEntries.Value;

    internal static FrozenDictionary<string, EntailmentEntry> ByIri => Index.Value;

    /// <summary>The tier-1 regimes, as a case's line names them.</summary>
    internal static readonly (string Name, string Iri, EntailmentRegime Regime)[] Tier1 =
    [
        ("rdf", Ent + "RDF", EntailmentRegime.Rdf),
        ("rdfs", Ent + "RDFS", EntailmentRegime.Rdfs),
        ("d", Ent + "D", EntailmentRegime.D),
    ];

    private static List<EntailmentEntry> Read()
    {
        string manifestPath = "sparql/" + Id + "/manifest.ttl";
        ManifestGraph graph = ManifestGraph.Load(TestData.ResolveFromRoot(manifestPath), EvaluationSuite.PublishedRoot + manifestPath);
        List<EntailmentEntry> result = [];
        RdfTerm manifest = graph.Subjects(Rdf + "type", Mf + "Manifest").Single();
        foreach (RdfTerm list in graph.Objects(manifest, Mf + "entries"))
        {
            foreach (RdfTerm entry in graph.Collection(list))
            {
                if (graph.Object(entry, Rdf + "type") is not { } type
                    || !string.Equals(ManifestGraph.Text(type), Mf + "QueryEvaluationTest", StringComparison.Ordinal)
                    || graph.Object(entry, Mf + "action") is not { } action)
                {
                    continue;
                }

                string testIri = ManifestGraph.Text(entry);
                EvaluationEntry evaluation = new(
                    testIri,
                    Id,
                    graph.Object(entry, Mf + "name") is { } name ? ManifestGraph.Text(name) : testIri,
                    SparqlVersion.Sparql11,
                    ManifestGraph.Text(graph.Object(action, Qt + "query")!),
                    [.. graph.Objects(action, Qt + "data").Select(ManifestGraph.Text)],
                    [.. graph.Objects(action, Qt + "graphData").Select(ManifestGraph.Text)],
                    [],
                    graph.Object(entry, Mf + "result") is { } expected ? ManifestGraph.Text(expected) : null,
                    false);
                result.Add(new EntailmentEntry(evaluation, Iris(graph, action, Sd + "entailmentRegime"), Iris(graph, action, Sd + "EntailmentProfile")));
            }
        }

        return result;
    }

    // One IRI, or the members of a list.
    private static FrozenSet<string> Iris(ManifestGraph graph, RdfTerm action, string predicate)
    {
        HashSet<string> iris = new(StringComparer.Ordinal);
        foreach (RdfTerm value in graph.Objects(action, predicate))
        {
            if (value.Kind == RdfTermKind.Iri && !ManifestGraph.Text(value).EndsWith("#nil", StringComparison.Ordinal))
            {
                iris.Add(ManifestGraph.Text(value));
                continue;
            }

            foreach (RdfTerm member in graph.Collection(value))
            {
                iris.Add(ManifestGraph.Text(member));
            }
        }

        return iris.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>Why a case does not run in this part, or null when it runs.</summary>
    internal static string? NotRun(EntailmentEntry entry)
    {
        if (entry.Regimes.Overlaps(Tier1.Select(t => t.Iri)))
        {
            return null;
        }

        if (entry.Regimes.Contains(Ent + "OWL-RDF-Based") && entry.Profiles.Contains(Profile + "RL"))
        {
            return "pending: OWL 2 RL by the closure, #171";
        }

        if (entry.Regimes.Contains(Ent + "OWL-Direct") && entry.Profiles.Contains(Profile + "QL"))
        {
            return "pending: OWL 2 QL by rewriting, #95's last part";
        }

        return entry.Regimes.SetEquals([Ent + "RIF"])
            ? "blocked: RIF entailment is out of scope (#95: RDFS, OWL 2 RL and QL, and SPARQL and SHACL rules)"
            : "blocked: OWL 2 DL and Full reasoning is out of scope (#15: no OWL 2 DL or EL reasoning)";
    }
}

/// <summary>One case of <c>sparql11/entailment</c>, with its regimes and profiles.</summary>
internal sealed record EntailmentEntry(EvaluationEntry Evaluation, FrozenSet<string> Regimes, FrozenSet<string> Profiles);
