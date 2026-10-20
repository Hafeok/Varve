// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using Varve.Rdf;
using Varve.Shacl.Model;
using Varve.Turtle;
using Xunit;

namespace Varve.Shacl.Tests;

/// <summary>Turtle in, datasets and reports out.</summary>
internal static class Support
{
    internal const string Prefixes = """
        @prefix sh: <http://www.w3.org/ns/shacl#> .
        @prefix ex: <http://example.com/ns#> .
        @prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> .
        @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .
        @prefix xsd: <http://www.w3.org/2001/XMLSchema#> .
        @prefix owl: <http://www.w3.org/2002/07/owl#> .

        """;

    internal static InMemoryDataset Dataset(string turtle)
    {
        InMemoryDatasetBuilder builder = new();
        ParseResult result = TurtleParser.Parse(
            Encoding.UTF8.GetBytes(Prefixes + turtle),
            (in QuadView quad) => builder.Add(quad.Subject.Materialise(), quad.Predicate.Materialise(), quad.Object.Materialise()),
            new TurtleOptions { Syntax = RdfSyntax.Turtle });
        Assert.True(result.Succeeded, result.FirstError.ToString());
        return builder.ToDataset();
    }

    /// <summary>Validates <paramref name="data"/> against <paramref name="shapes"/>, both Turtle, and returns the report.</summary>
    internal static ValidationReport Validate(string shapes, string data, ShaclOptions? options = null)
    {
        ValidationOutcome outcome = Outcome(shapes, data, options);
        Assert.True(outcome.Succeeded, string.Join("; ", outcome.Problems));
        return outcome.Report!;
    }

    internal static ValidationOutcome Outcome(string shapes, string data, ShaclOptions? options = null)
    {
        ShapesLoad load = ShapesGraph.Load(Dataset(shapes), GraphPattern.DefaultGraph, options);
        return load.Shapes is { } loaded
            ? ShaclValidator.Validate(loaded, Dataset(data), GraphPattern.DefaultGraph, options)
            : new ValidationOutcome(null, load.Problems);
    }

    internal static RdfTerm Ex(string local) => RdfTerm.Iri(Encoding.UTF8.GetBytes("http://example.com/ns#" + local));

    internal static string Text(RdfTerm term) => Encoding.UTF8.GetString(term.Lexical);

    /// <summary>The focus nodes of every result, as local names, sorted and distinct.</summary>
    internal static List<string> Foci(ValidationReport report)
    {
        SortedSet<string> found = new(StringComparer.Ordinal);
        foreach (ShaclResult result in report.Results)
        {
            found.Add(Text(result.FocusNode).Replace("http://example.com/ns#", string.Empty, StringComparison.Ordinal));
        }

        return [.. found];
    }

    /// <summary>The focus nodes of results with the given source shape, as local names, sorted.</summary>
    internal static List<string> Failing(ValidationReport report, string shape)
    {
        List<string> found = [];
        foreach (ShaclResult result in report.Results)
        {
            if (result.SourceShape.Equals(Ex(shape)))
            {
                found.Add(Text(result.FocusNode).Replace("http://example.com/ns#", string.Empty, StringComparison.Ordinal));
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }
}
