// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsCheck;
using Varve.Rdf;
using Xunit;

namespace Varve.Sparql.Results.Tests;

/// <summary>
/// The writers (<c>sparql-results.md</c> §5, §6): what each writes, the round
/// trip through this package's reader as a property, the call order, and
/// allocation.
/// </summary>
public class WriterTests
{
    /// <summary>The round trip's iterations per format.</summary>
    internal const int Iterations = 5_000;

    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    private static RdfTerm Iri(string s) => RdfTerm.Iri(U(s));

    private static RdfTerm Typed(string lexical, string datatype) => RdfTerm.Literal(U(lexical), Iri(datatype));

    private const string Xsd = "http://www.w3.org/2001/XMLSchema#";

    // ---- The generators. Every escape each format has, and the characters around them.

    private static readonly Gen<char> TextChar = Gen.OneOf(
        Gen.Char.AlphaNumeric,
        Gen.OneOfConst(' ', '"', '\\', '\n', '\r', '\t', '\b', '\f', '\u0001', '\u001F', '\u007F', ',', '<', '>', '&', ';', '#', '@', '^', 'é', '中', '\u2028'));

    // A non-BMP character, as a surrogate pair, is a character a term can hold.
    private static readonly Gen<string> Text = Gen.OneOf(
        Gen.String[TextChar, 0, 12],
        Gen.String[TextChar, 0, 6].Select(s => s + "😀"));

    private static readonly Gen<RdfTerm> IriTerm =
        Gen.OneOfConst("s", "p", "o", "é", "a%20b", "x#frag").Select(p => Iri("http://example.org/" + p));

    private static readonly Gen<RdfTerm> Blank =
        Gen.Int[0, 5].Select(i => RdfTerm.BlankNode(U("b" + i.ToString(CultureInfo.InvariantCulture))));

    private static readonly Gen<RdfTerm> Literal = Gen.OneOf(
        Text.Select(t => RdfTerm.Literal(U(t))),
        Gen.Select(Text, Gen.OneOfConst("en", "en-GB", "zh-Hans-CN"), Gen.OneOfConst(TextDirection.None, TextDirection.LeftToRight, TextDirection.RightToLeft),
            (t, l, d) => RdfTerm.Literal(U(t), U(l), d)),
        Gen.Select(Gen.OneOfConst("1", "-7", "+3", "01", " 1", "1.5", ".5", "1.", "1e3", "1.5E-2", ".5e1", "e1", "true", "false", "1x"),
            Gen.OneOfConst("integer", "decimal", "double", "boolean", "int", "dateTime"),
            (l, t) => Typed(l, Xsd + t)),
        Text.Select(t => Typed(t, "http://example.org/dt")));

    private static readonly Gen<RdfTerm> Term = Gen.Recursive<RdfTerm>((depth, term) =>
        depth > 2
            ? Gen.OneOf(IriTerm, Blank, Literal)
            : Gen.OneOf(IriTerm, Blank, Literal, Literal, Gen.Select(Gen.OneOf(IriTerm, Blank), IriTerm, term, RdfTerm.TripleTerm)));

    private static readonly Gen<(string[] Variables, RdfTerm?[][] Rows)> Table =
        Gen.Int[1, 4].SelectMany(width => Gen.Select(
            Gen.Const(Enumerable.Range(0, width).Select(i => "v" + i.ToString(CultureInfo.InvariantCulture)).ToArray()),
            Gen.OneOf(Term.Select<RdfTerm, RdfTerm?>(t => t), Gen.Const((RdfTerm?)null)).Array[width].Array[0, 8]));

    // ---- The round trip.

    [Theory]
    [InlineData(SparqlResultsFormat.Xml)]
    [InlineData(SparqlResultsFormat.Json)]
    [InlineData(SparqlResultsFormat.Tsv)]
    [InlineData(SparqlResultsFormat.Csv)]
    public void Writing_then_reading_gives_back_what_the_format_carries(SparqlResultsFormat format)
    {
        Table.Sample(
            table =>
            {
                if (format == SparqlResultsFormat.Xml && table.Rows.Any(r => r.Any(t => t is not null && !XmlCanCarry(t))))
                {
                    // §5.1: refused, and the refusal is its own test below.
                    return;
                }

                string expected = Expected(table.Variables, table.Rows, format);
                string actual = ResultDocument.Render(Write(table.Variables, table.Rows, format), format);
                Assert.Equal(expected, actual);
            },
            iter: Iterations);
    }

    [Theory]
    [InlineData(SparqlResultsFormat.Xml)]
    [InlineData(SparqlResultsFormat.Json)]
    public void A_boolean_round_trips(SparqlResultsFormat format)
    {
        foreach (bool value in new[] { true, false })
        {
            byte[] document = WriteBoolean(value, format);
            Assert.Equal("boolean " + (value ? "true" : "false") + "\n", ResultDocument.Render(document, format));
        }
    }

    // ---- Exact output, format by format.

    [Fact]
    public void Json_is_written_exactly()
    {
        RdfTerm?[][] rows =
        [
            [Iri("http://example.org/s"), RdfTerm.Literal(U("a\"b\\c\n\u0001é"), U("en"), TextDirection.RightToLeft)],
            [RdfTerm.BlankNode(U("b0")), null],
            [RdfTerm.TripleTerm(Iri("http://e/s"), Iri("http://e/p"), Typed("1", Xsd + "integer")), RdfTerm.Literal(U("x"))],
        ];

        Assert.Equal(
            "{\"head\":{\"vars\":[\"s\",\"o\"]},\"results\":{\"bindings\":["
            + "{\"s\":{\"type\":\"uri\",\"value\":\"http://example.org/s\"},\"o\":{\"type\":\"literal\",\"value\":\"a\\\"b\\\\c\\n\\u0001é\",\"xml:lang\":\"en\",\"its:dir\":\"rtl\"}},"
            + "{\"s\":{\"type\":\"bnode\",\"value\":\"b0\"}},"
            + "{\"s\":{\"type\":\"triple\",\"value\":{\"subject\":{\"type\":\"uri\",\"value\":\"http://e/s\"},\"predicate\":{\"type\":\"uri\",\"value\":\"http://e/p\"},\"object\":{\"type\":\"literal\",\"value\":\"1\",\"datatype\":\"http://www.w3.org/2001/XMLSchema#integer\"}}},\"o\":{\"type\":\"literal\",\"value\":\"x\"}}"
            + "]}}\n",
            Encoding.UTF8.GetString(Write(["s", "o"], rows, SparqlResultsFormat.Json)));
        Assert.Equal("{\"head\":{},\"boolean\":true}\n", Encoding.UTF8.GetString(WriteBoolean(true, SparqlResultsFormat.Json)));
    }

    [Fact]
    public void Xml_is_written_exactly()
    {
        RdfTerm?[][] rows =
        [
            [Iri("http://example.org/?a=1&b=2"), RdfTerm.Literal(U("<a>\r\n\tb"), U("en"), TextDirection.LeftToRight)],
            [RdfTerm.BlankNode(U("b0")), Typed("1", Xsd + "integer")],
        ];

        Assert.Equal(
            "<?xml version=\"1.0\"?>\n<sparql xmlns=\"http://www.w3.org/2005/sparql-results#\" xmlns:its=\"http://www.w3.org/2005/11/its\">\n"
            + "<head><variable name=\"s\"/><variable name=\"o\"/></head>\n<results>\n"
            + "<result><binding name=\"s\"><uri>http://example.org/?a=1&amp;b=2</uri></binding><binding name=\"o\"><literal xml:lang=\"en\" its:dir=\"ltr\">&lt;a&gt;&#13;\n\tb</literal></binding></result>\n"
            + "<result><binding name=\"s\"><bnode>b0</bnode></binding><binding name=\"o\"><literal datatype=\"http://www.w3.org/2001/XMLSchema#integer\">1</literal></binding></result>\n"
            + "</results>\n</sparql>\n",
            Encoding.UTF8.GetString(Write(["s", "o"], rows, SparqlResultsFormat.Xml)));
    }

    [Fact]
    public void Xml_refuses_a_character_it_cannot_carry()
    {
        foreach (string text in new[] { "a\u0001b", "\u0000", "a\uFFFE", "\uFFFF" })
        {
            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                Write(["o"], [[RdfTerm.Literal(U(text))]], SparqlResultsFormat.Xml));
            Assert.Contains("Char production", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Tsv_is_written_exactly()
    {
        RdfTerm?[][] rows =
        [
            [Iri("http://example.org/s"), Typed("4", Xsd + "integer"), Typed("5.5", Xsd + "decimal")],
            [RdfTerm.BlankNode(U("b0")), RdfTerm.Literal(U("a\tb\"c"), U("en"), TextDirection.RightToLeft), null],
            [null, Typed("01.", Xsd + "decimal"), RdfTerm.TripleTerm(Iri("http://e/s"), Iri("http://e/p"), Typed("true", Xsd + "boolean"))],
        ];

        Assert.Equal(
            "?s\t?o\t?x\n"
            + "<http://example.org/s>\t4\t5.5\n"
            + "_:b0\t\"a\\tb\\\"c\"@en--rtl\t\n"
            + "\t\"01.\"^^<http://www.w3.org/2001/XMLSchema#decimal>\t<<( <http://e/s> <http://e/p> true )>>\n",
            Encoding.UTF8.GetString(Write(["s", "o", "x"], rows, SparqlResultsFormat.Tsv)));
        Assert.Equal("?_askResult\nfalse\n", Encoding.UTF8.GetString(WriteBoolean(false, SparqlResultsFormat.Tsv)));
    }

    [Fact]
    public void Csv_is_written_exactly()
    {
        RdfTerm?[][] rows =
        [
            [Iri("http://example.org/s"), RdfTerm.Literal(U("String-with-dquote\""))],
            [RdfTerm.BlankNode(U("b0")), RdfTerm.Literal(U("a,b"), U("en"))],
            [null, RdfTerm.TripleTerm(Iri("http://e/s"), Iri("http://e/p"), RdfTerm.Literal(U("x\ny")))],
            [null, null],
        ];

        Assert.Equal(
            "x,literal\r\n"
            + "http://example.org/s,\"String-with-dquote\"\"\"\r\n"
            + "_:b0,\"a,b\"\r\n"
            + ",\"<<( http://e/s http://e/p x\ny )>>\"\r\n"
            + ",\r\n",
            Encoding.UTF8.GetString(Write(["x", "literal"], rows, SparqlResultsFormat.Csv)));
        Assert.Equal("_askResult\r\ntrue\r\n", Encoding.UTF8.GetString(WriteBoolean(true, SparqlResultsFormat.Csv)));
    }

    // ---- The call order.

    [Fact]
    public void Each_call_out_of_order_throws()
    {
        ArrayBufferWriter<byte> output = new();

        using (SparqlResultsWriter writer = new(output, SparqlResultsFormat.Json))
        {
            Assert.Throws<InvalidOperationException>(writer.StartSolution);
            Assert.Throws<InvalidOperationException>(writer.WriteEnd);
            writer.WriteHead(["a", "b"]);
            Assert.Throws<InvalidOperationException>(() => writer.WriteHead(["a"]));
            Assert.Throws<InvalidOperationException>(() => writer.WriteBoolean(true));
            Assert.Throws<InvalidOperationException>(() => writer.WriteBinding(0, Iri("http://e/")));
            writer.StartSolution();
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteBinding(2, Iri("http://e/")));
            writer.WriteBinding(1, Iri("http://e/"));
            Assert.Throws<InvalidOperationException>(() => writer.WriteBinding(1, Iri("http://e/")));
            Assert.Throws<InvalidOperationException>(() => writer.WriteBinding(0, Iri("http://e/")));
            Assert.Throws<InvalidOperationException>(writer.WriteEnd);
            writer.EndSolution();
            writer.WriteEnd();
            Assert.Throws<InvalidOperationException>(writer.StartSolution);
        }
    }

    // ---- The stream path.

    [Fact]
    public async Task A_stream_is_written_only_by_a_flush()
    {
        using MemoryStream stream = new();

        using (SparqlResultsWriter writer = new(stream, SparqlResultsFormat.Tsv))
        {
            writer.WriteHead(["x"]);
            writer.StartSolution();
            writer.WriteBinding(0, Iri("http://e/"));
            writer.EndSolution();
            writer.WriteEnd();
            Assert.Equal(0, stream.Length);
            Assert.Equal(15, writer.BytesPending);
            await writer.FlushAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, writer.BytesPending);
        }

        Assert.Equal("?x\n<http://e/>\n", Encoding.UTF8.GetString(stream.ToArray()));
    }

    // ---- Allocation: nothing per solution beyond the caller's row.

    [Theory]
    [InlineData(SparqlResultsFormat.Xml)]
    [InlineData(SparqlResultsFormat.Json)]
    [InlineData(SparqlResultsFormat.Tsv)]
    [InlineData(SparqlResultsFormat.Csv)]
    public void Writing_allocates_nothing_per_solution(SparqlResultsFormat format)
    {
        RdfTerm[] row =
        [
            Iri("http://example.org/s"),
            RdfTerm.Literal(U("a \"quoted\" value, with\ttab"), U("en"), TextDirection.LeftToRight),
            RdfTerm.TripleTerm(RdfTerm.BlankNode(U("b1")), Iri("http://e/p"), Typed("1.5", Xsd + "decimal")),
        ];

        // A buffer writer that reuses one array, so that the output is not
        // what is measured.
        ReusedBuffer output = new(1 << 22);

        (long small, long large) = AllocationMeter.MeasurePair(
            () => WriteMany(output, format, row, 1_000),
            () => WriteMany(output, format, row, 10_000));

        Assert.Equal(0, large - small);
    }

    [Theory]
    [InlineData(SparqlResultsFormat.Json)]
    [InlineData(SparqlResultsFormat.Tsv)]
    public void Writing_views_from_a_reader_allocates_nothing_per_solution(SparqlResultsFormat format)
    {
        byte[] small = Write(["s", "o"], Rows(1_000), format);
        byte[] large = Write(["s", "o"], Rows(10_000), format);
        ReusedBuffer output = new(1 << 22);

        (long smallCost, long largeCost) = AllocationMeter.MeasurePair(
            () => Copy(small, format, output),
            () => Copy(large, format, output));

        // The reader's own cost is zero per solution (ReaderTests); so is the copy.
        Assert.Equal(0, largeCost - smallCost);

        static RdfTerm?[][] Rows(int n) =>
            Enumerable.Range(0, n).Select(i => new RdfTerm?[] { Iri("http://example.org/s"), RdfTerm.Literal(U("value"), U("en")) }).ToArray();
    }

    private static SparqlResultsWriter Copy(byte[] document, SparqlResultsFormat format, ReusedBuffer output)
    {
        output.Reset();
        SparqlResultsReader reader = new(new ReadOnlySequence<byte>(document), format);
        SparqlResultsWriter writer = new(output, format);
        Assert.True(reader.ReadHead());
        writer.WriteHead(reader.Variables);

        while (reader.Read())
        {
            SolutionView solution = reader.Current;
            writer.StartSolution();

            for (int i = 0; i < solution.Count; i++)
            {
                if (solution.TryGet(i, out RdfTermView term))
                {
                    writer.WriteBinding(i, term);
                }
            }

            writer.EndSolution();
        }

        writer.WriteEnd();
        return writer;
    }

    private static SparqlResultsWriter WriteMany(ReusedBuffer output, SparqlResultsFormat format, RdfTerm[] row, int count)
    {
        output.Reset();
        SparqlResultsWriter writer = new(output, format);
        writer.WriteHead(["s", "o", "t"]);

        for (int i = 0; i < count; i++)
        {
            writer.StartSolution();
            writer.WriteBinding(0, row[0]);
            writer.WriteBinding(1, row[1]);
            writer.WriteBinding(2, row[2]);
            writer.EndSolution();
        }

        writer.WriteEnd();
        return writer;
    }

    // ---- Helpers.

    internal static byte[] Write(IReadOnlyList<string> variables, RdfTerm?[][] rows, SparqlResultsFormat format)
    {
        ArrayBufferWriter<byte> output = new();

        using (SparqlResultsWriter writer = new(output, format))
        {
            writer.WriteHead(variables);

            foreach (RdfTerm?[] row in rows)
            {
                writer.StartSolution();

                for (int i = 0; i < row.Length; i++)
                {
                    if (row[i] is { } term)
                    {
                        writer.WriteBinding(i, term);
                    }
                }

                writer.EndSolution();
            }

            writer.WriteEnd();
        }

        return output.WrittenSpan.ToArray();
    }

    private static byte[] WriteBoolean(bool value, SparqlResultsFormat format)
    {
        ArrayBufferWriter<byte> output = new();

        using (SparqlResultsWriter writer = new(output, format))
        {
            writer.WriteBoolean(value);
            writer.WriteEnd();
        }

        return output.WrittenSpan.ToArray();
    }

    /// <summary>
    /// What <see cref="ResultDocument.Render(byte[], SparqlResultsFormat)"/>
    /// should give for the table after a round trip through the format.
    /// </summary>
    private static string Expected(string[] variables, RdfTerm?[][] rows, SparqlResultsFormat format)
    {
        StringBuilder text = new();
        text.Append("vars ").AppendJoin(' ', variables).Append('\n');

        foreach (RdfTerm?[] row in rows)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(" | ");
                }

                RdfTerm? term = row[i];

                if (format == SparqlResultsFormat.Csv && term is not null)
                {
                    term = CsvCarries(term);
                }

                text.Append(term is null ? "-" : ResultDocument.Term(term));
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// What CSV carries of a term (§5.5): a blank node, or the simple literal
    /// of its string value; an empty string is indistinguishable from unbound.
    /// </summary>
    private static RdfTerm? CsvCarries(RdfTerm term)
    {
        if (term.Kind == RdfTermKind.BlankNode)
        {
            return term;
        }

        string text = CsvText(term);
        return text.Length == 0 ? null : RdfTerm.Literal(U(text));

        static string CsvText(RdfTerm t) => t.Kind switch
        {
            RdfTermKind.BlankNode => "_:" + Encoding.UTF8.GetString(t.Lexical),
            RdfTermKind.TripleTerm => "<<( " + CsvText(t.Subject!) + " " + CsvText(t.Predicate!) + " " + CsvText(t.Object!) + " )>>",
            _ => Encoding.UTF8.GetString(t.Lexical),
        };
    }

    private static bool XmlCanCarry(RdfTerm term) => term.Kind switch
    {
        RdfTermKind.TripleTerm => XmlCanCarry(term.Subject!) && XmlCanCarry(term.Predicate!) && XmlCanCarry(term.Object!),
        _ => !Encoding.UTF8.GetString(term.Lexical).Any(c => c < 0x20 && c is not ('\t' or '\n' or '\r') || c is '￾' or '￿'),
    };

    private sealed class ReusedBuffer(int size) : IBufferWriter<byte>
    {
        private readonly byte[] _buffer = new byte[size];
        private int _written;

        internal void Reset() => _written = 0;

        public void Advance(int count) => _written += count;

        public Memory<byte> GetMemory(int sizeHint = 0) => _buffer.AsMemory(_written);

        public Span<byte> GetSpan(int sizeHint = 0) => _buffer.AsSpan(_written);
    }
}
