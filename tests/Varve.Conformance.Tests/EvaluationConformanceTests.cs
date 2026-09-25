// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Sparql;
using Varve.Sparql.Algebra;
using Varve.Sparql.Evaluation;
using Xunit;

namespace Varve.Conformance.Tests;

/// <summary>
/// The W3C query evaluation suites (<c>sparql-evaluation.md</c> §12.1): every
/// case that is not blocked, over <see cref="InMemoryDataset"/> and over the
/// store's default projection, one ratchet line per case per subject —
/// <c>&lt;test IRI&gt;@dataset</c> and <c>&lt;test IRI&gt;@store</c>.
/// </summary>
public class EvaluationConformanceTests
{
    internal static readonly string[] SubjectNames = ["dataset", "store"];

    public static IEnumerable<TheoryDataRow<string>> Cases()
    {
        if (!TestData.IsCheckedOut)
        {
            yield break;
        }

        foreach (EvaluationEntry entry in EvaluationCatalogue.Entries)
        {
            if (EvaluationCatalogue.IsBlocked(entry))
            {
                continue;
            }

            foreach (string subject in SubjectNames)
            {
                string id = entry.TestIri + "@" + subject;
                yield return new TheoryDataRow<string>(id) { TestDisplayName = id };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Case(string testIri)
    {
        int at = testIri.LastIndexOf('@');
        EvaluationEntry entry = EvaluationCatalogue.ByIri[testIri[..at]];
        IEvaluationSubject subject = EvaluationSubjects.ByName(testIri[(at + 1)..]);
        string? failure = await RunAsync(entry, subject, TestContext.Current.CancellationToken);
        Assert.True(failure is null, entry.Suite + " " + entry.Name + " (" + subject.Name + "): " + failure);
    }

    /// <summary>Runs one case over one subject; null when it passes, otherwise why not.</summary>
    internal static async Task<string?> RunAsync(EvaluationEntry entry, IEvaluationSubject subject, CancellationToken cancellationToken)
    {
        Query query = ParseQuery(entry);
        List<(IReadOnlyList<DataQuad>, RdfTerm?)> graphs = [];
        foreach (string iri in entry.Data)
        {
            graphs.Add((EvaluationData.Quads(iri), null));
        }

        HashSet<string> named = new(StringComparer.Ordinal);
        foreach (string iri in entry.GraphData)
        {
            if (named.Add(iri))
            {
                graphs.Add((EvaluationData.Quads(iri), RdfTerm.Iri(Encoding.UTF8.GetBytes(iri))));
            }
        }

        // A FROM or FROM NAMED that names a suite file loads it as that named graph.
        if (query.Dataset is { } dataset)
        {
            foreach (RdfTerm graph in dataset.DefaultGraphs.ToArray().Concat(dataset.NamedGraphs.ToArray()))
            {
                string iri = Encoding.UTF8.GetString(graph.Lexical);
                if (iri.StartsWith(EvaluationSuite.PublishedRoot, StringComparison.Ordinal) && EvaluationCatalogue.Exists(iri) && named.Add(iri))
                {
                    graphs.Add((EvaluationData.Quads(iri), graph));
                }
            }
        }

        EvaluationOptions options = new()
        {
            Clock = FixedClock.Instance,
            Randomness = new SeededRandom(),
            ServiceHandler = new TestServiceHandler(entry.ServiceData),
        };

        await using LoadedSource loaded = await subject.LoadAsync(graphs);
        using QueryResults results = new SparqlEvaluator(options).Evaluate(query, loaded.Source, cancellationToken);
        return Compare(entry, query, results);
    }

    internal static Query ParseQuery(EvaluationEntry entry)
    {
        byte[] text = File.ReadAllBytes(EvaluationSuite.PathOf(entry.QueryIri));
        SparqlParseOptions options = new(Encoding.UTF8.GetBytes(entry.QueryIri), entry.Version);
        return SparqlParser.TryParseQuery(text, options, out Query? query, out SparqlParseError error)
            ? query
            : throw new InvalidOperationException("The query does not parse: " + error);
    }

    private static string? Compare(EvaluationEntry entry, Query query, QueryResults results)
    {
        if (entry.ResultIri is null)
        {
            return "the manifest names no result";
        }

        string resultPath = EvaluationSuite.PathOf(entry.ResultIri);
        bool graphFile = Path.GetExtension(resultPath) is ".ttl" or ".nt" or ".rdf";
        IReadOnlyList<DataQuad>? expectedGraph = graphFile ? EvaluationData.Quads(entry.ResultIri) : null;
        ResultTable? expected = ResultTable.Read(resultPath, expectedGraph);

        switch (results)
        {
            case SolutionResults solutions:
                {
                    if (expected is null)
                    {
                        return "a SELECT, but the expected result is a graph";
                    }

                    ResultTable actual = new();
                    actual.Variables.AddRange(solutions.Variables.Select(v => v.Name));
                    while (solutions.MoveNext())
                    {
                        RdfTerm?[] row = new RdfTerm?[actual.Variables.Count];
                        for (int i = 0; i < row.Length; i++)
                        {
                            row[i] = solutions.TryGetTerm(i, out RdfTerm? term) ? term : null;
                        }

                        actual.Rows.Add(row);
                    }

                    (bool ordered, List<string> keys) = OrderOf(query.Pattern);
                    return expected.Compare(actual, ordered, keys, entry.LaxCardinality);
                }

            case BooleanResult boolean:
                return expected is null
                    ? "an ASK, but the expected result is a graph"
                    : expected.Compare(new ResultTable { Boolean = boolean.Value }, false, [], false);
            case TripleResults triples:
                {
                    if (expectedGraph is null)
                    {
                        return "a graph, but the expected result is a table";
                    }

                    List<ParsedQuad> actual = [];
                    while (triples.MoveNext())
                    {
                        actual.Add(new ParsedQuad(EvaluationData.Text(triples.Subject), EvaluationData.Text(triples.Predicate), EvaluationData.Text(triples.Object), null));
                    }

                    List<ParsedQuad> wanted = [.. expectedGraph.Select(q => new ParsedQuad(EvaluationData.Text(q.Subject), EvaluationData.Text(q.Predicate), EvaluationData.Text(q.Object), null)).Distinct()];
                    IsomorphismResult verdict = Isomorphism.Compare(actual, wanted);
                    return verdict.Verdict == IsomorphismVerdict.Same
                        ? null
                        : verdict.Reason + "\n  actual:\n    " + string.Join("\n    ", actual) + "\n  expected:\n    " + string.Join("\n    ", wanted);
                }

            default:
                return "an unknown result kind";
        }
    }

    /// <summary>Whether the query orders its solutions, and the ORDER BY keys that are plain variables.</summary>
    private static (bool Ordered, List<string> Keys) OrderOf(QueryPattern pattern)
    {
        while (true)
        {
            switch (pattern)
            {
                case Slice slice:
                    pattern = slice.Inner;
                    continue;
                case Distinct distinct:
                    pattern = distinct.Inner;
                    continue;
                case Reduced reduced:
                    pattern = reduced.Inner;
                    continue;
                case Project project:
                    pattern = project.Inner;
                    continue;
                case OrderBy orderBy:
                    return (true, [.. orderBy.Conditions.ToArray()
                        .Select(c => c.Expression is VariableExpression v ? v.Variable.Name : null)
                        .TakeWhile(n => n is not null)
                        .Select(n => n!)]);
                default:
                    return (false, []);
            }
        }
    }
}

/// <summary>
/// SERVICE for the suite: each endpoint of the manifest's qt:serviceData is an
/// in-memory dataset of its data, and the pattern is evaluated there by this
/// same evaluator (ADR 0055). Any other endpoint fails.
/// </summary>
internal sealed class TestServiceHandler(IReadOnlyList<(string Endpoint, IReadOnlyList<string> Data)> endpoints) : IServiceHandler
{
    public ServiceResult Execute(ServiceRequest request, CancellationToken cancellationToken)
    {
        string endpoint = Encoding.UTF8.GetString(request.Endpoint.Lexical);
        foreach ((string iri, IReadOnlyList<string> data) in endpoints)
        {
            if (!string.Equals(iri, endpoint, StringComparison.Ordinal))
            {
                continue;
            }

            InMemoryDataset dataset = new();
            for (int f = 0; f < data.Count; f++)
            {
                foreach (DataQuad quad in EvaluationData.Quads(data[f]))
                {
                    dataset.Add(EvaluationSubjects.Scope(quad.Subject, f), quad.Predicate, EvaluationSubjects.Scope(quad.Object, f), quad.Graph);
                }
            }

            SelectQuery query = new(Prologue.Empty, null, new Project(request.Pattern.Inner, AlgebraList.From(request.Variables)));
            EvaluationOptions options = new() { Clock = FixedClock.Instance, ServiceHandler = this };
            using SolutionResults results = (SolutionResults)new SparqlEvaluator(options).Evaluate(query, dataset, cancellationToken);
            List<IReadOnlyList<RdfTerm?>> rows = [];
            while (results.MoveNext())
            {
                RdfTerm?[] row = new RdfTerm?[request.Variables.Count];
                for (int i = 0; i < row.Length; i++)
                {
                    row[i] = results.TryGetTerm(i, out RdfTerm? term) ? term : null;
                }

                rows.Add(row);
            }

            return ServiceResult.FromSolutions(request.Variables, rows);
        }

        return ServiceResult.Failed("The test suite defines no endpoint " + endpoint + ".");
    }
}
