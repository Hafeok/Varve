// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using Varve.Rdf;
using Xunit;
using static Varve.Turtle.Tests.TurtleHarness;

namespace Varve.Turtle.Tests;

/// <summary>
/// RDF 1.2 syntax is not accepted by the Turtle and TriG reader.
/// </summary>
/// <remarks>
/// <para>
/// The term model carries base direction and triple terms, and N-Triples and
/// N-Quads read and write both — gated by the <c>rdf12</c> syntax suites. RDF
/// 1.2 Turtle and TriG are a different matter: their Working Drafts are days
/// old, their suites are 167 cases of reifiers, annotations, triple terms and
/// a version directive, and none of that is implemented
/// (<c>docs/roadmap.md</c>).
/// </para>
/// <para>
/// So the rule is that none of it is <em>half</em> implemented. A construct
/// accepted with no suite behind it is the defect that gating rdf12 found at
/// milestone 3a, and base direction was exactly that here until this test:
/// the reader took <c>"x"@en--ltr</c> and the writer emitted it, with nothing
/// checking either. These cases are the closed door, so that adding RDF 1.2
/// Turtle means wiring its suites rather than discovering the feature already
/// half-works.
/// </para>
/// </remarks>
public class Rdf12NotInTurtleTests
{
    [Theory]
    // LANG_DIR [154s]. Not a 1.1 LANGTAG: [144s] wants each subtag after a '-'
    // to be alphanumeric, and "" is not.
    [InlineData("<http://a/s> <http://a/p> \"x\"@en--ltr .")]
    [InlineData("<http://a/s> <http://a/p> \"x\"@ar--rtl .")]
    [InlineData("<http://a/s> <http://a/p> \"x\"@en--xyz .")]
    // Triple terms [30].
    [InlineData("<http://a/s> <http://a/p> <<( <http://a/a> <http://a/b> <http://a/c> )>> .")]
    [InlineData("<<( <http://a/a> <http://a/b> <http://a/c> )>> <http://a/p> <http://a/o> .")]
    // Reifiers [29] and annotations [27].
    [InlineData("<http://a/s> <http://a/p> <http://a/o> ~ <http://a/r> .")]
    [InlineData("<http://a/s> <http://a/p> <http://a/o> ~ .")]
    [InlineData("<http://a/s> <http://a/p> <http://a/o> {| <http://a/q> <http://a/r> |} .")]
    // The version directive [4].
    [InlineData("VERSION \"1.2\"\n<http://a/s> <http://a/p> <http://a/o> .")]
    [InlineData("@version \"1.2\" .\n<http://a/s> <http://a/p> <http://a/o> .")]
    public void rdf12_syntax_is_rejected_in_turtle_and_trig(string document)
    {
        Assert.False(Parse(document).Result.Succeeded, "Turtle accepted it");
        Assert.False(ParseTriG(document).Result.Succeeded, "TriG accepted it");
    }

    [Fact]
    public void an_rdf11_language_tag_is_still_accepted()
    {
        // The negative half: rejecting the direction must not reject the tag.
        Read read = Parse("<http://a/s> <http://a/p> \"x\"@en-GB .");

        Assert.True(read.Result.Succeeded, read.Result.FirstError.ToString());
        Assert.Equal(["<http://a/s> <http://a/p> \"x\"@en-GB"], read.Lines());
    }

    [Fact]
    public void the_writer_refuses_a_literal_carrying_a_base_direction()
    {
        // The realistic route to such a term: read N-Triples, where RDF 1.2 is
        // gated and supported, then try to write Turtle. A writer that emitted
        // it would produce a document this library's own reader rejects;
        // dropping it would write a different term.
        Harness.ArrayBufferWriter output = new();
        TurtleWriteOptions options = default;

        System.InvalidOperationException error = Assert.Throws<System.InvalidOperationException>(
            () =>
            {
                using TurtleWriter writer = new(output, in options);
                NQuadsParser.Parse(
                    Harness.U("<http://a/s> <http://a/p> \"x\"@en--ltr .\n"),
                            (in QuadView quad) => writer.Write(in quad),
                    default);
            });

        Assert.Contains("base direction", error.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void n_quads_still_writes_a_base_direction()
    {
        // The direction is not unsupported, only unwritable in this syntax.
        Harness.ArrayBufferWriter output = new();
        WriteOptions write = new() { Syntax = RdfSyntax.NQuads };

        NQuadsParser.Parse(
            Harness.U("<http://a/s> <http://a/p> \"x\"@en--ltr .\n"),
            (in QuadView quad) => NQuadsWriter.Write(output, in quad, write),
            default);

        Assert.Contains(
            "@en--ltr", Encoding.UTF8.GetString(output.Written), System.StringComparison.Ordinal);
    }
}
