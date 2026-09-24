// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Unicode;
using Varve.Sparql.Algebra;
using Varve.Sparql.Parsing;

namespace Varve.Sparql;

/// <summary>
/// Parses SPARQL 1.1 and 1.2 Query and Update text into the algebra
/// (<c>docs/spec/sparql-grammar.md</c>, <c>docs/spec/sparql-algebra.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// UTF-8 is the native input; UTF-16 is transcoded into a pooled buffer first,
/// so positions in errors and spans are always byte offsets in the UTF-8 form.
/// The first error ends the parse: a query is one unit, and half of one is not
/// a query anybody can run.
/// </para>
/// <para>
/// Every entry point takes <see cref="SparqlParseOptions"/>; the default asks
/// for SPARQL 1.2 with no base IRI.
/// </para>
/// </remarks>
public static class SparqlParser
{
    /// <summary>Parses a query as SPARQL 1.2 with no base, throwing <see cref="SparqlParseException"/> on the first error.</summary>
    public static Query ParseQuery(ReadOnlySpan<byte> utf8) => ParseQuery(utf8, default);

    /// <summary>Parses a query, throwing <see cref="SparqlParseException"/> on the first error.</summary>
    public static Query ParseQuery(ReadOnlySpan<byte> utf8, SparqlParseOptions options) =>
        TryParseQuery(utf8, options, out Query? query, out SparqlParseError error) ? query : throw new SparqlParseException(error);

    /// <summary>Parses a query from UTF-16 as SPARQL 1.2 with no base, throwing <see cref="SparqlParseException"/> on the first error.</summary>
    public static Query ParseQuery(ReadOnlySpan<char> text) => ParseQuery(text, default);

    /// <summary>Parses a query from UTF-16, throwing <see cref="SparqlParseException"/> on the first error.</summary>
    public static Query ParseQuery(ReadOnlySpan<char> text, SparqlParseOptions options) =>
        TryParseQuery(text, options, out Query? query, out SparqlParseError error) ? query : throw new SparqlParseException(error);

    /// <summary>Parses a query; false with the error on the first one.</summary>
    public static bool TryParseQuery(ReadOnlySpan<byte> utf8, SparqlParseOptions options, [NotNullWhen(true)] out Query? query, out SparqlParseError error)
    {
        try
        {
            query = Run(utf8, options, static (ref Parser parser) => parser.ParseQueryUnit());
            error = default;
            return true;
        }
        catch (SparqlParseException exception)
        {
            query = null;
            error = exception.Error;
            return false;
        }
    }

    /// <summary>Parses a query from UTF-16; false with the error on the first one.</summary>
    public static bool TryParseQuery(ReadOnlySpan<char> text, SparqlParseOptions options, [NotNullWhen(true)] out Query? query, out SparqlParseError error)
    {
        byte[]? rented = null;

        try
        {
            ReadOnlySpan<byte> utf8 = Transcode(text, ref rented, out SparqlParseError encoding);

            if (encoding.IsError)
            {
                query = null;
                error = encoding;
                return false;
            }

            return TryParseQuery(utf8, options, out query, out error);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private delegate T Parse<T>(ref Parser parser);

    /// <summary>
    /// Runs the parser; when a label the author wrote has the shape of a
    /// generated one, runs it again with a longer prefix, so that generated
    /// labels never collide with written ones (<c>docs/spec/sparql-grammar.md</c> §3.6).
    /// </summary>
    private static T Run<T>(ReadOnlySpan<byte> utf8, SparqlParseOptions options, Parse<T> parse)
    {
        string prefix = "b";

        while (true)
        {
            Parser parser = new(utf8, options, prefix);

            try
            {
                T result = parse(ref parser);

                if (!parser.FreshLabelsMayCollide)
                {
                    return result;
                }

                prefix += "b";
            }
            finally
            {
                parser.Dispose();
            }
        }
    }

    private static ReadOnlySpan<byte> Transcode(ReadOnlySpan<char> text, ref byte[]? rented, out SparqlParseError error)
    {
        rented = ArrayPool<byte>.Shared.Rent(text.Length * 3);
        OperationStatus status = Utf8.FromUtf16(text, rented, out int charsRead, out int bytesWritten, replaceInvalidSequences: false);

        if (status != OperationStatus.Done)
        {
            // The offset is where the transcoded bytes stop: the bad character's position in UTF-8 terms.
            ReadOnlySpan<byte> good = rented.AsSpan(0, bytesWritten);
            int line = 1;
            int lineStart = 0;

            for (int i = 0; i < good.Length; i++)
            {
                if (good[i] == 0x0A || (good[i] == 0x0D && (i + 1 >= good.Length || good[i + 1] != 0x0A)))
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            error = new SparqlParseError(SparqlErrorKind.InvalidEncoding, bytesWritten, line, bytesWritten - lineStart + 1,
                "A UTF-16 surrogate with no partner at character " + charsRead.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
            return default;
        }

        error = default;
        return rented.AsSpan(0, bytesWritten);
    }
}
