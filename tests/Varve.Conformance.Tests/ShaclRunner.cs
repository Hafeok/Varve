// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Varve.Rdf;
using Varve.Shacl;
using Varve.Shacl.Model;
using Varve.Store;

namespace Varve.Conformance.Tests;

/// <summary>
/// Runs one SHACL case over one subject and compares the report with the
/// suite's expected one by its "full compliance" rule (ADR 0149): the
/// actual report normalised to the suite's predicates, then isomorphism.
/// </summary>
internal static class ShaclRunner
{
    private const string Sh = "http://www.w3.org/ns/shacl#";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>The predicates the suite compares (its index.html, "Validate RDF data").</summary>
    private static readonly HashSet<string> Kept = new(StringComparer.Ordinal)
    {
        RdfType, Sh + "result", Sh + "conforms", Sh + "focusNode", Sh + "resultPath", Sh + "resultSeverity",
        Sh + "sourceConstraint", Sh + "sourceConstraintComponent", Sh + "sourceShape", Sh + "value", Sh + "resultMessage",
    };

    internal static readonly string[] SubjectNames = ["dataset", "store"];

    /// <summary>The extensions the harness validates with; the SPARQL suites need SHACL-SPARQL's.</summary>
    internal static Func<IReadOnlyList<IShaclExtension>> Extensions { get; set; } = () => [];

    /// <summary>Null when the case passes, otherwise why not.</summary>
    internal static async Task<string?> RunAsync(ShaclEntry entry, string subject, CancellationToken cancellationToken)
    {
        IReadOnlyList<DataQuad> dataQuads = ShaclSuite.Read(entry.DataGraph);
        bool same = string.Equals(entry.DataGraph, entry.ShapesGraph, StringComparison.Ordinal);
        IReadOnlyList<DataQuad> shapesQuads = same ? dataQuads : ShaclSuite.Read(entry.ShapesGraph);

        List<Func<ValueTask>> releases = [];
        try
        {
            IQuadSource data = await LoadAsync(subject, dataQuads, releases);
            IQuadSource shapes = same ? data : await LoadAsync(subject, shapesQuads, releases);
            // The suite's conformance-disallows-001: the framework uses the expected report's set.
            List<RdfTerm>? disallows = entry.ExpectsFailure ? null : ExpectedDisallows(entry);
            ShaclOptions options = new() { Extensions = Extensions(), ConformanceDisallows = disallows };

            ShapesLoad load = ShapesGraph.Load(shapes, GraphPattern.DefaultGraph, options, cancellationToken);
            ValidationOutcome? outcome = load.Shapes is { } loaded
                ? ShaclValidator.Validate(loaded, data, GraphPattern.DefaultGraph, options, cancellationToken)
                : null;
            IReadOnlyList<ShaclProblem> problems = outcome?.Problems ?? load.Problems;

            if (entry.ExpectsFailure)
            {
                return outcome?.Report is null ? null : "expected a failure, and the validation reported " + Describe(outcome.Report);
            }

            if (outcome?.Report is not { } report)
            {
                return "expected a report, and the validation failed: " + string.Join("; ", problems.Select(p => p.ToString()));
            }

            List<DataQuad> expected = Expected(entry);
            List<DataQuad> actual = Normalise(report.ToDataset(), expected);
            string? difference = DatasetComparison.Compare(actual, expected);
            return difference is null ? null : difference + "\n  report: " + Describe(report);
        }
        finally
        {
            foreach (Func<ValueTask> release in releases)
            {
                await release();
            }
        }
    }

    private static async ValueTask<IQuadSource> LoadAsync(string subject, IReadOnlyList<DataQuad> quads, List<Func<ValueTask>> releases)
    {
        if (subject == "dataset")
        {
            InMemoryDatasetBuilder builder = new();
            foreach (DataQuad quad in quads)
            {
                builder.Add(quad.Subject, quad.Predicate, quad.Object);
            }

            return builder.ToDataset();
        }

        Dataset dataset = await EvaluationSubjects.LoadStoreAsync([(quads, null)]);
        DatasetView view = dataset.Pin();
        releases.Add(async () =>
        {
            view.Dispose();
            await dataset.DisposeAsync();
        });
        return view;
    }

    /// <summary>The expected report's <c>sh:conformanceDisallows</c>, or null when it states none.</summary>
    private static List<RdfTerm>? ExpectedDisallows(ShaclEntry entry)
    {
        List<RdfTerm> found = [];
        foreach (DataQuad quad in ShaclSuite.Read(entry.File))
        {
            if (quad.Subject.Equals(entry.ExpectedReport!) && Iri(quad.Predicate) == Sh + "conformanceDisallows")
            {
                found.Add(quad.Object);
            }
        }

        return found.Count == 0 ? null : found;
    }

    /// <summary>The expected graph: the report's triples, its results', and their paths' structures.</summary>
    private static List<DataQuad> Expected(ShaclEntry entry)
    {
        IReadOnlyList<DataQuad> file = ShaclSuite.Read(entry.File);
        RdfTerm report = entry.ExpectedReport!;
        List<DataQuad> expected = [];
        HashSet<RdfTerm> results = new(RdfTerm.Comparer);
        foreach (DataQuad quad in file)
        {
            if (quad.Subject.Equals(report))
            {
                expected.Add(quad);
                if (Iri(quad.Predicate) == Sh + "result")
                {
                    results.Add(quad.Object);
                }
            }
        }

        foreach (DataQuad quad in file)
        {
            if (results.Contains(quad.Subject) && Kept.Contains(Iri(quad.Predicate)))
            {
                expected.Add(quad);
                if (Iri(quad.Predicate) == Sh + "resultPath")
                {
                    Structure(file, quad.Object, expected, new HashSet<RdfTerm>(RdfTerm.Comparer));
                }
            }
        }

        return expected;
    }

    /// <summary>The suite's normalisation of the actual report: only the compared predicates, messages only where the expected graph has them.</summary>
    private static List<DataQuad> Normalise(InMemoryDataset report, List<DataQuad> expected)
    {
        List<DataQuad> all = Quads(report);
        HashSet<RdfTerm> expectedMessages = new(RdfTerm.Comparer);
        foreach (DataQuad quad in expected)
        {
            if (Iri(quad.Predicate) == Sh + "resultMessage")
            {
                expectedMessages.Add(quad.Object);
            }
        }

        RdfTerm? reportNode = all.FirstOrDefault(q => Iri(q.Predicate) == RdfType && Iri(q.Object) == Sh + "ValidationReport")?.Subject;
        List<DataQuad> normalised = [];
        HashSet<RdfTerm> results = new(RdfTerm.Comparer);
        foreach (DataQuad quad in all)
        {
            if (reportNode is not null && quad.Subject.Equals(reportNode) && Iri(quad.Predicate) is RdfType or Sh + "conforms" or Sh + "result" or Sh + "conformanceDisallows")
            {
                normalised.Add(quad);
                if (Iri(quad.Predicate) == Sh + "result")
                {
                    results.Add(quad.Object);
                }
            }
        }

        foreach (DataQuad quad in all)
        {
            string predicate = Iri(quad.Predicate);
            if (!results.Contains(quad.Subject) || !Kept.Contains(predicate))
            {
                continue;
            }

            if (predicate == Sh + "resultMessage" && !expectedMessages.Contains(quad.Object))
            {
                continue;
            }

            normalised.Add(quad);
            if (predicate == Sh + "resultPath")
            {
                Structure(all, quad.Object, normalised, new HashSet<RdfTerm>(RdfTerm.Comparer));
            }
        }

        return normalised;
    }

    /// <summary>The blank node structure under a path node.</summary>
    private static void Structure(IReadOnlyList<DataQuad> graph, RdfTerm node, List<DataQuad> into, HashSet<RdfTerm> visited)
    {
        if (node.Kind != RdfTermKind.BlankNode || !visited.Add(node))
        {
            return;
        }

        foreach (DataQuad quad in graph)
        {
            if (quad.Subject.Equals(node))
            {
                into.Add(quad);
                Structure(graph, quad.Object, into, visited);
            }
        }
    }

    private static List<DataQuad> Quads(InMemoryDataset source)
    {
        List<DataQuad> quads = [];
        using IQuadCursor cursor = source.Match(TermHandle.None, TermHandle.None, TermHandle.None, GraphPattern.Any);
        while (cursor.MoveNext())
        {
            Quad quad = cursor.Current;
            source.TryExternalise(quad.Subject, out RdfTerm? s);
            source.TryExternalise(quad.Predicate, out RdfTerm? p);
            source.TryExternalise(quad.Object, out RdfTerm? o);
            quads.Add(new DataQuad(s!, p!, o!, null));
        }

        return quads;
    }

    private static string Iri(RdfTerm term) => term.Kind == RdfTermKind.Iri ? Encoding.UTF8.GetString(term.Lexical) : string.Empty;

    private static string Describe(ValidationReport report) =>
        "conforms " + report.Conforms + ", " + report.Results.Count + " result(s): "
        + string.Join("; ", report.Results.Select(r =>
            EvaluationData.Text(r.FocusNode) + " " + Iri(r.SourceConstraintComponent).Replace(Sh, "sh:", StringComparison.Ordinal)
            + (r.Value is null ? string.Empty : " value " + EvaluationData.Text(r.Value))));
}
