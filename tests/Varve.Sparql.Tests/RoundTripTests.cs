// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Text;
using CsCheck;
using Varve.Sparql.Algebra;
using Xunit;

namespace Varve.Sparql.Tests;

/// <summary>
/// The serialiser's whole contract (<c>docs/spec/sparql-algebra.md</c> §6):
/// for every tree in the parser's image, parsing what the writer wrote gives
/// the identical tree. A counterexample here is a bug in the writer or the
/// parser, and the report shows the text so that it can be told which.
/// </summary>
public class RoundTripTests
{
    private const int Iterations = 3_000;

    [Fact]
    public void a_generated_query_survives_write_then_parse()
    {
        Gen.Int[0, int.MaxValue].Sample(
            seed =>
            {
                Query query = new AlgebraGenerator(seed).Query();
                string text = SparqlWriter.ToText(query);
                Query again = SparqlParser.ParseQuery(Encoding.UTF8.GetBytes(text));

                // The texts first: a difference there is readable, one in the trees is not.
                Assert.Equal(text, SparqlWriter.ToText(again));
                Assert.Equal(query, again);
            },
            iter: Iterations,
            print: seed => "seed " + seed + "\n" + SparqlWriter.ToText(new AlgebraGenerator(seed).Query()));
    }

    [Fact]
    public void a_generated_update_survives_write_then_parse()
    {
        Gen.Int[0, int.MaxValue].Sample(
            seed =>
            {
                Update update = new AlgebraGenerator(seed).Update();
                string text = SparqlWriter.ToText(update);
                Update again = SparqlParser.ParseUpdate(Encoding.UTF8.GetBytes(text));
                Assert.Equal(text, SparqlWriter.ToText(again));
                Assert.Equal(update, again);
            },
            iter: Iterations,
            print: seed => "seed " + seed + "\n" + SparqlWriter.ToText(new AlgebraGenerator(seed).Update()));
    }

    [Fact]
    public void the_written_text_is_a_fixed_point_and_utf16_agrees()
    {
        Gen.Int[0, 5_000].Sample(
            seed =>
            {
                Query query = new AlgebraGenerator(seed).Query();
                string text = SparqlWriter.ToText(query);
                Query fromChars = SparqlParser.ParseQuery(text.AsSpan());
                Assert.Equal(query, fromChars);
            },
            iter: 500,
            print: seed => "seed " + seed + "\n" + SparqlWriter.ToText(new AlgebraGenerator(seed).Query()));
    }
}
