// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Varve.Rdf;
using Varve.Shacl.Model;
using Xunit;

namespace Varve.Shacl.Tests;

/// <summary>ADR 0154: the report as a value, its order, and the data's blank nodes kept.</summary>
public class ReportTests
{
    [Fact]
    public void Data_blank_nodes_keep_their_labels_so_the_report_joins_back_to_the_data()
    {
        InMemoryDataset shapes = Support.Dataset("ex:S sh:targetClass ex:C ; sh:property [ sh:path ex:age ; sh:minCount 1 ] .");
        InMemoryDataset data = Support.Dataset("""
            _:good a ex:C ; ex:age 3 .
            _:bad1 a ex:C ; ex:name "one" .
            _:bad2 a ex:C ; ex:name "two" .
            """);

        CancellationToken ct = TestContext.Current.CancellationToken;
        ValidationReport report = ShaclValidator.Validate(ShapesGraph.Load(shapes, GraphPattern.DefaultGraph, null, ct).Shapes!, data, GraphPattern.DefaultGraph, null, ct).Report!;
        Assert.Equal(2, report.Results.Count);

        // Join the report's focus nodes back to the data by label: each names the node with that name.
        HashSet<string> names = [];
        InMemoryDataset graph = report.ToDataset();
        foreach (ShaclResult result in report.Results)
        {
            Assert.Equal(RdfTermKind.BlankNode, result.FocusNode.Kind);
            Assert.True(data.TryInternalise(result.FocusNode, out TermHandle focus), "the report's label is not the data's");
            Assert.True(data.TryInternalise(Support.Ex("name"), out TermHandle name));
            using IQuadCursor cursor = data.Match(focus, name, TermHandle.None, GraphPattern.DefaultGraph);
            Assert.True(cursor.MoveNext());
            data.TryExternalise(cursor.Current.Object, out RdfTerm? value);
            names.Add(Encoding.UTF8.GetString(value!.Lexical));

            // The same blank node, under the same label, is the focus node in the report graph.
            Assert.True(graph.TryInternalise(result.FocusNode, out _));
        }

        Assert.Equal(["one", "two"], names);
    }

    [Fact]
    public void Equal_results_are_kept_as_two()
    {
        // lessThan-002's shape: a result for each pair (SHACL 1.2 Core §7.6.4).
        ValidationReport report = Support.Validate(
            "ex:S sh:targetNode ex:x ; sh:property [ sh:path ex:first ; sh:lessThan ex:second ] .",
            "ex:x ex:first 5 ; ex:second 1, 2 .");

        Assert.Equal(2, report.Results.Count);
        Assert.Equal(2, report.Summary.Violations);
        string nquads = Encoding.UTF8.GetString(report.ToCanonicalNQuads().Span);
        Assert.Equal(2, CountOf(nquads, "<http://www.w3.org/ns/shacl#result>"));
    }

    [Fact]
    public void The_summary_counts_by_severity_and_conformance_follows_the_disallowed_set()
    {
        const string Shapes = """
            ex:S sh:targetNode ex:x ; sh:property [ sh:path ex:p ; sh:minCount 2 ; sh:severity sh:Warning ] .
            ex:T sh:targetNode ex:x ; sh:property [ sh:path ex:p ; sh:maxCount 0 ; sh:severity sh:Info ] .
            """;
        ValidationReport report = Support.Validate(Shapes, "ex:x ex:p 1 .");
        Assert.False(report.Conforms);
        Assert.Equal((0, 1, 1), (report.Summary.Violations, report.Summary.Warnings, report.Summary.Infos));

        ValidationReport relaxed = Support.Validate(Shapes, "ex:x ex:p 1 .", new ShaclOptions { ConformanceDisallows = [RdfTerm.Iri("http://www.w3.org/ns/shacl#Violation"u8)] });
        Assert.True(relaxed.Conforms);
        Assert.Contains("<http://www.w3.org/ns/shacl#conformanceDisallows>", Encoding.UTF8.GetString(relaxed.ToCanonicalNQuads().Span), StringComparison.Ordinal);
    }

    [Fact]
    public void Results_are_in_the_stated_order()
    {
        ValidationReport report = Support.Validate(
            "ex:S sh:targetNode ex:c, ex:a, ex:b ; sh:class ex:C .",
            "ex:z ex:p 1 .");

        List<string> foci = [];
        foreach (ShaclResult result in report.Results)
        {
            foci.Add(Support.Text(result.FocusNode));
        }

        Assert.Equal(["http://example.com/ns#a", "http://example.com/ns#b", "http://example.com/ns#c"], foci);
    }

    private static int CountOf(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
