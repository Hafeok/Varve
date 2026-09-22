using System.Collections.Generic;
using System.Text;
using Varve.Rdf;
using Xunit;
using static Varve.Turtle.Tests.Harness;

namespace Varve.Turtle.Tests;

/// <summary>
/// What the writer puts on the wire, and that reading it back gives the same
/// dataset.
/// </summary>
public class TurtleWriterTests
{
    /// <summary>
    /// Parses <paramref name="document"/> and writes it out again, with
    /// <paramref name="prefixes"/> declared first.
    /// </summary>
    private static string Round(
        string document,
        RdfSyntax syntax = RdfSyntax.Turtle,
        params (string Prefix, string Iri)[] prefixes)
    {
        ArrayBufferWriter output = new();
        TurtleWriteOptions writeOptions = new() { Syntax = syntax };

        using (TurtleWriter writer = new(output, in writeOptions))
        {
            foreach ((string prefix, string iri) in prefixes)
            {
                writer.DeclarePrefix(U(prefix), U(iri));
            }

            TurtleOptions readOptions = new() { Syntax = syntax };
            TurtleParser.Parse(U(document), (in QuadView quad) => writer.Write(in quad), in readOptions);
        }

        return Encoding.UTF8.GetString(output.Written);
    }

    [Fact]
    public void a_triple_is_written_in_full()
    {
        string written = Round("<http://a/s> <http://a/p> <http://a/o> .");

        Assert.Equal("<http://a/s> <http://a/p> <http://a/o> .\n", written);
    }

    [Fact]
    public void a_declared_prefix_is_written_and_used()
    {
        string written = Round(
            "<http://a/s> <http://a/p> <http://a/o> .", RdfSyntax.Turtle, ("p", "http://a/"));

        Assert.Equal("@prefix p: <http://a/> .\np:s p:p p:o .\n", written);
    }

    [Fact]
    public void the_empty_prefix_is_used()
    {
        string written = Round(
            "<http://a/s> <http://a/p> <http://a/o> .", RdfSyntax.Turtle, ("", "http://a/"));

        Assert.Equal("@prefix : <http://a/> .\n:s :p :o .\n", written);
    }

    [Fact]
    public void the_longest_matching_prefix_wins()
    {
        string written = Round(
            "<http://a/v/x> <http://a/p> <http://a/o> .",
            RdfSyntax.Turtle,
            ("a", "http://a/"),
            ("av", "http://a/v/"));

        Assert.Contains("av:x a:p a:o .", written, System.StringComparison.Ordinal);
    }

    [Fact]
    public void an_undeclared_prefix_is_never_invented()
    {
        string written = Round("<http://a/s> <http://a/p> <http://a/o> .");

        Assert.DoesNotContain("@prefix", written, System.StringComparison.Ordinal);
    }

    [Fact]
    public void a_remainder_needing_an_escape_is_compacted_with_one()
    {
        // '/' is not in PN_CHARS but it is in PN_LOCAL_ESC, so "s/t" is a
        // local name spelt "s\\/t" rather than a reason to give up.
        string written = Round(
            "<http://a/s/t> <http://a/p> <http://a/o> .", RdfSyntax.Turtle, ("p", "http://a/"));

        Assert.Contains("p:s\\/t p:p p:o .", written, System.StringComparison.Ordinal);
    }

    [Fact]
    public void an_iri_whose_remainder_cannot_be_a_local_name_is_written_in_full()
    {
        // A brace is in neither PN_CHARS nor PN_LOCAL_ESC, so there is no
        // spelling of it in a local name. Reaching one needs validation off,
        // which is also the only way such an IRI gets into a graph.
        ArrayBufferWriter output = new();
        TurtleWriteOptions write = default;

        using (TurtleWriter writer = new(output, in write))
        {
            writer.DeclarePrefix(U("p"), U("http://a/"));
            TurtleOptions read = new() { ValidateIris = false };
            TurtleParser.Parse(
                U("<http://a/a\\u007Bb> <http://a/p> <http://a/o> ."),
                (in QuadView quad) => writer.Write(in quad),
                in read);
        }

        string written = Encoding.UTF8.GetString(output.Written);

        Assert.Contains("<http://a/a{b>", written, System.StringComparison.Ordinal);
        Assert.Contains("p:p p:o .", written, System.StringComparison.Ordinal);
    }

    [Fact]
    public void a_percent_in_the_remainder_falls_back_to_the_full_iri()
    {
        // Written as a local name, "%20" would read back as a percent-escape
        // and mean a space, which is a different IRI.
        string written = Round(
            "@prefix p: <http://a/> .\np:a%20b p:p p:o .", RdfSyntax.Turtle, ("p", "http://a/"));

        Assert.Contains("<http://a/a%20b>", written, System.StringComparison.Ordinal);
    }

    [Fact]
    public void a_trailing_dot_in_a_local_name_is_escaped()
    {
        string written = Round(
            "@prefix p: <http://a/> .\np:a\\. p:p p:o .", RdfSyntax.Turtle, ("p", "http://a/"));

        Assert.Contains("p:a\\.", written, System.StringComparison.Ordinal);
    }

    [Fact]
    public void an_interior_dot_in_a_local_name_is_not_escaped()
    {
        string written = Round(
            "@prefix p: <http://a/> .\np:a\\.b p:p p:o .", RdfSyntax.Turtle, ("p", "http://a/"));

        Assert.Contains("p:a.b ", written, System.StringComparison.Ordinal);
    }

    [Fact]
    public void a_literal_carries_its_datatype_and_language()
    {
        string written = Round(
            "<http://a/s> <http://a/p> \"x\"@en-GB, \"1\"^^<http://a/d>, \"plain\" .");

        Assert.Contains("\"x\"@en-GB", written, System.StringComparison.Ordinal);
        Assert.Contains("\"1\"^^<http://a/d>", written, System.StringComparison.Ordinal);
        Assert.Contains("\"plain\" .", written, System.StringComparison.Ordinal);
    }

    [Fact]
    public void a_datatype_is_compacted_too()
    {
        string written = Round(
            "<http://a/s> <http://a/p> \"1\"^^<http://a/d> .", RdfSyntax.Turtle, ("p", "http://a/"));

        Assert.Contains("\"1\"^^p:d", written, System.StringComparison.Ordinal);
    }

    [Fact]
    public void rdf_type_is_not_abbreviated_to_a()
    {
        // 'a' is a second spelling of one IRI, and the writer's rule is one
        // spelling per term (turtle.md §7).
        string written = Round("<http://a/s> a <http://a/C> .");

        Assert.Contains("<http://www.w3.org/1999/02/22-rdf-syntax-ns#type>", written, System.StringComparison.Ordinal);
    }

    [Fact]
    public void a_number_keeps_its_datatype_rather_than_becoming_bare()
    {
        string written = Round("<http://a/s> <http://a/p> 1 .");

        Assert.Contains("\"1\"^^<http://www.w3.org/2001/XMLSchema#integer>", written, System.StringComparison.Ordinal);
    }

    [Fact]
    public void a_collection_is_written_as_the_triples_it_denotes()
    {
        string written = Round("<http://a/s> <http://a/p> ( <http://a/1> ) .");

        Assert.DoesNotContain("(", written, System.StringComparison.Ordinal);
        Assert.Equal(3, written.Split(" .\n").Length - 1);
    }

    [Fact]
    public void turtle_drops_a_graph_it_cannot_carry()
    {
        string written = Round(
            "<http://a/g> { <http://a/s> <http://a/p> <http://a/o> . }", RdfSyntax.Turtle);

        // Read as Turtle the input is rejected, so nothing is written; the
        // point is only that the writer never emits a graph in Turtle.
        Assert.DoesNotContain("{", written, System.StringComparison.Ordinal);
    }

    [Fact]
    public void trig_opens_a_block_for_a_named_graph()
    {
        string written = Round(
            "<http://a/g> { <http://a/s> <http://a/p> <http://a/o> . }", RdfSyntax.TriG);

        Assert.Equal("<http://a/g> {\n  <http://a/s> <http://a/p> <http://a/o> .\n}\n", written);
    }

    [Fact]
    public void trig_writes_the_default_graph_unwrapped()
    {
        string written = Round("<http://a/s> <http://a/p> <http://a/o> .", RdfSyntax.TriG);

        Assert.Equal("<http://a/s> <http://a/p> <http://a/o> .\n", written);
    }

    [Fact]
    public void trig_closes_a_block_when_the_graph_changes()
    {
        string written = Round(
            "<http://a/g> { <http://a/s> <http://a/p> <http://a/o> . }\n"
            + "<http://a/h> { <http://a/s> <http://a/p> <http://a/o> . }",
            RdfSyntax.TriG);

        Assert.Equal(2, written.Split("{\n").Length - 1);
        Assert.Equal(2, written.Split("}\n").Length - 1);
    }

    [Fact]
    public void trig_closes_a_block_when_the_default_graph_follows()
    {
        string written = Round(
            "<http://a/g> { <http://a/s> <http://a/p> <http://a/o> . }\n"
            + "<http://a/s> <http://a/p> <http://a/o> .",
            RdfSyntax.TriG);

        Assert.EndsWith("}\n<http://a/s> <http://a/p> <http://a/o> .\n", written, System.StringComparison.Ordinal);
    }

    [Fact]
    public void trig_reopens_a_block_for_interleaved_quads()
    {
        // Streaming means the writer never reorders, so interleaved input gives
        // several blocks for one graph. Both denote the same dataset.
        string written = Round(
            "<http://a/g> { <http://a/s> <http://a/p> <http://a/o> . }\n"
            + "<http://a/h> { <http://a/s> <http://a/p> <http://a/o> . }\n"
            + "<http://a/g> { <http://a/s> <http://a/q> <http://a/r> . }",
            RdfSyntax.TriG);

        Assert.Equal(3, written.Split("{\n").Length - 1);
    }

    [Fact]
    public void a_blank_node_labels_a_trig_block()
    {
        string written = Round(
            "_:g { <http://a/s> <http://a/p> <http://a/o> . }", RdfSyntax.TriG);

        Assert.StartsWith("_:", written, System.StringComparison.Ordinal);
        Assert.Contains(" {\n", written, System.StringComparison.Ordinal);
    }

    [Fact]
    public void a_declaration_between_quads_closes_an_open_block()
    {
        ArrayBufferWriter output = new();
        TurtleWriteOptions options = new() { Syntax = RdfSyntax.TriG };

        using (TurtleWriter writer = new(output, in options))
        {
            TurtleOptions read = new() { Syntax = RdfSyntax.TriG };
            TurtleParser.Parse(
                U("<http://a/g> { <http://a/s> <http://a/p> <http://a/o> . }"),
                (in QuadView quad) => writer.Write(in quad),
                in read);

            writer.DeclarePrefix(U("p"), U("http://a/"));
        }

        string written = Encoding.UTF8.GetString(output.Written);

        Assert.Equal(
            "<http://a/g> {\n  <http://a/s> <http://a/p> <http://a/o> .\n}\n@prefix p: <http://a/> .\n",
            written);
    }

    [Fact]
    public void dispose_closes_an_open_block()
    {
        string written = Round(
            "<http://a/g> { <http://a/s> <http://a/p> <http://a/o> }", RdfSyntax.TriG);

        Assert.EndsWith("}\n", written, System.StringComparison.Ordinal);
    }

    [Fact]
    public void a_base_is_declared_and_shortens_nothing()
    {
        ArrayBufferWriter output = new();
        TurtleWriteOptions options = default;

        using (TurtleWriter writer = new(output, in options))
        {
            writer.DeclareBase(U("http://a/"));
            TurtleOptions read = new() { BaseIri = U("http://a/") };
            TurtleParser.Parse(U("<s> <p> <o> ."), (in QuadView quad) => writer.Write(in quad), in read);
        }

        Assert.Equal(
            "@base <http://a/> .\n<http://a/s> <http://a/p> <http://a/o> .\n",
            Encoding.UTF8.GetString(output.Written));
    }

    [Fact]
    public void an_ill_formed_prefix_is_refused()
    {
        ArrayBufferWriter output = new();
        TurtleWriteOptions options = default;
        using TurtleWriter writer = new(output, in options);

        Assert.Throws<System.ArgumentException>(() => writer.DeclarePrefix(U("1bad"), U("http://a/")));
    }

    [Theory]
    [InlineData("<http://a/s> <http://a/p> <http://a/o> .")]
    [InlineData("@prefix p: <http://a/> .\np:s p:p \"x\"@en, 1, 1.5, true, [ p:q p:r ] .")]
    [InlineData("<http://a/s> <http://a/p> ( <http://a/1> <http://a/2> ) .")]
    [InlineData("<http://a/s> <http://a/p> \"a\\nb\\\"c\\\\d\" .")]
    [InlineData("<http://a/s> <http://a/p> \"\U0001F600\" .")]
    [InlineData("_:x <http://a/p> _:y .\n_:y <http://a/p> _:x .")]
    public void what_the_writer_produces_reads_back_as_the_same_dataset(string document)
    {
        List<string> first = Read(document, RdfSyntax.Turtle);
        string written = Round(document, RdfSyntax.Turtle, ("p", "http://a/"));
        List<string> again = Read(written, RdfSyntax.Turtle);

        Assert.Equal(Relabelled(first), Relabelled(again));
    }

    [Theory]
    [InlineData("<http://a/g> { <http://a/s> <http://a/p> <http://a/o> . }")]
    [InlineData("<http://a/s> <http://a/p> <http://a/o> .\n<http://a/g> { <http://a/s> <http://a/p> <http://a/o> . }")]
    [InlineData("_:g { <http://a/s> <http://a/p> [ <http://a/q> <http://a/r> ] . }")]
    public void what_the_writer_produces_reads_back_as_the_same_dataset_in_trig(string document)
    {
        List<string> first = Read(document, RdfSyntax.TriG);
        string written = Round(document, RdfSyntax.TriG);
        List<string> again = Read(written, RdfSyntax.TriG);

        Assert.Equal(Relabelled(first), Relabelled(again));
    }

    /// <summary>
    /// The same lines with each distinct blank node label replaced by the order
    /// it first appears in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A round trip is isomorphic and not byte-identical (<c>turtle.md</c> §7):
    /// the reader gives a document's own label <c>x</c> the name <c>bx</c>, so
    /// reading the output again names it <c>bbx</c>. The dataset is the same
    /// one; only the naming differs.
    /// </para>
    /// <para>
    /// This is a canonical relabelling and not a general isomorphism check. It
    /// is sound here only because the writer emits quads in the order it
    /// receives them, so first appearance is the same on both sides. The real
    /// bijection, which does not assume that, is the conformance project's and
    /// arrives with the evaluation tests.
    /// </para>
    /// </remarks>
    private static List<string> Relabelled(List<string> lines)
    {
        Dictionary<string, int> seen = new(System.StringComparer.Ordinal);
        List<string> result = [];

        foreach (string line in lines)
        {
            StringBuilder text = new();
            int at = 0;

            while (at < line.Length)
            {
                int start = line.IndexOf("_:", at, System.StringComparison.Ordinal);

                if (start < 0)
                {
                    text.Append(line, at, line.Length - at);
                    break;
                }

                int end = start + 2;

                while (end < line.Length && line[end] is not (' ' or '\n'))
                {
                    end++;
                }

                string label = line[(start + 2)..end];

                if (!seen.TryGetValue(label, out int index))
                {
                    index = seen.Count;
                    seen[label] = index;
                }

                text.Append(line, at, start - at).Append("_:n").Append(index);
                at = end;
            }

            result.Add(text.ToString());
        }

        return result;
    }

    /// <summary>The dataset as N-Quads lines.</summary>
    private static List<string> Read(string document, RdfSyntax syntax)
    {
        List<string> lines = [];
        ArrayBufferWriter output = new();
        WriteOptions write = new() { Syntax = RdfSyntax.NQuads };
        TurtleOptions options = new() { Syntax = syntax };

        TurtleParser.Parse(
            U(document),
            (in QuadView quad) =>
            {
                output.Reset();
                NQuadsWriter.Write(output, in quad, write);
                lines.Add(Encoding.UTF8.GetString(output.Written));
            },
            in options);

        return lines;
    }
}
