// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using Varve.Rdf;
using Varve.Turtle;
using Xunit;

namespace Varve.Shacl.Sparql.Tests;

/// <summary>Turtle in, a dataset out.</summary>
internal static class Support
{
    internal const string Prefixes = """
        @prefix sh: <http://www.w3.org/ns/shacl#> .
        @prefix ex: <http://example.com/ns#> .
        @prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#> .
        @prefix xsd: <http://www.w3.org/2001/XMLSchema#> .
        @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .

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

    internal static string Text(RdfTerm term) => Encoding.UTF8.GetString(term.Lexical);
}
