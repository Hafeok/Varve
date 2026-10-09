// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Text;
using CsCheck;
using Varve.Rdf;
using Varve.Turtle;
using Xunit;
using static Varve.RdfXml.Tests.Harness;

namespace Varve.RdfXml.Tests;

/// <summary>
/// The RDF/XML writer against its reader over generated datasets (ADR 0111,
/// <c>rdf-xml.md</c> §6): write, read back, same dataset up to blank node
/// labels; write, read, write, same bytes. And the datasets the syntax cannot
/// express, which the writer must refuse by name rather than mangle.
/// </summary>
public class PropertyTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    private static readonly Gen<string> Iri = Gen.Int[0, 5].Select(n => "<http://a.example/n" + n + ">");

    /// <summary>Predicates whose IRIs end in an NCName, which is what RDF/XML needs.</summary>
    private static readonly Gen<string> Predicate = Gen.OneOf(
        Gen.Int[0, 4].Select(n => "<http://a.example/p" + n + ">"),
        Gen.Int[0, 2].Select(n => "<http://b.example/vocab#q" + n + ">"),
        Gen.Const("<" + Rdf + "type>"),
        Gen.Const("<http://c.example/path/with.dots/prop-x>"));

    private static readonly Gen<string> Blank = Gen.Int[0, 3].Select(n => "_:b" + n);

    private static readonly Gen<string> Text = Gen.String[Gen.OneOf(Gen.Char.AlphaNumeric, Gen.Const(' '), Gen.Const('<'), Gen.Const('&'), Gen.Const('"'), Gen.Const('\n'), Gen.Const('é'), Gen.Const('中')), 0, 12];

    private static readonly Gen<string> Literal = Gen.OneOf(
        Text.Select(t => Quote(t)),
        Text.Select(t => Quote(t) + "@en"),
        Text.Select(t => Quote(t) + "@ar--rtl"),
        Text.Select(t => Quote(t) + "@en-GB--ltr"),
        Gen.Int.Select(i => "\"" + i + "\"^^<http://www.w3.org/2001/XMLSchema#integer>"),
        Text.Select(t => Quote(t) + "^^<http://a.example/dt>"));

    private static readonly Gen<string> Node = Gen.OneOf(Iri, Blank);

    private static readonly Gen<string> TripleTerm = Gen.Select(Node, Predicate, Gen.OneOf(Iri, Blank, Literal))
        .Select(t => "<<( " + t.Item1 + " " + t.Item2 + " " + t.Item3 + " )>>");

    private static readonly Gen<string> Object = Gen.OneOf(Iri, Blank, Literal, TripleTerm);

    private static readonly Gen<string> Triple = Gen.Select(Node, Predicate, Object).Select(t => t.Item1 + " " + t.Item2 + " " + t.Item3 + " .");

    private static readonly Gen<string> Dataset = Triple.List[0, 12].Select(lines => string.Join('\n', lines) + "\n");

    [Fact]
    public void a_dataset_survives_the_writer_and_the_reader_as_the_same_dataset()
    {
        Dataset.Sample(
            nquads =>
            {
                List<string> before = Canonical(ReadNQuads(nquads));
                string xml = WriteXml(nquads, writer => writer.DeclarePrefix(U("a"), U("http://a.example/")));
                Read back = Parse(xml, baseIri: "http://doc.example/");

                if (!back.Result.Succeeded)
                {
                    throw new InvalidOperationException(back.Result.Error!.ToString() + "\n" + xml);
                }

                List<string> after = Canonical(back.Lines);
                return Same(before, after);
            },
            iter: 2000);
    }

    [Fact]
    public void writing_what_was_written_reproduces_it()
    {
        Dataset.Sample(
            nquads =>
            {
                string first = WriteXml(nquads);
                Read back = Parse(first, baseIri: "http://doc.example/");
                string second = WriteFromLines(back.Lines);
                return string.Equals(first, second, StringComparison.Ordinal);
            },
            iter: 1000);
    }

    /// <summary>
    /// What RDF/XML cannot express: a predicate with no NCName suffix, a quad
    /// with a graph label, a triple term as subject. Each is refused by name.
    /// </summary>
    [Fact]
    public void what_the_syntax_cannot_express_is_refused_by_name()
    {
        Gen<string> badPredicate = Gen.OneOf(
            Gen.Int[0, 99].Select(n => "<http://a.example/" + n + ">"),
            Gen.Const("<http://a.example/p/>"),
            Gen.Const("<http://a.example/#>"),
            Gen.Const("<http://a.example/p?>"));

        Gen.Select(Node, badPredicate, Object).Sample(
            t =>
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                    () => WriteXml(t.Item1 + " " + t.Item2 + " " + t.Item3 + " .\n"));
                return error.Message.Contains("NCName", StringComparison.Ordinal) && error.Message.Contains(t.Item2.Trim('<', '>'), StringComparison.Ordinal);
            },
            iter: 500);

        Gen.Select(Node, Predicate, Object, Iri).Sample(
            t =>
            {
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                    () => WriteXml(t.Item1 + " " + t.Item2 + " " + t.Item3 + " " + t.Item4 + " .\n"));
                return error.Message.Contains("no graphs", StringComparison.Ordinal);
            },
            iter: 200);
    }

    private static string Quote(string text) =>
        "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

    private static List<string> ReadNQuads(string nquads)
    {
        List<string> lines = [];
        ParseResult result = NQuadsParser.Parse(U(nquads), (in QuadView quad) => lines.Add(Line(in quad)), new ParseOptions { Syntax = RdfSyntax.NQuads });

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.FirstError.ToString());
        }

        return lines;
    }

    private static string WriteFromLines(List<string> lines)
    {
        StringBuilder nquads = new();

        foreach (string line in lines)
        {
            nquads.Append(line).Append(" .\n");
        }

        return WriteXml(nquads.ToString());
    }

    /// <summary>Whether two runs denote the same dataset, under a relabelling of blank nodes by first appearance.</summary>
    private static bool Same(List<string> left, List<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Lines with blank nodes numbered by first appearance, inside triple terms too; the writer keeps the order.</summary>
    private static List<string> Canonical(List<string> lines)
    {
        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        List<string> result = [];

        foreach (string line in lines)
        {
            StringBuilder text = new();
            int at = 0;

            while (at < line.Length)
            {
                int start = line.IndexOf("_:", at, StringComparison.Ordinal);

                if (start < 0 || (start > 0 && line[start - 1] is not (' ' or '(')))
                {
                    if (start < 0)
                    {
                        text.Append(line, at, line.Length - at);
                        break;
                    }

                    text.Append(line, at, start + 2 - at);
                    at = start + 2;
                    continue;
                }

                int end = start + 2;

                while (end < line.Length && line[end] is not (' ' or ')'))
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
}
