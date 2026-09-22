// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Text;
using CsCheck;
using Varve.Rdf;
using Xunit;
using static Varve.Turtle.Tests.Harness;

namespace Varve.Turtle.Tests;

/// <summary>
/// What blank nodes are called, and that writing does not rename them.
/// </summary>
public class BlankNodeNamingTests
{
    private static string Write(string document, RdfSyntax syntax = RdfSyntax.Turtle)
    {
        ArrayBufferWriter output = new();
        TurtleWriteOptions write = new() { Syntax = syntax };

        using (TurtleWriter writer = new(output, in write))
        {
            TurtleOptions read = new() { Syntax = syntax };
            TurtleParser.Parse(U(document), (in QuadView quad) => writer.Write(in quad), in read);
        }

        return Encoding.UTF8.GetString(output.Written);
    }

    [Fact]
    public void a_documents_own_label_is_left_alone()
    {
        Assert.Equal("_:x <http://a/p> _:y .\n", Write("_:x <http://a/p> _:y ."));
    }

    [Fact]
    public void an_anonymous_node_is_named_from_the_generated_series()
    {
        Assert.Equal("_:g0 <http://a/p> <http://a/o> .\n", Write("[] <http://a/p> <http://a/o> ."));
    }

    [Fact]
    public void a_label_and_an_anonymous_node_do_not_collide()
    {
        string written = Write("_:g0 <http://a/p> <http://a/o> .\n[] <http://a/q> <http://a/r> .");

        Assert.Equal("_:g0 <http://a/p> <http://a/o> .\n_:g1 <http://a/q> <http://a/r> .\n", written);
    }

    [Fact]
    public void a_claim_after_the_number_was_handed_out_renames_the_claim()
    {
        // The only case the naming cannot honour: g0 is already an anonymous
        // node's by the time the document asks for it. turtle.md §4 says the
        // document's occurrence is the one that moves.
        string written = Write("[] <http://a/q> <http://a/r> .\n_:g0 <http://a/p> <http://a/o> .");

        Assert.Equal("_:g0 <http://a/q> <http://a/r> .\n_:g1 <http://a/p> <http://a/o> .\n", written);
    }

    [Fact]
    public void a_renamed_claim_stays_renamed_the_same_way()
    {
        string written = Write(
            "[] <http://a/q> <http://a/r> .\n_:g0 <http://a/p> <http://a/o> .\n_:g0 <http://a/s> <http://a/t> .");

        Assert.Equal(
            "_:g0 <http://a/q> <http://a/r> .\n_:g1 <http://a/p> <http://a/o> .\n"
            + "_:g1 <http://a/s> <http://a/t> .\n",
            written);
    }

    [Fact]
    public void a_claim_and_a_mint_in_one_statement_do_not_collide()
    {
        // Found by the property test after the naming was made allocation-free:
        // a claim recorded for the statement but not yet committed was
        // invisible to the generator, which then handed out the number the
        // claim had just taken. Two nodes, one label.
        string written = Write("_:g0 <http://a/p> [ <http://a/q> <http://a/r> ] .");

        Assert.Equal(2, Labels(written).Count);
    }

    [Fact]
    public void a_claim_and_several_mints_in_one_statement_do_not_collide()
    {
        string written = Write(
            "<http://a/s> <http://a/p> [ <http://a/q> [] ] .\n_:g0 <http://a/p> _:g1 .\n"
            + "<http://a/s> <http://a/p> [] .\n[] <http://a/p> <http://a/o> .");

        // Two from the first statement, two claims, then two more mints.
        Assert.Equal(6, Labels(written).Count);
    }

    [Fact]
    public void the_generator_skips_a_number_the_document_claimed_first()
    {
        string written = Write("_:g0 <http://a/p> <http://a/o> .\n[] <http://a/q> ( <http://a/1> ) .");

        Assert.DoesNotContain("_:g0 <http://a/q>", written, System.StringComparison.Ordinal);

        // Three nodes: the document's g0, the property list, and the one list
        // node a single-member collection denotes.
        Assert.Equal(3, Labels(written).Count);
    }

    [Theory]
    [InlineData("g007")]
    [InlineData("gx")]
    [InlineData("g")]
    [InlineData("0g")]
    [InlineData("g1x")]
    public void a_label_that_only_looks_generated_is_not_treated_as_one(string label)
    {
        // "g007" is not the generated form: the generator never writes a
        // leading zero, and reading it as g7 would merge two labels the
        // document kept apart.
        string written = Write($"_:{label} <http://a/p> <http://a/o> .\n[] <http://a/q> <http://a/r> .");

        Assert.Contains($"_:{label} ", written, System.StringComparison.Ordinal);
        Assert.Contains("_:g0 <http://a/q>", written, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("_:x <http://a/p> _:y .")]
    [InlineData("[] <http://a/p> <http://a/o> .")]
    [InlineData("<http://a/s> <http://a/p> ( <http://a/1> <http://a/2> ) .")]
    [InlineData("<http://a/s> <http://a/p> [ <http://a/q> [ <http://a/r> <http://a/t> ] ] .")]
    [InlineData("_:g0 <http://a/p> <http://a/o> .\n[] <http://a/q> <http://a/r> .")]
    [InlineData("[] <http://a/q> <http://a/r> .\n_:g0 <http://a/p> <http://a/o> .")]
    [InlineData("_:g1 <http://a/p> _:g0 .\n[] <http://a/q> [] .")]
    public void write_parse_write_is_byte_identical(string document)
    {
        string first = Write(document);
        string second = Write(first);

        Assert.Equal(first, second);
    }

    [Fact]
    public void write_parse_write_is_byte_identical_in_trig()
    {
        string document = "<http://a/g> { [] <http://a/p> ( <http://a/1> ) . }\n"
            + "_:g0 <http://a/p> <http://a/o> .";
        string first = Write(document, RdfSyntax.TriG);
        string second = Write(first, RdfSyntax.TriG);

        Assert.Equal(first, second);
    }

    /// <summary>
    /// A document of statements that mix named and anonymous blank nodes, with
    /// labels drawn deliberately from the generated series so that the claim
    /// and rename paths are exercised rather than avoided.
    /// </summary>
    private static readonly Gen<string> Statement = Gen.OneOfConst(
        "_:a <http://a/p> _:b .",
        "_:g0 <http://a/p> _:g1 .",
        "_:g2 <http://a/p> <http://a/o> .",
        "[] <http://a/p> <http://a/o> .",
        "<http://a/s> <http://a/p> [] .",
        "<http://a/s> <http://a/p> ( <http://a/1> <http://a/2> ) .",
        "<http://a/s> <http://a/p> [ <http://a/q> [] ] .",
        "_:g10 <http://a/p> [ <http://a/q> ( [] ) ] .");

    [Fact]
    public void writing_is_a_fixed_point_for_any_mixture_of_named_and_anonymous_nodes()
    {
        Statement.List[1, 12]
            .Select(statements => string.Join('\n', statements))
            .Sample(
                document =>
                {
                    string first = Write(document);
                    string second = Write(first);
                    return string.Equals(first, second, System.StringComparison.Ordinal);
                },
                iter: 2000);
    }

    [Fact]
    public void every_distinct_node_keeps_a_distinct_label()
    {
        // The property the renaming exists for: whatever else happens, two
        // nodes never end up sharing a name.
        Statement.List[1, 12]
            .Select(statements => string.Join('\n', statements))
            .Sample(
                document =>
                {
                    string written = Write(document);
                    int before = CountNodes(document);
                    return Labels(written).Count == before;
                },
                iter: 2000);
    }

    /// <summary>The distinct blank node labels in a written document.</summary>
    private static HashSet<string> Labels(string written)
    {
        HashSet<string> labels = new(System.StringComparer.Ordinal);
        int at = 0;

        while (true)
        {
            int start = written.IndexOf("_:", at, System.StringComparison.Ordinal);

            if (start < 0)
            {
                return labels;
            }

            int end = start + 2;

            while (end < written.Length && written[end] is not (' ' or '\n'))
            {
                end++;
            }

            labels.Add(written[(start + 2)..end]);
            at = end;
        }
    }

    /// <summary>
    /// How many distinct blank nodes the source document denotes: each distinct
    /// written label, plus one per anonymous construct.
    /// </summary>
    private static int CountNodes(string document)
    {
        HashSet<string> named = Labels(document);
        int anonymous = 0;

        for (int i = 0; i < document.Length; i++)
        {
            if (document[i] == '[')
            {
                anonymous++;
            }
            else if (document[i] == '(')
            {
                // A collection of n members is n list nodes.
                for (int j = i + 1; j < document.Length && document[j] != ')'; j++)
                {
                    if (document[j] == '<' || document[j] == '[')
                    {
                        anonymous++;
                    }
                }
            }
        }

        return named.Count + anonymous;
    }
}
