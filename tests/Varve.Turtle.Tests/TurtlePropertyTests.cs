// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using CsCheck;
using Varve.Rdf;
using Xunit;
using static Varve.Turtle.Tests.Harness;

namespace Varve.Turtle.Tests;

/// <summary>
/// Properties of the Turtle reader and writer over generated input.
/// </summary>
/// <remarks>
/// The corpus tests say what the W3C suites contain; these say what is true of
/// input nobody wrote down. The generators are deliberately hostile in the
/// places the grammar is delicate — a quote inside a long string, a backslash
/// before a quote, a surrogate pair, a character that must be escaped in one
/// quoting form and not another.
/// </remarks>
public class TurtlePropertyTests
{
    /// <summary>
    /// Characters chosen because each is a decision point for some quoting
    /// form: the two quote characters, the escape character, the line
    /// terminators, and text outside the basic plane.
    /// </summary>
    private static readonly Gen<string> Lexical = Gen.String[
        Gen.OneOf(
            Gen.Char.AlphaNumeric,
            Gen.Const(' '),
            Gen.Const('"'),
            Gen.Const('\''),
            Gen.Const('\\'),
            Gen.Const('\n'),
            Gen.Const('\r'),
            Gen.Const('\t'),
            Gen.Const('\b'),
            Gen.Const('\f'),
            Gen.Const('#'),
            Gen.Const('<'),
            Gen.Const('>'),
            Gen.Const('é'),
            Gen.Const('中'),
            // The two halves of a surrogate pair, so that generated text can carry
            // astral characters and, when a half lands alone, the ill-formed case
            // the reader has to refuse to mangle.
            Gen.Const('\uD83D'),
            Gen.Const('\uDE00')),
        0,
        24];

    /// <summary>
    /// The four ways Turtle can spell a string [17], as a function from the
    /// text to the document.
    /// </summary>
    private enum Quoting
    {
        Double,
        Single,
        LongDouble,
        LongSingle,
    }

    private static readonly Gen<Quoting> AnyQuoting =
        Gen.OneOfConst(Quoting.Double, Quoting.Single, Quoting.LongDouble, Quoting.LongSingle);

    /// <summary>
    /// Writes <paramref name="text"/> in one of the four forms, escaping only
    /// what that form requires.
    /// </summary>
    /// <remarks>
    /// This is the generator's own escaper, not the writer's: using the writer
    /// to produce the input would make the property "the writer agrees with
    /// itself", which is not the question. A long form may carry a raw
    /// newline and a lone quote; a short one may not.
    /// </remarks>
    private static string Quote(string text, Quoting form)
    {
        bool longForm = form is Quoting.LongDouble or Quoting.LongSingle;
        char quote = form is Quoting.Double or Quoting.LongDouble ? '"' : '\'';
        string delimiter = longForm ? new string(quote, 3) : quote.ToString();
        StringBuilder written = new(delimiter);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '\\')
            {
                written.Append("\\\\");
            }
            else if (c == quote)
            {
                written.Append('\\').Append(quote);
            }
            else if (c is '\n' or '\r')
            {
                written.Append(c == '\n' ? "\\n" : "\\r");
            }
            else if (c == '\t')
            {
                written.Append("\\t");
            }
            else if (c == '\b')
            {
                written.Append("\\b");
            }
            else if (c == '\f')
            {
                written.Append("\\f");
            }
            else
            {
                written.Append(c);
            }
        }

        return written.Append(delimiter).ToString();
    }

    /// <summary>The object of the one triple in <paramref name="document"/>.</summary>
    private static RdfTerm? Object(string document)
    {
        RdfTerm? found = null;
        TurtleOptions options = default;
        ParseResult result = TurtleParser.Parse(
            U(document), (in QuadView quad) => found = quad.Object.Materialise(), in options);

        return result.Succeeded ? found : null;
    }

    [Fact]
    public void every_quoting_form_denotes_the_same_literal()
    {
        // The property the four forms exist for: they are spellings, not
        // meanings. A reader that mishandled one would produce a different
        // term for the same text, and no single-form test would see it.
        Lexical.Sample(
            text =>
            {
                RdfTerm? first = null;

                foreach (Quoting form in Enum.GetValues<Quoting>())
                {
                    RdfTerm? term = Object($"<http://a/s> <http://a/p> {Quote(text, form)} .");

                    if (term is null)
                    {
                        return false;
                    }

                    if (first is null)
                    {
                        first = term;
                    }
                    else if (!first.Equals(term))
                    {
                        return false;
                    }
                }

                return first is not null && S(first.Lexical) == Surrogates(text);
            },
            iter: 2000);
    }

    [Fact]
    public void a_literal_survives_the_writer_in_whatever_form_it_arrived()
    {
        // Parse from one form, write, parse again: the term is the same, and
        // the writer's choice of form is its own business.
        Gen.Select(Lexical, AnyQuoting).Sample(
            pair =>
            {
                string document = $"<http://a/s> <http://a/p> {Quote(pair.Item1, pair.Item2)} .";
                RdfTerm? before = Object(document);

                if (before is null)
                {
                    return false;
                }

                ArrayBufferWriter output = new();
                TurtleWriteOptions write = default;
                TurtleOptions read = default;

                using (TurtleWriter writer = new(output, in write))
                {
                    TurtleParser.Parse(U(document), (in QuadView quad) => writer.Write(in quad), in read);
                }

                RdfTerm? after = Object(Encoding.UTF8.GetString(output.Written));
                return after is not null && before.Equals(after);
            },
            iter: 2000);
    }

    [Theory]
    // Cases the generator reaches rarely and that each broke something once in
    // some parser or other, kept as names rather than left to chance.
    [InlineData("\"\"\"a\"b\"\"\"", "a\"b")]
    [InlineData("\"\"\"a\"\"b\"\"\"", "a\"\"b")]
    [InlineData("'''a'b'''", "a'b")]
    [InlineData("'''a''b'''", "a''b")]
    [InlineData("\"\"\"\\\"\\\"\\\"\"\"\"", "\"\"\"")]
    [InlineData("\"a\\\\\"", "a\\")]
    [InlineData("\"\\\\\\\\\"", "\\\\")]
    [InlineData("\"\\u0041\\U0001F600\"", "A\U0001F600")]
    [InlineData("'''\n'''", "\n")]
    [InlineData("\"\"", "")]
    [InlineData("''''''", "")]
    public void a_quoting_corner_reads_as_its_text(string written, string expected)
    {
        RdfTerm? term = Object($"<http://a/s> <http://a/p> {written} .");

        Assert.NotNull(term);
        Assert.Equal(expected, S(term.Lexical));
    }

    /// <summary>
    /// Whole documents: a mixture of the constructs that carry state across a
    /// statement, parsed and written and parsed again.
    /// </summary>
    private static readonly Gen<string> Statement = Gen.OneOfConst(
        "<http://a/s> <http://a/p> <http://a/o> .",
        "p:s p:p p:o .",
        "p:s p:p \"x\"@en , 1 , 1.5 , 1.5e3 , true , \"\"\"long\nstring\"\"\" .",
        "p:s p:q [ p:r ( <rel> p:t [] ) ] .",
        "[] p:p [] .",
        "_:a p:p _:b .",
        "p:s a p:C ; p:p p:o , p:o2 ; p:q () .",
        "<http://a/s> <http://a/p> \"a\\\"b\\\\c\" .");

    private static readonly Gen<string> Document =
        Statement.List[1, 10].Select(s => "@prefix p: <http://a/> .\n" + string.Join('\n', s));

    [Fact]
    public void a_document_survives_a_round_trip_as_the_same_dataset()
    {
        Document.Sample(
            document =>
            {
                List<string> before = Read(document);
                string written = Write(document);
                List<string> after = Read(written);

                return Same(before, after);
            },
            iter: 1000);
    }

    [Fact]
    public void writing_twice_gives_the_same_bytes()
    {
        // The fixed point from turtle.md §7, over generated documents rather
        // than over the corpus.
        Document.Sample(
            document =>
            {
                string first = Write(document);
                return string.Equals(first, Write(first), StringComparison.Ordinal);
            },
            iter: 1000);
    }

    private static string Write(string document)
    {
        ArrayBufferWriter output = new();
        TurtleWriteOptions write = default;
        TurtleOptions read = new() { BaseIri = U("http://a/base/") };

        using (TurtleWriter writer = new(output, in write))
        {
            writer.DeclarePrefix(U("p"), U("http://a/"));
            TurtleParser.Parse(U(document), (in QuadView quad) => writer.Write(in quad), in read);
        }

        return Encoding.UTF8.GetString(output.Written);
    }

    /// <summary>The dataset as N-Quads lines, blank nodes numbered by first appearance.</summary>
    private static List<string> Read(string document)
    {
        List<string> lines = [];
        ArrayBufferWriter output = new();
        WriteOptions write = new() { Syntax = RdfSyntax.NQuads };
        TurtleOptions read = new() { BaseIri = U("http://a/base/") };

        TurtleParser.Parse(
            U(document),
            (in QuadView quad) =>
            {
                output.Reset();
                NQuadsWriter.Write(output, in quad, write);
                lines.Add(Encoding.UTF8.GetString(output.Written));
            },
            in read);

        return lines;
    }

    /// <summary>
    /// Whether two runs denote the same dataset, under a relabelling of blank
    /// nodes by first appearance across the whole run.
    /// </summary>
    /// <remarks>
    /// Sound only because the writer emits quads in the order it receives
    /// them, so first appearance is the same on both sides. The general
    /// bijection, which assumes nothing of the kind, is the conformance
    /// project's.
    /// </remarks>
    private static bool Same(List<string> left, List<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        List<string> a = Canonical(left);
        List<string> b = Canonical(right);

        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> Canonical(List<string> lines)
    {
        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        List<string> result = [];

        foreach (string line in lines)
        {
            result.Add(Canonical(line, seen));
        }

        return result;
    }

    private static string Canonical(string line, Dictionary<string, int> seen)
    {
        StringBuilder text = new();
        int at = 0;

        while (at < line.Length)
        {
            int start = line.IndexOf("_:", at, StringComparison.Ordinal);

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

        return text.ToString();
    }

    /// <summary>
    /// A generated string may contain a lone surrogate, which is not text and
    /// cannot survive UTF-8. Replacing it is what the N-Triples property tests
    /// do, for the same reason.
    /// </summary>
    private static string Surrogates(string text)
    {
        StringBuilder rebuilt = new(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                rebuilt.Append(c).Append(text[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                rebuilt.Append('�');
            }
            else
            {
                rebuilt.Append(c);
            }
        }

        return rebuilt.ToString();
    }
}
