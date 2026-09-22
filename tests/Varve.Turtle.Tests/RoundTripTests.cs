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
/// Serialise, parse, compare — over generated terms rather than chosen ones.
/// </summary>
/// <remarks>
/// The W3C suites are all positive and negative <em>syntax</em> tests, so they
/// prove that the right documents are accepted and prove nothing at all about
/// the quads produced. This is where that is checked.
/// </remarks>
public class RoundTripTests
{
    private static readonly Gen<string> Lexical = Gen.String[
        Gen.OneOf(
            Gen.Char.AlphaNumeric,
            Gen.Const(' '),
            Gen.Const('"'),
            Gen.Const('\\'),
            Gen.Const('\n'),
            Gen.Const('\r'),
            Gen.Const('\t'),
            Gen.Const('#'),
            Gen.Const('<'),
            Gen.Const('é'),
            Gen.Const('中'),
            Gen.Const('�')),
        0,
        16];

    private static readonly Gen<string> Path = Gen.String[Gen.Char.AlphaNumeric, 1, 10];

    private static readonly Gen<RdfTerm> Iri =
        Path.Select(p => RdfTerm.Iri(U("http://example.org/" + p)));

    private static readonly Gen<RdfTerm> BlankNode =
        Gen.String[Gen.Char.AlphaNumeric, 1, 8].Select(l => RdfTerm.BlankNode(U("b" + l)));

    /// <summary>
    /// Every one of these is well-formed BCP 47, which the term model now
    /// requires: a generator that produced an ill-formed tag would be testing
    /// that the model rejects it, which is <c>LanguageTagTests</c>' job.
    /// </summary>
    private static readonly Gen<string> Language =
        Gen.OneOfConst("en", "en-GB", "de-DE-1901", "zh-Hans-CN", "es-419", "en-x-custom");

    private static readonly Gen<TextDirection> Direction =
        Gen.OneOfConst(TextDirection.None, TextDirection.LeftToRight, TextDirection.RightToLeft);

    private static readonly Gen<RdfTerm> Literal = Gen.OneOf(
        Lexical.Select(l => RdfTerm.Literal(Surrogates(l))),
        Gen.Select(Lexical, Path, (l, p) =>
            RdfTerm.Literal(Surrogates(l), RdfTerm.Iri(U("http://example.org/dt/" + p)))),
        Gen.Select(Lexical, Language, Direction, (l, lang, d) => RdfTerm.Literal(Surrogates(l), U(lang), d)));

    private static readonly Gen<RdfTerm> Term = Gen.Recursive<RdfTerm>((depth, term) =>
        depth > 2
            ? Gen.OneOf(Iri, BlankNode, Literal)
            : Gen.OneOf(
                Iri,
                BlankNode,
                Literal,
                Gen.Select(
                    Gen.OneOf(Iri, BlankNode),
                    Iri,
                    term,
                    RdfTerm.TripleTerm)));

    private static readonly Gen<Row> Triple = Gen.Select(
        Gen.OneOf(Iri, BlankNode),
        Iri,
        Term,
        (s, p, o) => new Row(s, p, o, null));

    private static readonly Gen<Row> Statement = Gen.Select(
        Gen.OneOf(Iri, BlankNode),
        Iri,
        Term,
        Gen.OneOf(
            Iri.Select<RdfTerm, RdfTerm?>(x => x),
            BlankNode.Select<RdfTerm, RdfTerm?>(x => x),
            Gen.Const((RdfTerm?)null)),
        (s, p, o, g) => new Row(s, p, o, g));

    /// <summary>
    /// A generated string can contain an unpaired surrogate, which is not a
    /// character and cannot be UTF-8. Replacing it keeps the generator honest
    /// about what a term can hold rather than papering over a parser defect.
    /// </summary>
    private static byte[] Surrogates(string text)
    {
        Span<char> chars = text.Length <= 64 ? stackalloc char[text.Length] : new char[text.Length];
        text.CopyTo(chars);

        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsSurrogate(chars[i]))
            {
                chars[i] = '�';
            }
        }

        return Encoding.UTF8.GetBytes(new string(chars));
    }

    private static string Serialise(Row row, RdfSyntax syntax)
    {
        WriteOptions options = new() { Syntax = syntax };
        ArrayBufferWriter output = new();
        InMemoryDataset dataset = new();

        Quad quad = new(
            dataset.Internalise(row.Subject),
            dataset.Internalise(row.Predicate),
            dataset.Internalise(row.Object),
            row.Graph is null ? TermHandle.None : dataset.Internalise(row.Graph));

        NQuadsWriter.Write(output, in quad, dataset, options);
        return Encoding.UTF8.GetString(output.Written);
    }

    private static bool Survives(Row row, RdfSyntax syntax)
    {
        string document = Serialise(row, syntax);
        (ParseResult result, List<Row> rows) = Parse(document, new ParseOptions { Syntax = syntax });

        return result.Succeeded && rows.Count == 1 && rows[0] == row;
    }

    [Fact]
    public void a_triple_survives_a_write_and_a_read()
    {
        Triple.Sample(row => Survives(row, RdfSyntax.NTriples));
    }

    [Fact]
    public void a_quad_survives_a_write_and_a_read()
    {
        Statement.Sample(row => Survives(row, RdfSyntax.NQuads));
    }

    [Fact]
    public void a_second_write_is_byte_identical_to_the_first()
    {
        Statement.Sample(row =>
        {
            string once = Serialise(row, RdfSyntax.NQuads);
            (_, List<Row> rows) = Parse(once, new ParseOptions { Syntax = RdfSyntax.NQuads });
            return rows.Count == 1 && Serialise(rows[0], RdfSyntax.NQuads) == once;
        });
    }

    [Fact]
    public void the_non_canonical_form_reads_back_to_the_same_terms()
    {
        Statement.Sample(row =>
        {
            ArrayBufferWriter output = new();
            InMemoryDataset dataset = new();

            Quad quad = new(
                dataset.Internalise(row.Subject),
                dataset.Internalise(row.Predicate),
                dataset.Internalise(row.Object),
                row.Graph is null ? TermHandle.None : dataset.Internalise(row.Graph));

            NQuadsWriter.Write(
                output, in quad, dataset, new WriteOptions { Syntax = RdfSyntax.NQuads, Canonical = false });

            (ParseResult result, List<Row> rows) = Parse(
                Encoding.UTF8.GetString(output.Written), new ParseOptions { Syntax = RdfSyntax.NQuads });

            return result.Succeeded && rows.Count == 1 && rows[0] == row;
        });
    }

    [Fact]
    public void a_whole_document_survives_a_write_and_a_read()
    {
        Statement.List[1, 25].Sample(statements =>
        {
            StringBuilder builder = new();

            foreach (Row row in statements)
            {
                builder.Append(Serialise(row, RdfSyntax.NQuads));
            }

            (ParseResult result, List<Row> rows) =
                Parse(builder.ToString(), new ParseOptions { Syntax = RdfSyntax.NQuads });

            if (!result.Succeeded || rows.Count != statements.Count)
            {
                return false;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != statements[i])
                {
                    return false;
                }
            }

            return true;
        });
    }
}
