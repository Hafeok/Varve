// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Sparql.Algebra;
using Varve.Sparql.Entailment;
using Varve.Sparql.Entailment.Model;
using Varve.Sparql.Evaluation;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// <c>sparql11/entailment</c> under the tier-1 regimes (ADR 0193): each case
/// that lists RDF, RDFS or D, rewritten under each of those it lists and
/// evaluated over <see cref="InMemoryDataset"/>, one ratchet line per case
/// per regime, <c>&lt;test IRI&gt;@rdf</c>, <c>@rdfs</c> and <c>@d</c>. The
/// ontology is in the case's own data, so no TBox graph is merged.
/// </summary>
public class EntailmentConformanceTests
{
    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (EntailmentEntry entry in EntailmentSuite.Entries)
        {
            foreach ((string name, string iri, EntailmentRegime _) in EntailmentSuite.Tier1)
            {
                if (entry.Regimes.Contains(iri))
                {
                    string id = entry.Evaluation.TestIri + "@" + name;
                    yield return new TheoryDataRow<string>(id) { TestDisplayName = id };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Case(string testIri)
    {
        int at = testIri.LastIndexOf('@');
        EntailmentEntry entry = EntailmentSuite.ByIri[testIri[..at]];
        EntailmentRegime regime = EntailmentSuite.Tier1.Single(t => t.Name == testIri[(at + 1)..]).Regime;
        string? failure = await RunAsync(entry.Evaluation, regime);
        Assert.True(failure is null, entry.Evaluation.Name + " (" + regime + "): " + failure);
    }

    internal static async Task<string?> RunAsync(EvaluationEntry entry, EntailmentRegime regime)
    {
        Query query = EvaluationRunner.ParseQuery(entry);
        List<(IReadOnlyList<DataQuad>, RdfTerm?)> graphs = [];
        foreach (string iri in entry.Data)
        {
            graphs.Add((EvaluationData.Quads(iri), null));
        }

        foreach (string iri in entry.GraphData)
        {
            graphs.Add((EvaluationData.Quads(iri), RdfTerm.Iri(Encoding.UTF8.GetBytes(iri))));
        }

        await using LoadedSource loaded = await EvaluationSubjects.Dataset.LoadAsync(graphs);
        EntailedQuery entailed = EntailmentRewriter.Rewrite(query, loaded.Source, [], regime, cancellationToken: TestContext.Current.CancellationToken);
        using QueryResults results = new SparqlEvaluator(EntailmentFunctions.Options).Evaluate(entailed.Query, entailed.Source, TestContext.Current.CancellationToken);
        return EvaluationRunner.Compare(entry, query, results, null);
    }
}

/// <summary>The host's adapters for D's two functions (ADR 0193 point 7).</summary>
internal static class EntailmentFunctions
{
    internal static EvaluationOptions Options { get; } = new()
    {
        Clock = FixedClock.Instance,
        Randomness = new SeededRandom(),
        Functions = new Dictionary<string, IExtensionFunction>(StringComparer.Ordinal)
        {
            [Encoding.UTF8.GetString(LiteralValues.FunctionIri.Lexical)] = new Canonical(),
            [Encoding.UTF8.GetString(LiteralValues.HasValueTypeIri.Lexical)] = new HasValueType(),
        },
    };

    private sealed class Canonical : IExtensionFunction
    {
        public bool TryEvaluate(ReadOnlySpan<RdfTerm> arguments, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RdfTerm? result)
        {
            result = arguments.Length == 1 ? LiteralValues.Canonical(arguments[0]) : null;
            return result is not null;
        }
    }

    private sealed class HasValueType : IExtensionFunction
    {
        private static readonly RdfTerm Boolean = RdfTerm.Iri("http://www.w3.org/2001/XMLSchema#boolean"u8);

        public bool TryEvaluate(ReadOnlySpan<RdfTerm> arguments, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RdfTerm? result)
        {
            result = arguments.Length == 2 ? RdfTerm.Literal(LiteralValues.HasValueType(arguments[0], arguments[1]) ? "true"u8 : "false"u8, Boolean) : null;
            return result is not null;
        }
    }
}
